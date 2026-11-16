using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Cultivation.Systems;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Movement.Components;
using Content.Shared.Projectiles;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Cultivation;

/// <summary>
///     Cultivation: qi recovery, internal injury, manuals and law slots, breakthroughs and deviations, and the
///         defensive techniques.
/// </summary>
[TestFixture]
[TestOf(typeof(CultivationSystem))]
[TestOf(typeof(SharedCultivationSystem))]
public sealed class CultivationTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: KsTestCultivator
  components:
  - type: Cultivator

- type: entity
  parent: MobHuman
  id: KsTestCultivatorWithManual
  components:
  - type: Cultivator
    startingManuals:
    - CultivationManualFire
";

    private static readonly EntProtoId FlyingSword = "ActionCultivationFlyingSword";
    private static readonly EntProtoId SwordRain = "ActionCultivationSwordRain";
    private static readonly EntProtoId StillWater = "ActionCultivationStillWaterWard";
    private static readonly EntProtoId TidalSurge = "ActionCultivationTidalSurge";

    private static DamageSpecifier Blunt(float amount)
    {
        var damage = new DamageSpecifier();
        damage.DamageDict.Add("Blunt", FixedPoint2.New(amount));
        return damage;
    }

    [Test]
    public async Task TestHeavyBlowsCauseInternalInjury()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var damageableSystem = SEntMan.System<DamageableSystem>();
            var cultivatorUid = SEntMan.SpawnEntity("KsTestCultivator", map.GridCoords);
            var cultivatorComponent = SEntMan.GetComponent<CultivatorComponent>(cultivatorUid);

            damageableSystem.TryChangeDamage(cultivatorUid, Blunt(10), ignoreResistances: true);
            Assert.That(cultivatorComponent.InternalInjury, Is.Zero, "A light blow bruised the meridians.");

            damageableSystem.TryChangeDamage(cultivatorUid, Blunt(20), ignoreResistances: true);
            Assert.That(cultivatorComponent.InternalInjury, Is.EqualTo(1), "A heavy blow did not cause an internal injury.");

            for (var i = 0; i < 5; i++)
                damageableSystem.TryChangeDamage(cultivatorUid, Blunt(16), ignoreResistances: true);

            Assert.That(cultivatorComponent.InternalInjury, Is.EqualTo(cultivatorComponent.MaxInternalInjury),
                "Internal injury did not stop at its cap.");

            SEntMan.DeleteEntity(cultivatorUid);
        });
    }

    [Test]
    public async Task TestInternalInjurySlowsQiRecovery()
    {
        var map = await Pair.CreateTestMap();
        EntityUid healthyUid = default;
        EntityUid injuredUid = default;

        await Server.WaitPost(() =>
        {
            var cultivationSystem = SEntMan.System<SharedCultivationSystem>();
            healthyUid = SEntMan.SpawnEntity("KsTestCultivator", map.GridCoords);
            injuredUid = SEntMan.SpawnEntity("KsTestCultivator", map.GridCoords);

            foreach (var uid in new[] { healthyUid, injuredUid })
                cultivationSystem.TrySpendQi(uid, SEntMan.GetComponent<CultivatorComponent>(uid).Qi);

            cultivationSystem.AddInternalInjury(injuredUid, stacks: 2);
        });

        await RunSeconds(10);

        await Server.WaitAssertion(() =>
        {
            var healthyQi = SEntMan.GetComponent<CultivatorComponent>(healthyUid).Qi;
            var injuredQi = SEntMan.GetComponent<CultivatorComponent>(injuredUid).Qi;

            Assert.That(healthyQi, Is.GreaterThan(0f), "Qi did not recover at all.");
            Assert.That(injuredQi, Is.EqualTo(healthyQi * 0.5f).Within(1.01f),
                "Two stacks of internal injury did not halve qi recovery.");
        });
    }

    [Test]
    public async Task TestManualsLawSlotsAndTechniques()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var cultivationSystem = SEntMan.System<CultivationSystem>();
            var sharedCultivationSystem = SEntMan.System<SharedCultivationSystem>();

            var cultivatorUid = SEntMan.SpawnEntity("KsTestCultivator", map.GridCoords);
            var cultivatorEntity = SEntity<CultivatorComponent>(cultivatorUid);
            var metal = SEntity<CultivationManualComponent>(SEntMan.SpawnEntity("CultivationManualMetal", map.GridCoords));
            var water = SEntity<CultivationManualComponent>(SEntMan.SpawnEntity("CultivationManualWater", map.GridCoords));
            var fire = SEntity<CultivationManualComponent>(SEntMan.SpawnEntity("CultivationManualFire", map.GridCoords));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(cultivationSystem.TryLearnFromManual(cultivatorUid, metal), Is.True, "Could not learn a first law.");
                Assert.That(sharedCultivationSystem.HasRoot(cultivatorUid, CultivationElement.Metal), Is.True, "The first law learned is not the Root Law.");
                Assert.That(sharedCultivationSystem.HasTechnique(cultivatorEntity.Comp, FlyingSword), Is.True, "The Root Law's first technique was not granted.");
                Assert.That(sharedCultivationSystem.HasTechnique(cultivatorEntity.Comp, SwordRain), Is.False, "A Golden Core technique was granted at Qi Condensation.");
                Assert.That(cultivationSystem.TryLearnFromManual(cultivatorUid, water), Is.False, "A second law fit before Foundation Establishment.");
            }

            cultivationSystem.BreakThrough(cultivatorEntity);
            Assert.That(cultivatorEntity.Comp.Realm, Is.EqualTo(CultivationRealm.FoundationEstablishment));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(cultivationSystem.TryLearnFromManual(cultivatorUid, water), Is.True, "Foundation Establishment did not open a second law slot.");
                Assert.That(sharedCultivationSystem.HasTechnique(cultivatorEntity.Comp, StillWater), Is.True, "The Complementary Law's technique was not granted.");
                Assert.That(sharedCultivationSystem.HasRoot(cultivatorUid, CultivationElement.Water), Is.False, "The Complementary Law replaced the Root Law.");
                Assert.That(cultivationSystem.TryLearnFromManual(cultivatorUid, fire), Is.False, "A third law fit.");
            }

            cultivationSystem.BreakThrough(cultivatorEntity);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(cultivatorEntity.Comp.Realm, Is.EqualTo(CultivationRealm.GoldenCore));
                Assert.That(sharedCultivationSystem.HasTechnique(cultivatorEntity.Comp, SwordRain), Is.True, "Golden Core did not grant the Root Law's Golden Core technique.");
                Assert.That(sharedCultivationSystem.HasTechnique(cultivatorEntity.Comp, TidalSurge), Is.True, "Golden Core did not grant the Complementary Law's Golden Core technique.");
                Assert.That(cultivatorEntity.Comp.Qi, Is.EqualTo(sharedCultivationSystem.GetMaxQi(cultivatorEntity.Comp)), "A breakthrough did not refill qi.");
            }

            SEntMan.DeleteEntity(cultivatorUid);
        });
    }

    [Test]
    public async Task TestDeviationCostsInsightAndInjures()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var cultivationSystem = SEntMan.System<CultivationSystem>();
            var sharedCultivationSystem = SEntMan.System<SharedCultivationSystem>();
            var cultivatorUid = SEntMan.SpawnEntity("KsTestCultivator", map.GridCoords);
            var cultivatorEntity = SEntity<CultivatorComponent>(cultivatorUid);

            sharedCultivationSystem.AddInsight(cultivatorUid, 100f);
            Assert.That(sharedCultivationSystem.ReadyToBreakThrough(cultivatorEntity.Comp), Is.True);

            cultivationSystem.Deviate(cultivatorEntity);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(cultivatorEntity.Comp.Realm, Is.EqualTo(CultivationRealm.QiCondensation), "A deviation still broke through.");
                Assert.That(cultivatorEntity.Comp.Insight, Is.EqualTo(50f).Within(0.01f), "A deviation did not scatter half the insight.");
                Assert.That(cultivatorEntity.Comp.InternalInjury, Is.EqualTo(1), "A deviation did not injure the meridians.");
            }

            SEntMan.DeleteEntity(cultivatorUid);
        });
    }

    [Test]
    public async Task TestMeditationGathersInsightAndHealsInjury()
    {
        var map = await Pair.CreateTestMap();
        EntityUid cultivatorUid = default;

        await Server.WaitPost(() =>
        {
            cultivatorUid = SEntMan.SpawnEntity("KsTestCultivator", map.GridCoords);
            SEntMan.System<SharedCultivationSystem>().AddInternalInjury(cultivatorUid, stacks: 1);
            Assert.That(SEntMan.System<CultivationSystem>().StartMeditation(SEntity<CultivatorComponent>(cultivatorUid)), Is.True,
                "Meditation did not start.");
        });

        // Two full cycles: the second heals the injury.
        await RunSeconds(17);

        await Server.WaitAssertion(() =>
        {
            var cultivatorComponent = SEntMan.GetComponent<CultivatorComponent>(cultivatorUid);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(cultivatorComponent.Insight, Is.EqualTo(cultivatorComponent.MeditateInsight * 2).Within(0.01f),
                    "Two meditation cycles did not gather two cycles of insight.");
                Assert.That(cultivatorComponent.InternalInjury, Is.Zero, "Two meditation cycles did not heal an internal injury.");
            }
        });
    }

    [Test]
    public async Task TestTechniquesNeedQi()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var cultivationSystem = SEntMan.System<CultivationSystem>();
            var cultivatorUid = SEntMan.SpawnEntity("KsTestCultivator", map.GridCoords);
            var cultivatorComponent = SEntMan.GetComponent<CultivatorComponent>(cultivatorUid);
            var qi = cultivatorComponent.Qi;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(cultivationSystem.TryUseTechnique(cultivatorUid, qi + 1f), Is.False, "A technique went off without enough qi.");
                Assert.That(cultivatorComponent.Qi, Is.EqualTo(qi), "A failed technique still spent qi.");
                Assert.That(cultivationSystem.TryUseTechnique(cultivatorUid, 10f), Is.True);
                Assert.That(cultivatorComponent.Qi, Is.EqualTo(qi - 10f).Within(0.01f), "A technique did not spend its qi.");
            }

            SEntMan.DeleteEntity(cultivatorUid);
        });
    }

    [Test]
    public async Task TestDefensiveTechniques()
    {
        var map = await Pair.CreateTestMap();
        EntityUid controlUid = default;
        EntityUid wardedUid = default;
        EntityUid rootedUid = default;
        var baseWalkSpeed = 0f;

        await Server.WaitPost(() =>
        {
            var stanceSystem = SEntMan.System<CultivationStanceSystem>();
            controlUid = SEntMan.SpawnEntity("KsTestCultivator", map.GridCoords);
            wardedUid = SEntMan.SpawnEntity("KsTestCultivator", map.GridCoords);
            rootedUid = SEntMan.SpawnEntity("KsTestCultivator", map.GridCoords);
            baseWalkSpeed = SEntMan.GetComponent<MovementSpeedModifierComponent>(rootedUid).CurrentWalkSpeed;

            stanceSystem.ApplyWard(wardedUid, System.TimeSpan.FromSeconds(1), 0.5f, releaseWave: false);
            stanceSystem.ApplyRootedStance(rootedUid, System.TimeSpan.FromSeconds(1), 0.75f, 0.6f);
        });

        await Server.WaitAssertion(() =>
        {
            // Resistances on: the stances work by modifying damage, which ignoring resistances skips entirely.
            //      The control takes the same blow, so the body's own resistances cancel out.
            var damageableSystem = SEntMan.System<DamageableSystem>();
            foreach (var uid in new[] { controlUid, wardedUid, rootedUid })
                damageableSystem.TryChangeDamage(uid, Blunt(10));

            var control = damageableSystem.GetTotalDamage(controlUid).Float();
            Assert.That(control, Is.GreaterThan(0f), "The control took no damage; the comparison would prove nothing.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(damageableSystem.GetTotalDamage(wardedUid).Float(), Is.EqualTo(control * 0.5f).Within(0.01f),
                    "Still Water Ward did not halve the blow.");
                Assert.That(damageableSystem.GetTotalDamage(rootedUid).Float(), Is.EqualTo(control * 0.75f).Within(0.01f),
                    "Rooted Mountain Stance did not soften the blow.");
                Assert.That(SEntMan.GetComponent<MovementSpeedModifierComponent>(rootedUid).CurrentWalkSpeed,
                    Is.EqualTo(baseWalkSpeed * 0.6f).Within(0.01f), "Rooted Mountain Stance did not slow its user.");
            }
        });

        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.HasComponent<StillWaterWardComponent>(wardedUid), Is.False, "Still Water Ward never ended.");
                Assert.That(SEntMan.HasComponent<RootedStanceComponent>(rootedUid), Is.False, "Rooted Mountain Stance never ended.");
                Assert.That(SEntMan.GetComponent<MovementSpeedModifierComponent>(rootedUid).CurrentWalkSpeed,
                    Is.EqualTo(baseWalkSpeed).Within(0.01f), "Leaving Rooted Mountain Stance did not restore speed.");
            }
        });
    }

    [Test]
    public async Task TestNewCultivatorGetsActionsAndManual()
    {
        var map = await Pair.CreateTestMap();
        EntityUid cultivatorUid = default;

        await Server.WaitPost(() => cultivatorUid = SEntMan.SpawnEntity("KsTestCultivatorWithManual", map.GridCoords));

        // The manual is handed over on the next tick, once the hands exist.
        await RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            var cultivatorComponent = SEntMan.GetComponent<CultivatorComponent>(cultivatorUid);
            var held = SEntMan.System<SharedHandsSystem>().EnumerateHeld(cultivatorUid).ToList();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(cultivatorComponent.MeditateActionEntity, Is.Not.Null, "No meditate action was granted.");
                Assert.That(cultivatorComponent.BreakthroughActionEntity, Is.Not.Null, "No breakthrough action was granted.");
                Assert.That(held.Count(SEntMan.HasComponent<CultivationManualComponent>), Is.EqualTo(1),
                    "Exactly one starting manual was not put in hand.");
            }
        });
    }

    [Test]
    public async Task TestSwordQiDealsOnlySlash()
    {
        await Server.WaitAssertion(() =>
        {
            var bladeUid = SEntMan.SpawnEntity("ProjectileCultivationSwordQi", MapCoordinates.Nullspace);
            var damage = SEntMan.GetComponent<ProjectileComponent>(bladeUid).Damage;

            Assert.That(damage.DamageDict.Keys, Is.EquivalentTo(new[] { "Slash" }),
                "Sword qi inherited its parent bullet's damage types.");
            SEntMan.DeleteEntity(bladeUid);
        });
    }

    [Test]
    public async Task TestBreakthroughReadiness()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var cultivationSystem = SEntMan.System<CultivationSystem>();
            var sharedCultivationSystem = SEntMan.System<SharedCultivationSystem>();
            var cultivatorUid = SEntMan.SpawnEntity("KsTestCultivator", map.GridCoords);
            var cultivatorEntity = SEntity<CultivatorComponent>(cultivatorUid);

            var baseline = cultivationSystem.BreakthroughReadiness(cultivatorEntity);
            sharedCultivationSystem.AddInternalInjury(cultivatorUid, stacks: 1);
            var injured = cultivationSystem.BreakthroughReadiness(cultivatorEntity);
            sharedCultivationSystem.SetDaoHeartTempered(cultivatorUid, true);
            var tempered = cultivationSystem.BreakthroughReadiness(cultivatorEntity);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(injured, Is.EqualTo(baseline - cultivatorEntity.Comp.ReadinessPerInjury), "Internal injury did not weigh on readiness.");
                Assert.That(tempered, Is.EqualTo(injured + 15), "A tempered Dao heart did not steady the breakthrough.");
            }
        });
    }

    [Test]
    public async Task TestBreakthroughEndsInTheNextRealm()
    {
        var map = await Pair.CreateTestMap();
        EntityUid cultivatorUid = default;

        await Server.WaitPost(() =>
        {
            var sharedCultivationSystem = SEntMan.System<SharedCultivationSystem>();
            cultivatorUid = SEntMan.SpawnEntity("KsTestCultivator", map.GridCoords);
            var cultivatorEntity = SEntity<CultivatorComponent>(cultivatorUid);

            // Full insight, a full dantian and a tempered Dao heart: a stable breakthrough.
            sharedCultivationSystem.AddInsight(cultivatorUid, 1000f);
            sharedCultivationSystem.AddQi(cultivatorUid, 1000f);
            sharedCultivationSystem.SetDaoHeartTempered(cultivatorUid, true);
            Assert.That(SEntMan.System<CultivationSystem>().BreakthroughReadiness(cultivatorEntity), Is.GreaterThanOrEqualTo(70));
            Assert.That(SEntMan.System<CultivationSystem>().StartBreakthrough(cultivatorEntity), Is.True, "The breakthrough did not start.");
        });

        // Three cycles of three seconds at Qi Condensation.
        await RunSeconds(11);

        await Server.WaitAssertion(() =>
        {
            var cultivatorComponent = SComp<CultivatorComponent>(cultivatorUid);
            Assert.That(cultivatorComponent.Realm, Is.EqualTo(CultivationRealm.FoundationEstablishment),
                $"A stable breakthrough endured to the end did not reach the next realm (insight {cultivatorComponent.Insight}, injury {cultivatorComponent.InternalInjury}, cycles left {cultivatorComponent.BreakthroughCyclesLeft}).");
        });
    }

    [Test]
    public async Task TestHeartDemonBeatenOrReturned()
    {
        var map = await Pair.CreateTestMap();
        EntityUid beatenHostUid = default;
        EntityUid neglectedHostUid = default;

        await Server.WaitPost(() =>
        {
            var heartDemonSystem = SEntMan.System<HeartDemonSystem>();
            beatenHostUid = SEntMan.SpawnEntity("KsTestCultivator", map.GridCoords);
            neglectedHostUid = SEntMan.SpawnEntity("KsTestCultivator", map.GridCoords);

            var demonUid = heartDemonSystem.Summon(beatenHostUid);
            Assert.That(demonUid, Is.Not.Null, "No heart demon tore free.");
            Assert.That(heartDemonSystem.Summon(beatenHostUid), Is.EqualTo(demonUid), "A second demon tore free while the first was loose.");

            // Killed right beside its host, short of being gibbed.
            SEntMan.System<DamageableSystem>().TryChangeDamage(demonUid!.Value, Blunt(220), ignoreResistances: true);

            heartDemonSystem.Summon(neglectedHostUid, lifetime: System.TimeSpan.FromSeconds(1));
        });

        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SComp<CultivatorComponent>(beatenHostUid).DaoHeartTempered, Is.True, "Beating your own heart demon did not temper your Dao heart.");
                Assert.That(SComp<CultivatorComponent>(neglectedHostUid).InternalInjury, Is.EqualTo(2), "A demon left to run out the clock did not climb back in.");
            }
        });
    }

    [Test]
    public void TestPureRules()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(SharedCultivationSystem.RegenMultiplier(0.25f, 0), Is.EqualTo(1f));
            Assert.That(SharedCultivationSystem.RegenMultiplier(0.25f, 5), Is.Zero, "Qi recovery went negative.");
            Assert.That(SharedCultivationSystem.BreakthroughChance(70), Is.EqualTo(1f), "A stable breakthrough is not certain.");
            Assert.That(SharedCultivationSystem.BreakthroughChance(50), Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(SharedCultivationSystem.BreakthroughChance(-40), Is.EqualTo(0.05f).Within(0.0001f), "A hopeless breakthrough has no chance at all.");
            Assert.That(SharedCultivationSystem.LawSlots(CultivationRealm.QiCondensation), Is.EqualTo(1));
            Assert.That(SharedCultivationSystem.LawSlots(CultivationRealm.NascentSoul), Is.EqualTo(2));
        }
    }
}
