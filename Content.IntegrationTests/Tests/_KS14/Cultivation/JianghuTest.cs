using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Cultivation.Components;
using Content.Server._KS14.Cultivation.Systems;
using Content.Shared._KS14.Cultivation.Jianghu;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mind;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._KS14.Cultivation;

/// <summary>
///     Face and honor duels.
/// </summary>
[TestFixture]
[TestOf(typeof(JianghuSystem))]
public sealed class JianghuTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    private EntityUid SpawnPerson(Robust.Shared.Map.EntityCoordinates coordinates)
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
    public void TestFaceTitles()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JianghuSystem.FaceTitle(0), Is.Null, "A nobody had a title.");
            Assert.That(JianghuSystem.FaceTitle(5), Is.EqualTo("jianghu-title-rising"));
            Assert.That(JianghuSystem.FaceTitle(30), Is.EqualTo("jianghu-title-legend"));
            Assert.That(JianghuSystem.FaceTitle(-10), Is.EqualTo("jianghu-title-disgraced"));
        }
    }

    [Test]
    public async Task TestBeatenDuelistLosesFaceAndStake()
    {
        var map = await Pair.CreateTestMap();
        EntityUid winnerUid = default;
        EntityUid loserUid = default;
        EntityUid stakeUid = default;

        await Server.WaitAssertion(() =>
        {
            var jianghuSystem = SEntMan.System<JianghuSystem>();
            winnerUid = SpawnPerson(map.GridCoords);
            loserUid = SpawnPerson(map.GridCoords);
            stakeUid = SEntMan.SpawnEntity("Crowbar", map.GridCoords);
            SEntMan.System<SharedHandsSystem>().PickupOrDrop(loserUid, stakeUid, checkActionBlocker: false);

            jianghuSystem.HoldStake(loserUid);
            jianghuSystem.StartDuel(winnerUid, loserUid);
            SEntMan.System<DamageableSystem>().TryChangeDamage(loserUid, Blunt(80), ignoreResistances: true, origin: winnerUid);
        });

        await RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            var jianghuSystem = SEntMan.System<JianghuSystem>();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(jianghuSystem.GetFace(winnerUid), Is.EqualTo(5), "The winner gained no face.");
                Assert.That(jianghuSystem.GetFace(loserUid), Is.EqualTo(-2), "The loser lost no face.");
                Assert.That(jianghuSystem.InDuel(winnerUid) || jianghuSystem.InDuel(loserUid), Is.False, "The duel outlived its result.");
                Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(winnerUid, stakeUid), Is.True, "The winner did not take the stake.");
                Assert.That(jianghuSystem.IsDishonored(winnerUid), Is.False, "Fighting your own duel was counted as interference.");
            }
        });
    }

    [Test]
    public async Task TestInterferenceDisgraces()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var jianghuSystem = SEntMan.System<JianghuSystem>();
            var oneUid = SpawnPerson(map.GridCoords);
            var twoUid = SpawnPerson(map.GridCoords);
            var meddlerUid = SpawnPerson(map.GridCoords);

            jianghuSystem.StartDuel(oneUid, twoUid);
            SEntMan.System<DamageableSystem>().TryChangeDamage(oneUid, Blunt(5), ignoreResistances: true, origin: meddlerUid);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(jianghuSystem.IsDishonored(meddlerUid), Is.True, "Interfering in a duel was not shameful.");
                Assert.That(jianghuSystem.GetFace(meddlerUid), Is.EqualTo(-5));
            }
        });
    }

    [Test]
    public async Task TestDrawIsHonorable()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var jianghuSystem = SEntMan.System<JianghuSystem>();
            var oneUid = SpawnPerson(map.GridCoords);
            var twoUid = SpawnPerson(map.GridCoords);

            jianghuSystem.StartDuel(oneUid, twoUid);
            jianghuSystem.FinishDuel(oneUid, twoUid, winnerUid: null, outcome: HonorDuelOutcome.Draw);
            jianghuSystem.FinishDuel(oneUid, twoUid, winnerUid: oneUid, outcome: HonorDuelOutcome.Yielded);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(jianghuSystem.GetFace(oneUid), Is.EqualTo(1), "A decided duel was decided again.");
                Assert.That(jianghuSystem.GetFace(twoUid), Is.EqualTo(1));
            }
        });
    }
}
