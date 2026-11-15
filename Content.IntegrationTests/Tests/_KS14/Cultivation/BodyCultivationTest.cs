using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Cultivation.Body;
using Content.Shared._KS14.Cultivation.Body;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Slippery;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._KS14.Cultivation;

/// <summary>
///     The Body Molding Art: forging tempering into the body, the stage caps, the Tribulation of Flesh, and the
///         powers tempered parts grant.
/// </summary>
[TestFixture]
[TestOf(typeof(BodyCultivationSystem))]
[TestOf(typeof(SharedBodyCultivationSystem))]
public sealed class BodyCultivationTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: KsTestBodyCultivator
  components:
  - type: BodyCultivator

- type: entity
  parent: MobHuman
  id: KsTestBodyCultivatorCommitted
  components:
  - type: BodyCultivator
    committed: true
";

    private static DamageSpecifier Blunt(float amount)
    {
        var damage = new DamageSpecifier();
        damage.DamageDict.Add("Blunt", FixedPoint2.New(amount));
        return damage;
    }

    [Test]
    public async Task TestForgingFillsEveryPartUpToTheCapAndBanksTheRest()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var bodySystem = SEntMan.System<SharedBodyCultivationSystem>();
            var uid = SEntMan.SpawnEntity("KsTestBodyCultivator", map.GridCoords);
            var body = SEntity<BodyCultivatorComponent>(uid);

            bodySystem.AddTempering(uid, 500f);
            Assert.That(body.Comp.Tempering, Is.EqualTo(body.Comp.TemperingCap), "Tempering went past its cap.");

            while (bodySystem.WeakestGroup(body.Comp) is { } group)
                Assert.That(bodySystem.Forge(uid, group, 100f), Is.GreaterThan(0), $"Forging {group} gained nothing.");

            using (Assert.EnterMultipleScope())
            {
                foreach (var group in SharedBodyCultivationSystem.AllGroups)
                    Assert.That(SharedBodyCultivationSystem.GetLevel(body.Comp, group), Is.EqualTo(1), $"{group} was not forged to the cap.");

                // Ten parts at three tempering each, the rest banked.
                Assert.That(body.Comp.Tempering, Is.EqualTo(body.Comp.TemperingCap - 10 * SharedBodyCultivationSystem.PartCost(1)).Within(0.01f),
                    "Forging past the cap took tempering it couldn't use.");
                Assert.That(bodySystem.ReadyForTribulation(body.Comp), Is.True, "Every part forged past stage 0 did not open the tribulation.");
            }
        });
    }

    [Test]
    public async Task TestTribulationAndTheMortalCap()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var bodySystem = SEntMan.System<SharedBodyCultivationSystem>();
            var bodyCultivationSystem = SEntMan.System<BodyCultivationSystem>();
            var thresholdSystem = SEntMan.System<MobThresholdSystem>();
            var uid = SEntMan.SpawnEntity("KsTestBodyCultivator", map.GridCoords);
            var body = SEntity<BodyCultivatorComponent>(uid);

            Assert.That(thresholdSystem.TryGetThresholdForState(uid, MobState.Critical, out var criticalBefore), Is.True);

            foreach (var group in SharedBodyCultivationSystem.AllGroups)
                bodySystem.SetLevel(uid, group, 1);

            bodyCultivationSystem.SucceedTribulation(body);

            Assert.That(thresholdSystem.TryGetThresholdForState(uid, MobState.Critical, out var criticalAfter), Is.True);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(body.Comp.Stage, Is.EqualTo(1), "The tribulation did not raise the stage.");
                Assert.That(criticalAfter!.Value.Float(), Is.GreaterThanOrEqualTo(criticalBefore!.Value.Float() + 10f),
                    "Copper Skin did not toughen the body.");
                Assert.That(bodySystem.HasTechnique(body.Comp, "ActionBodyBreathOfRenewal"), Is.True, "Copper Skin did not open Breath of Renewal.");
                Assert.That(SharedBodyCultivationSystem.PartCap(body.Comp), Is.EqualTo(1), "A mortal's parts can be forged past Copper Skin.");
                Assert.That(bodySystem.ReadyForTribulation(body.Comp), Is.False, "A mortal can go past Copper Skin.");
            }

            bodySystem.Commit(uid);
            Assert.That(SharedBodyCultivationSystem.PartCap(body.Comp), Is.EqualTo(2), "Committing did not open the path beyond Copper Skin.");
        });
    }

    [Test]
    public async Task TestTemperedPartsGrantPowers()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var bodySystem = SEntMan.System<SharedBodyCultivationSystem>();
            var damageableSystem = SEntMan.System<DamageableSystem>();
            var controlUid = SEntMan.SpawnEntity("KsTestBodyCultivatorCommitted", map.GridCoords);
            var uid = SEntMan.SpawnEntity("KsTestBodyCultivatorCommitted", map.GridCoords);

            bodySystem.SetLevel(uid, BodyGroup.Chest, 9);
            bodySystem.SetLevel(uid, BodyGroup.Legs, 8);

            damageableSystem.TryChangeDamage(controlUid, Blunt(20));
            damageableSystem.TryChangeDamage(uid, Blunt(20));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(damageableSystem.GetTotalDamage(uid).Float(), Is.EqualTo(damageableSystem.GetTotalDamage(controlUid).Float() * 0.65f).Within(0.05f),
                    "Never Wounded did not take the best brute resistance of the chest's powers.");
                Assert.That(SEntMan.HasComponent<NoSlipComponent>(uid), Is.True, "Unslippable legs did not stop slipping.");
                Assert.That(SComp<BodyCultivatorComponent>(uid).Powers, Does.Contain(new Robust.Shared.Prototypes.ProtoId<BodyTemperingPowerPrototype>("KsBodyLegsUnslippable")));
            }
        });
    }

    [Test]
    public async Task TestBodyArtsCostExhaustion()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var bodySystem = SEntMan.System<SharedBodyCultivationSystem>();
            var uid = SEntMan.SpawnEntity("KsTestBodyCultivatorCommitted", map.GridCoords);
            var body = SComp<BodyCultivatorComponent>(uid);

            var ev = new BodyIronShirtActionEvent { Performer = uid };
            SEntMan.EventBus.RaiseLocalEvent(uid, ev);
            Assert.That(ev.Handled, Is.True, "Iron Shirt did not go off.");
            Assert.That(body.Exhaustion, Is.EqualTo(ev.Exhaustion).Within(0.01f), "Iron Shirt cost no exhaustion.");
            Assert.That(bodySystem.InVajra(uid), Is.False);

            bodySystem.AddExhaustion(uid, body.MaxExhaustion);
            var tooTired = new BodyIronShirtActionEvent { Performer = uid };
            SEntMan.EventBus.RaiseLocalEvent(uid, tooTired);
            Assert.That(tooTired.Handled, Is.False, "A body art went off past full exhaustion.");
        });
    }

    [Test]
    public async Task TestEternalHeartRestartsTheBody()
    {
        var map = await Pair.CreateTestMap();
        EntityUid uid = default;

        await Server.WaitPost(() =>
        {
            uid = SEntMan.SpawnEntity("KsTestBodyCultivatorCommitted", map.GridCoords);
            SEntMan.System<SharedBodyCultivationSystem>().SetLevel(uid, BodyGroup.Heart, 9);
            // Past death but well short of being gibbed: a deleted body would read as "not dead" and prove nothing.
            SEntMan.System<DamageableSystem>().TryChangeDamage(uid, Blunt(240), ignoreResistances: true);
            Assert.That(SEntMan.System<MobStateSystem>().IsDead(uid), Is.True, "The test body did not die.");
        });

        await RunSeconds(7);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityExists(uid), Is.True, "The body was destroyed, so the test proves nothing.");
            Assert.That(SEntMan.System<MobStateSystem>().IsDead(uid), Is.False, "The Eternal Heart did not restart the body.");
        });
    }
}
