using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Cultivation.Components;
using Content.Server._KS14.Cultivation.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mind;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._KS14.Cultivation;

/// <summary>
///     The oath of brotherhood: who can swear, fighting together, grief, and betrayal.
/// </summary>
[TestFixture]
[TestOf(typeof(JianghuSystem))]
public sealed class SwornBrotherhoodTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    private EntityUid SpawnPerson(EntityCoordinates coordinates)
    {
        var mindSystem = SEntMan.System<SharedMindSystem>();
        var personUid = SEntMan.SpawnEntity("MobHuman", coordinates);
        mindSystem.TransferTo(mindSystem.CreateMind(null).Owner, personUid);
        return personUid;
    }

    private static DamageSpecifier Blunt(float amount)
    {
        var damage = new DamageSpecifier();
        damage.DamageDict.Add("Blunt", FixedPoint2.New(amount));
        return damage;
    }

    [Test]
    public async Task TestOathBindsThreeAtMost()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var jianghuSystem = SEntMan.System<JianghuSystem>();
            var liuUid = SpawnPerson(map.GridCoords);
            var guanUid = SpawnPerson(map.GridCoords);
            var zhangUid = SpawnPerson(map.GridCoords);
            var latecomerUid = SpawnPerson(map.GridCoords);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(jianghuSystem.TrySwear(liuUid, guanUid), Is.True);
                Assert.That(jianghuSystem.TrySwear(liuUid, zhangUid), Is.True, "A third could not join the oath.");
                Assert.That(jianghuSystem.TrySwear(zhangUid, latecomerUid), Is.False, "An oath bound four.");
                Assert.That(jianghuSystem.AreSworn(guanUid, zhangUid), Is.True, "Joining one sibling did not bind you to the rest.");
            }
        });
    }

    [Test]
    public async Task TestBetrayalBreaksTheOath()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var jianghuSystem = SEntMan.System<JianghuSystem>();
            var damageableSystem = SEntMan.System<DamageableSystem>();
            var betrayerUid = SpawnPerson(map.GridCoords);
            var betrayedUid = SpawnPerson(map.GridCoords);
            var strangerUid = SpawnPerson(map.GridCoords);
            jianghuSystem.TrySwear(betrayerUid, betrayedUid);

            for (var strike = 0; strike < 2; strike++)
                damageableSystem.TryChangeDamage(betrayedUid, Blunt(2), ignoreResistances: true, origin: betrayerUid);
            Assert.That(jianghuSystem.AreSworn(betrayerUid, betrayedUid), Is.True, "Two strikes broke the oath.");

            damageableSystem.TryChangeDamage(betrayedUid, Blunt(2), ignoreResistances: true, origin: betrayerUid);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(jianghuSystem.IsOathbreaker(betrayerUid), Is.True, "Striking a sibling three times was no betrayal.");
                Assert.That(jianghuSystem.GetFace(betrayerUid), Is.EqualTo(-10));
                Assert.That(jianghuSystem.GetBond(betrayedUid), Is.Null, "The broken bond lingered.");
                Assert.That(jianghuSystem.TrySwear(betrayerUid, strangerUid), Is.False, "An oathbreaker swore a second oath in one round.");
            }
        });
    }

    [Test]
    public async Task TestTogetherAndGrief()
    {
        var map = await Pair.CreateTestMap();
        EntityUid survivorUid = default;
        EntityUid fallenUid = default;

        await Server.WaitAssertion(() =>
        {
            survivorUid = SpawnPerson(map.GridCoords);
            fallenUid = SpawnPerson(map.GridCoords);
            SEntMan.System<JianghuSystem>().TrySwear(survivorUid, fallenUid);
        });

        await RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<SwornTogetherComponent>(survivorUid), Is.True, "Siblings side by side did not fight together.");
            SEntMan.System<DamageableSystem>().TryChangeDamage(fallenUid, Blunt(300), ignoreResistances: true);
            Assert.That(SEntMan.HasComponent<SwornVengeanceComponent>(survivorUid), Is.True, "A sibling's death stirred no fury.");
        });
    }
}
