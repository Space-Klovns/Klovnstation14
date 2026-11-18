#nullable enable
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Cultivation.Body;
using Content.Server._KS14.Cultivation.Systems;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Body;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Systems;
using Content.Shared.Actions;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._KS14.Cultivation;

/// <summary>
///     Higher realms and stages hit far harder, the Buddha's Palm crushes everyone under it, and Hundred Fists lands its
///         blows one after another.
/// </summary>
[TestFixture]
[TestOf(typeof(CultivationSystem))]
public sealed class CultivationPowerTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: KsTestPowerCultivator
  components:
  - type: Cultivator
- type: entity
  parent: MobHuman
  id: KsTestPowerBody
  components:
  - type: BodyCultivator
    committed: true
";

    [Test]
    public async Task TestRealmPowerGrows()
    {
        await Server.WaitAssertion(() =>
        {
            var previous = 0f;
            foreach (var realm in System.Enum.GetValues<CultivationRealm>())
            {
                var power = CultivationSystem.PowerFor(realm);
                Assert.That(power, Is.GreaterThan(previous), $"{realm} is no stronger than the realm below it.");
                previous = power;
            }

            Assert.That(CultivationSystem.PowerFor(CultivationRealm.NascentSoul), Is.GreaterThanOrEqualTo(3f), "Nascent Soul isn't much stronger.");
        });
    }

    [Test]
    public async Task TestBodyPunchesGrowDeadly()
    {
        await Server.WaitAssertion(() =>
        {
            var bodySystem = SEntMan.System<SharedBodyCultivationSystem>();
            var uid = SEntMan.SpawnEntity("KsTestPowerBody", MapCoordinatesNullspace());
            bodySystem.AdvanceStage(uid);
            var early = SEntMan.GetComponent<BodyCultivatorComponent>(uid).PunchDamage;
            for (var stage = 1; stage < BodyCultivatorComponent.MaxStage; stage++)
                bodySystem.AdvanceStage(uid);

            var late = SEntMan.GetComponent<BodyCultivatorComponent>(uid).PunchDamage;
            Assert.That(late, Is.GreaterThan(early * 10f).And.GreaterThan(40f), "A Primordial Chaos Body's fists are hardly deadlier.");

            // Destruction climbs with every stage, and the ninth stands clearly above the eighth.
            for (var stage = 1; stage < BodyCultivatorComponent.MaxStage; stage++)
                Assert.That(BodyArtSystem.Destruction(stage + 1), Is.GreaterThan(BodyArtSystem.Destruction(stage)), $"Stage {stage + 1} breaks no more than stage {stage}.");
            Assert.That(BodyArtSystem.Destruction(BodyCultivatorComponent.MaxStage),
                Is.GreaterThan(BodyArtSystem.Destruction(BodyCultivatorComponent.MaxStage - 1) * 1.3f), "The ninth stage hardly breaks more than the eighth.");
        });
    }

    [Test]
    public async Task TestBuddhaPalmCrushesEveryoneUnderIt()
    {
        var map = await Pair.CreateTestMap();
        var victims = new EntityUid[3];

        await Server.WaitAssertion(() =>
        {
            var cultivationSystem = SEntMan.System<CultivationSystem>();
            var uid = SEntMan.SpawnEntity("KsTestPowerCultivator", map.GridCoords.Offset(new Vector2(8f, 0f)));
            var cultivator = SEntity<CultivatorComponent>(uid);
            for (var realm = 0; realm < 2; realm++)
                cultivationSystem.BreakThrough(cultivator);

            SEntMan.System<SharedCultivationSystem>().AddQi(uid, 1000f);
            // Three people spread across the palm: none of them more than two tiles from its centre.
            victims[0] = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            victims[1] = SEntMan.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(2f, 0f)));
            victims[2] = SEntMan.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(-1.4f, 1.4f)));
            var ev = new BuddhaPalmActionEvent { Performer = uid, Target = map.GridCoords };
            SEntMan.EventBus.RaiseLocalEvent(uid, ev);
            Assert.That(ev.Handled, Is.True, "The palm wasn't called down.");
        });

        await RunSeconds(1.5f);

        await Server.WaitAssertion(() =>
        {
            var damageableSystem = SEntMan.System<DamageableSystem>();
            using (Assert.EnterMultipleScope())
            {
                foreach (var victimUid in victims)
                {
                    Assert.That(damageableSystem.GetTotalDamage(victimUid), Is.GreaterThanOrEqualTo(FixedPoint2.New(100)),
                        "Someone under a Golden Core palm wasn't crushed.");
                }
            }
        });
    }

    [Test]
    public async Task TestHundredFistsLandOneAfterAnother()
    {
        var map = await Pair.CreateTestMap();
        EntityUid userUid = default;
        EntityUid victimUid = default;

        await Server.WaitAssertion(() =>
        {
            var bodySystem = SEntMan.System<SharedBodyCultivationSystem>();
            userUid = SEntMan.SpawnEntity("KsTestPowerBody", map.GridCoords);
            victimUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(1f, 0f)));
            for (var stage = 0; stage < 3; stage++)
                bodySystem.AdvanceStage(userUid);

            var actionsSystem = SEntMan.System<SharedActionsSystem>();

            // Hundred Fists is past the first few arts, so it waits in the Techniques menu: equip it.
            var loadout = SEntMan.GetComponent<CultivationLoadoutComponent>(userUid);
            foreach (var known in loadout.Learned)
            {
                if (!loadout.Equipped.Contains(known) && SEntMan.GetComponent<MetaDataComponent>(known).EntityPrototype?.ID == "ActionBodyHundredFists")
                    actionsSystem.AddActionDirect(userUid, known);
            }

            foreach (var action in actionsSystem.GetActions(userUid))
            {
                if (SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID != "ActionBodyHundredFists")
                    continue;

                actionsSystem.SetEventTarget(action, victimUid);
                actionsSystem.PerformAction(userUid, action, predicted: false);
            }

            // Nothing has landed yet: the blows come in the following moments.
            Assert.That(SEntMan.System<DamageableSystem>().GetTotalDamage(victimUid), Is.EqualTo(FixedPoint2.Zero), "Every blow landed at once.");
            Assert.That(SEntMan.HasComponent<BodyFlurryComponent>(userUid), Is.True, "No flurry started.");
        });

        await RunTicksSync(3);
        FixedPoint2 early = default;
        await Server.WaitAssertion(() => early = SEntMan.System<DamageableSystem>().GetTotalDamage(victimUid));

        await RunSeconds(1.5f);

        await Server.WaitAssertion(() =>
        {
            var total = SEntMan.System<DamageableSystem>().GetTotalDamage(victimUid);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(early, Is.GreaterThan(FixedPoint2.Zero), "The first blow never landed.");
                Assert.That(total, Is.GreaterThan(early * 3), "The later blows never followed.");
                Assert.That(SEntMan.HasComponent<BodyFlurryComponent>(userUid), Is.False, "The flurry never ended.");
            }
        });
    }

    [Test]
    public async Task TestLoadoutCapsTheBar()
    {
        await Server.WaitAssertion(() =>
        {
            var bodySystem = SEntMan.System<SharedBodyCultivationSystem>();
            var actionsSystem = SEntMan.System<SharedActionsSystem>();
            var uid = SEntMan.SpawnEntity("KsTestPowerBody", MapCoordinatesNullspace());
            for (var stage = 0; stage < BodyCultivatorComponent.MaxStage; stage++)
                bodySystem.AdvanceStage(uid);

            var loadout = SEntMan.GetComponent<CultivationLoadoutComponent>(uid);
            var bar = 0;
            foreach (var action in actionsSystem.GetActions(uid))
            {
                if (loadout.Learned.Contains(action))
                    bar++;
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(loadout.Learned, Has.Count.GreaterThan(loadout.Slots), "A stage 9 body didn't learn more arts than fit on the bar.");
                Assert.That(loadout.Equipped, Has.Count.EqualTo(loadout.Slots), "The bar isn't filled to its slots.");
                Assert.That(loadout.Slots, Is.EqualTo(7), "A Primordial Chaos Body can't ready seven techniques.");
                Assert.That(bar, Is.EqualTo(loadout.Slots), "More techniques sit on the bar than there are slots.");
            }
        });
    }

    [Test]
    public async Task TestTemperedEyesSeeInTheDark()
    {
        await Server.WaitAssertion(() =>
        {
            var bodySystem = SEntMan.System<SharedBodyCultivationSystem>();
            var uid = SEntMan.SpawnEntity("KsTestPowerBody", MapCoordinatesNullspace());
            var body = SEntMan.GetComponent<BodyCultivatorComponent>(uid);
            bodySystem.SetLevel((uid, body), BodyGroup.Eyes, 1);
            bodySystem.Recalculate((uid, body));
            Assert.That(SEntMan.TryGetComponent<Content.Shared._Mono.Overlays.PhosphorNightVisionComponent>(uid, out var clear), Is.True,
                "Clear Eyes gave no night vision.");
            Assert.That(clear!.ActionEntity, Is.Not.Null, "There's no button to turn the night vision on.");
            var weak = clear.Amplification;

            bodySystem.SetLevel((uid, body), BodyGroup.Eyes, 3);
            bodySystem.Recalculate((uid, body));
            Assert.That(SEntMan.GetComponent<Content.Shared._Mono.Overlays.PhosphorNightVisionComponent>(uid).Amplification, Is.GreaterThan(weak),
                "Hunter's Sight didn't sharpen the night vision.");
        });
    }

    private static Robust.Shared.Map.MapCoordinates MapCoordinatesNullspace() => Robust.Shared.Map.MapCoordinates.Nullspace;
}
