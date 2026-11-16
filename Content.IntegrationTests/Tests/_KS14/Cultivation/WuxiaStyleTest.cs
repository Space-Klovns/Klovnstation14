using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Cultivation.Systems;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._KS14.Cultivation;

/// <summary>
///     The wuxia fighting styles: learning one from its manual, and streaks landing combos.
/// </summary>
[TestFixture]
[TestOf(typeof(WuxiaStyleSystem))]
public sealed class WuxiaStyleTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    [Test]
    public async Task TestManualTeachesStyleAndDuel()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var martialArtsSystem = SEntMan.System<SharedMartialArtsSystem>();
            var studentUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);

            Assert.That(martialArtsSystem.TryLearn(studentUid, "KsWingChun"), Is.EqualTo(MartialArtLearnResult.Learned));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SComp<WuxiaStyleComponent>(studentUid).Style, Is.EqualTo(WuxiaStyle.WingChun));
                Assert.That(martialArtsSystem.HasArtAction(studentUid, "ActionJianghuDuel"), Is.True, "A martial artist was not taught to duel.");
            }
        });
    }

    [Test]
    public async Task TestStreaksLandCombos()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var styleSystem = SEntMan.System<WuxiaStyleSystem>();
            var damageableSystem = SEntMan.System<DamageableSystem>();
            var clawUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var chunUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var rakedUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var centeredUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var clawEntity = new Entity<WuxiaStyleComponent>(clawUid, SEntMan.AddComponent<WuxiaStyleComponent>(clawUid));
            clawEntity.Comp.Style = WuxiaStyle.EagleClaw;
            var chunEntity = new Entity<WuxiaStyleComponent>(chunUid, SEntMan.AddComponent<WuxiaStyleComponent>(chunUid));
            chunEntity.Comp.Style = WuxiaStyle.WingChun;

            // Two punches are just punches; the third is the Claw Rake.
            styleSystem.AddToStreak(clawEntity, rakedUid, 'H');
            styleSystem.AddToStreak(clawEntity, rakedUid, 'H');
            Assert.That(damageableSystem.GetTotalDamage(rakedUid), Is.EqualTo(FixedPoint2.Zero), "A combo landed early.");
            styleSystem.AddToStreak(clawEntity, rakedUid, 'H');

            // Switching opponents starts the streak over.
            styleSystem.AddToStreak(chunEntity, rakedUid, 'H');
            styleSystem.AddToStreak(chunEntity, centeredUid, 'D');
            Assert.That(SComp<StaminaComponent>(centeredUid).StaminaDamage, Is.Zero, "A streak carried over to a new opponent.");
            styleSystem.AddToStreak(chunEntity, centeredUid, 'H');
            styleSystem.AddToStreak(chunEntity, centeredUid, 'D');

            using (Assert.EnterMultipleScope())
            {
                Assert.That(damageableSystem.GetTotalDamage(rakedUid), Is.GreaterThanOrEqualTo(FixedPoint2.New(18)), "Three punches did not rake.");
                Assert.That(SComp<StaminaComponent>(centeredUid).StaminaDamage, Is.GreaterThanOrEqualTo(30f), "Punch, shove was no Centerline Strike.");
            }
        });
    }

    [Test]
    public async Task TestStickyHandsTurnsABlow()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var styleSystem = SEntMan.System<WuxiaStyleSystem>();
            var damageableSystem = SEntMan.System<DamageableSystem>();
            var chunUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var attackerUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var chunEntity = new Entity<WuxiaStyleComponent>(chunUid, SEntMan.AddComponent<WuxiaStyleComponent>(chunUid));
            chunEntity.Comp.Style = WuxiaStyle.WingChun;

            styleSystem.AddToStreak(chunEntity, attackerUid, 'D');
            styleSystem.AddToStreak(chunEntity, attackerUid, 'D');

            var blow = new DamageSpecifier();
            blow.DamageDict.Add("Blunt", FixedPoint2.New(10));
            damageableSystem.TryChangeDamage(chunUid, blow, origin: attackerUid);
            Assert.That(damageableSystem.GetTotalDamage(chunUid), Is.EqualTo(FixedPoint2.Zero), "Sticky Hands did not turn the blow aside.");

            damageableSystem.TryChangeDamage(chunUid, blow, origin: attackerUid);
            Assert.That(damageableSystem.GetTotalDamage(chunUid), Is.GreaterThan(FixedPoint2.Zero), "Sticky Hands turned aside more than one blow.");
        });
    }
}
