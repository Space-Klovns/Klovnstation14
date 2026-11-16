using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Cultivation.Components;
using Content.Server._KS14.Cultivation.Systems;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Projectiles;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._KS14.Cultivation;

/// <summary>
///     Martial arts from manuals, and the legendary armor.
/// </summary>
[TestFixture]
[TestOf(typeof(MartialArtsSystem))]
[TestOf(typeof(SharedMartialArtsSystem))]
public sealed class MartialArtsTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: KsTestMartialCultivator
  components:
  - type: Cultivator
";

    private const string Cultivator = "KsTestMartialCultivator";
    private const string Mortal = "MobHuman";

    private static DamageSpecifier Damage(string type, float amount)
    {
        var damage = new DamageSpecifier();
        damage.DamageDict.Add(type, FixedPoint2.New(amount));
        return damage;
    }

    [Test]
    public async Task TestLearningRules()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var artsSystem = SEntMan.System<SharedMartialArtsSystem>();
            var cultivationSystem = SEntMan.System<CultivationSystem>();
            var mortalUid = SEntMan.SpawnEntity(Mortal, map.GridCoords);
            var cultivatorUid = SEntMan.SpawnEntity(Cultivator, map.GridCoords);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(artsSystem.TryLearn(mortalUid, "KsGoldenBell"), Is.EqualTo(MartialArtLearnResult.Learned), "A mortal could not learn a body art.");
                Assert.That(artsSystem.HasArtAction(mortalUid, "ActionMartialGoldenBell"), Is.True, "Learning a body art granted no action.");
                Assert.That(artsSystem.TryLearn(mortalUid, "KsGoldenBell"), Is.EqualTo(MartialArtLearnResult.AlreadyKnown));
                Assert.That(artsSystem.TryLearn(mortalUid, "KsMuscleTendon"), Is.EqualTo(MartialArtLearnResult.Learned));
                Assert.That(SEntMan.HasComponent<MuscleTendonComponent>(mortalUid), Is.True, "A passive art did not change the body.");
                Assert.That(artsSystem.TryLearn(mortalUid, "KsDugu"), Is.EqualTo(MartialArtLearnResult.NoRoom), "A third body art fit.");
                Assert.That(artsSystem.TryLearn(mortalUid, "KsNineYang"), Is.EqualTo(MartialArtLearnResult.NotCultivator), "A mortal learned a qi art.");

                Assert.That(artsSystem.TryLearn(cultivatorUid, "KsDragonPalms"), Is.EqualTo(MartialArtLearnResult.RealmTooLow),
                    "A Foundation art was learned at Qi Condensation.");
                Assert.That(artsSystem.TryLearn(cultivatorUid, "KsStarAbsorbing"), Is.EqualTo(MartialArtLearnResult.Learned));
                Assert.That(SEntMan.HasComponent<DarkArtsComponent>(cultivatorUid), Is.True, "A dark art left no mark.");
                Assert.That(artsSystem.TryLearn(cultivatorUid, "KsNineYang"), Is.EqualTo(MartialArtLearnResult.NoRoom),
                    "A second qi art fit at Qi Condensation.");
            }

            cultivationSystem.BreakThrough(SEntity<CultivatorComponent>(cultivatorUid));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(artsSystem.TryLearn(cultivatorUid, "KsNineYang"), Is.EqualTo(MartialArtLearnResult.Learned),
                    "Foundation Establishment did not open another qi art slot.");
                Assert.That(SEntMan.HasComponent<NineYangComponent>(cultivatorUid), Is.True);
            }
        });
    }

    [Test]
    public async Task TestGoldenBellTurnsAsidePhysicalBlowsOnly()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var damageableSystem = SEntMan.System<DamageableSystem>();
            var controlUid = SEntMan.SpawnEntity(Mortal, map.GridCoords);
            var bellUid = SEntMan.SpawnEntity(Mortal, map.GridCoords);
            SEntMan.System<CultivationStanceSystem>().ApplyGoldenBell(bellUid, System.TimeSpan.FromSeconds(5), 0.6f);

            foreach (var uid in new[] { controlUid, bellUid })
            {
                damageableSystem.TryChangeDamage(uid, Damage("Blunt", 10));
                damageableSystem.TryChangeDamage(uid, Damage("Heat", 10));
            }

            var control = damageableSystem.GetAllDamage(controlUid).DamageDict;
            var bell = damageableSystem.GetAllDamage(bellUid).DamageDict;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(bell["Blunt"].Float(), Is.EqualTo(control["Blunt"].Float() * 0.6f).Within(0.01f), "Golden Bell did not soften a physical blow.");
                Assert.That(bell["Heat"], Is.EqualTo(control["Heat"]), "Golden Bell softened fire as well.");
            }
        });
    }

    [Test]
    public async Task TestDuguCounterBreaksTheBlow()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var damageableSystem = SEntMan.System<DamageableSystem>();
            var defenderUid = SEntMan.SpawnEntity(Mortal, map.GridCoords);
            var attackerUid = SEntMan.SpawnEntity(Mortal, map.GridCoords);
            SEntMan.System<MartialArtsSystem>().StartDuguCounter(defenderUid, System.TimeSpan.FromMinutes(1));

            damageableSystem.TryChangeDamage(defenderUid, Damage("Blunt", 20), origin: attackerUid);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(damageableSystem.GetTotalDamage(defenderUid), Is.EqualTo(FixedPoint2.Zero), "The countered blow still landed.");
                Assert.That(damageableSystem.GetTotalDamage(attackerUid), Is.GreaterThan(FixedPoint2.Zero), "The attacker was not cut by the counter.");
            }

            // The counter is spent: the next blow lands.
            damageableSystem.TryChangeDamage(defenderUid, Damage("Blunt", 20), origin: attackerUid);
            Assert.That(damageableSystem.GetTotalDamage(defenderUid), Is.GreaterThan(FixedPoint2.Zero), "One counter broke two blows.");
        });
    }

    [Test]
    public async Task TestHedgehogArmorPricksAttackers()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var damageableSystem = SEntMan.System<DamageableSystem>();
            var inventorySystem = SEntMan.System<InventorySystem>();
            var wearerUid = SEntMan.SpawnEntity(Mortal, map.GridCoords);
            var attackerUid = SEntMan.SpawnEntity(Mortal, map.GridCoords);

            // Both wear it: the thorns must not bounce back and forth between them.
            foreach (var uid in new[] { wearerUid, attackerUid })
            {
                var armorUid = SEntMan.SpawnEntity("ClothingOuterSoftHedgehogArmor", map.GridCoords);
                Assert.That(inventorySystem.TryEquip(uid, armorUid, "outerClothing", silent: true, force: true), Is.True);
            }

            damageableSystem.TryChangeDamage(wearerUid, Damage("Blunt", 10), origin: attackerUid);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(damageableSystem.GetTotalDamage(attackerUid), Is.GreaterThan(FixedPoint2.Zero), "The attacker was not pricked.");
                Assert.That(damageableSystem.GetTotalDamage(wearerUid), Is.LessThan(FixedPoint2.New(10)),
                    "The armor neither softened the blow nor stopped the thorns bouncing back.");
            }
        });
    }

    [Test]
    public async Task TestGreatSageRegaliaWakesOnlyWhenComplete()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var inventorySystem = SEntMan.System<InventorySystem>();
            var wearerUid = SEntMan.SpawnEntity(Mortal, map.GridCoords);
            var pieces = new[]
            {
                ("ClothingOuterGoldenChainMail", "outerClothing"),
                ("ClothingHeadPhoenixWingCrown", "head"),
                ("ClothingShoesCloudStepping", "shoes"),
            };

            foreach (var (proto, slot) in pieces)
            {
                Assert.That(SEntMan.HasComponent<GreatSageComponent>(wearerUid), Is.False, "The set woke before it was complete.");
                var pieceUid = SEntMan.SpawnEntity(proto, map.GridCoords);
                Assert.That(inventorySystem.TryEquip(wearerUid, pieceUid, slot, silent: true, force: true), Is.True, $"Could not wear {proto}.");
            }

            Assert.That(SEntMan.HasComponent<GreatSageComponent>(wearerUid), Is.True, "The full regalia did not wake.");

            inventorySystem.TryUnequip(wearerUid, "head", silent: true, force: true);
            Assert.That(SEntMan.HasComponent<GreatSageComponent>(wearerUid), Is.False, "The set stayed awake with a piece missing.");
        });
    }

    [Test]
    public async Task TestDragonPalmsBuildUp()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var userUid = SEntMan.SpawnEntity(Cultivator, map.GridCoords);
            var targetUid = SEntMan.SpawnEntity(Mortal, map.GridCoords);
            SEntMan.EnsureComponent<MartialArtistComponent>(userUid);
            SEntMan.System<SharedCultivationSystem>().SetRealm(userUid, CultivationRealm.GoldenCore);

            for (var i = 1; i <= 2; i++)
            {
                var ev = new DragonPalmActionEvent { Performer = userUid, Target = targetUid };
                SEntMan.EventBus.RaiseLocalEvent(userUid, ev);
                Assert.That(ev.Handled, Is.True, "The palm did not go off.");
                Assert.That(SComp<DragonPalmComboComponent>(targetUid).Stacks, Is.EqualTo(i), "The palms did not build on each other.");
            }
        });
    }

    [Test]
    public async Task TestGreatShiftTurnsProjectilesBack()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var userUid = SEntMan.SpawnEntity(Cultivator, map.GridCoords);
            var shooterUid = SEntMan.SpawnEntity(Mortal, map.GridCoords);
            SEntMan.System<MartialArtsSystem>().StartGreatShift(userUid, System.TimeSpan.FromMinutes(1), charges: 1);

            var projectileUid = SEntMan.SpawnEntity("ProjectileCultivationSwordQi", map.GridCoords);
            var projectileComponent = SComp<ProjectileComponent>(projectileUid);
            SEntMan.System<SharedProjectileSystem>().SetShooter(projectileUid, projectileComponent, shooterUid);
            var physicsComponent = SComp<PhysicsComponent>(projectileUid);
            SEntMan.System<SharedPhysicsSystem>().SetLinearVelocity(projectileUid, new Vector2(5f, 0f), body: physicsComponent);

            var ev = new ProjectileReflectAttemptEvent(projectileUid, projectileComponent, false);
            SEntMan.EventBus.RaiseLocalEvent(userUid, ref ev);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(ev.Cancelled, Is.True, "The projectile was not turned.");
                Assert.That(physicsComponent.LinearVelocity.X, Is.LessThan(0f), "The projectile still flies the same way.");
                Assert.That(projectileComponent.Shooter, Is.EqualTo(userUid), "The turned projectile still belongs to its shooter.");
                Assert.That(SEntMan.GetComponent<GreatShiftComponent>(userUid).LifeStage, Is.GreaterThan(ComponentLifeStage.Running),
                    "Great Shift outlasted its last charge.");
            }
        });
    }

    [Test]
    public async Task TestMuscleTendonEasesStaminaDamage()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var staminaSystem = SEntMan.System<SharedStaminaSystem>();
            var controlUid = SEntMan.SpawnEntity(Mortal, map.GridCoords);
            var trainedUid = SEntMan.SpawnEntity(Mortal, map.GridCoords);
            SEntMan.System<SharedMartialArtsSystem>().TryLearn(trainedUid, "KsMuscleTendon");

            staminaSystem.TakeStaminaDamage(controlUid, 20f);
            staminaSystem.TakeStaminaDamage(trainedUid, 20f);

            Assert.That(SComp<StaminaComponent>(trainedUid).StaminaDamage,
                Is.EqualTo(SComp<StaminaComponent>(controlUid).StaminaDamage * 0.7f).Within(0.01f),
                "The Muscle-Tendon Changing Classic did not ease stamina damage.");
        });
    }
}
