using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Cultivation.Systems;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared.Mind;
using Content.Shared.Movement.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._KS14.Cultivation;

/// <summary>
///     The universal techniques, the rest of each law's techniques, element combinations, and the soul leaving the body.
/// </summary>
[TestFixture]
[TestOf(typeof(CultivationUniversalSystem))]
[TestOf(typeof(CultivationLawArtSystem))]
[TestOf(typeof(NascentProjectionSystem))]
public sealed class CultivationTechniquesTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: KsTestTechniqueCultivator
  components:
  - type: Cultivator
";

    private const string Cultivator = "KsTestTechniqueCultivator";

    private static DamageSpecifier Blunt(float amount)
    {
        var damage = new DamageSpecifier();
        damage.DamageDict.Add("Blunt", FixedPoint2.New(amount));
        return damage;
    }

    [Test]
    public async Task TestUniversalTechniquesOpenByRealm()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var cultivationSystem = SEntMan.System<CultivationSystem>();
            var sharedCultivationSystem = SEntMan.System<SharedCultivationSystem>();
            var cultivatorUid = SEntMan.SpawnEntity(Cultivator, map.GridCoords);
            var cultivatorEntity = SEntity<CultivatorComponent>(cultivatorUid);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(sharedCultivationSystem.HasTechnique(cultivatorEntity.Comp, "ActionCultivationSpiritualSense"), Is.True);
                Assert.That(sharedCultivationSystem.HasTechnique(cultivatorEntity.Comp, "ActionCultivationRealmPressure"), Is.False,
                    "A Foundation technique opened at Qi Condensation.");
            }

            cultivationSystem.BreakThrough(cultivatorEntity);
            cultivationSystem.BreakThrough(cultivatorEntity);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(sharedCultivationSystem.HasTechnique(cultivatorEntity.Comp, "ActionCultivationRealmPressure"), Is.True);
                Assert.That(sharedCultivationSystem.HasTechnique(cultivatorEntity.Comp, "ActionCultivationVoidStep"), Is.True,
                    "Golden Core did not open Void Step.");
            }
        });
    }

    [Test]
    public async Task TestCombinationsComeAndGoWithTheirLaws()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var cultivationSystem = SEntMan.System<CultivationSystem>();
            var sharedCultivationSystem = SEntMan.System<SharedCultivationSystem>();
            var universalSystem = SEntMan.System<CultivationUniversalSystem>();
            var cultivatorUid = SEntMan.SpawnEntity(Cultivator, map.GridCoords);
            var cultivatorEntity = SEntity<CultivatorComponent>(cultivatorUid);
            var fire = SEntity<CultivationManualComponent>(SEntMan.SpawnEntity("CultivationManualFire", map.GridCoords));
            var water = SEntity<CultivationManualComponent>(SEntMan.SpawnEntity("CultivationManualWater", map.GridCoords));

            cultivationSystem.TryLearnFromManual(cultivatorUid, fire);
            cultivationSystem.BreakThrough(cultivatorEntity);
            Assert.That(sharedCultivationSystem.HasTechnique(cultivatorEntity.Comp, "ActionCultivationSteamVeil"), Is.False, "A combination opened with one law.");

            cultivationSystem.TryLearnFromManual(cultivatorUid, water);
            Assert.That(sharedCultivationSystem.HasTechnique(cultivatorEntity.Comp, "ActionCultivationSteamVeil"), Is.True,
                "Fire and water did not open Steam Veil.");

            Assert.That(universalSystem.ForgetComplementaryLaw(cultivatorEntity), Is.True);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(cultivatorEntity.Comp.Laws, Has.Count.EqualTo(1));
                Assert.That(sharedCultivationSystem.HasTechnique(cultivatorEntity.Comp, "ActionCultivationSteamVeil"), Is.False,
                    "The combination outlived the law it needed.");
                Assert.That(sharedCultivationSystem.HasTechnique(cultivatorEntity.Comp, "ActionCultivationStillWaterWard"), Is.False,
                    "The released law's techniques stayed.");
                Assert.That(sharedCultivationSystem.HasTechnique(cultivatorEntity.Comp, "ActionCultivationBurningPalm"), Is.True,
                    "Releasing the Complementary Law took the Root Law's techniques too.");
            }
        });
    }

    [Test]
    public async Task TestTimedEffects()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var stanceSystem = SEntMan.System<CultivationStanceSystem>();
            var damageableSystem = SEntMan.System<DamageableSystem>();
            var bellUid = SEntMan.SpawnEntity(Cultivator, map.GridCoords);
            var boundUid = SEntMan.SpawnEntity(Cultivator, map.GridCoords);

            stanceSystem.ApplyGoldenBellQi(bellUid, System.TimeSpan.FromSeconds(5));
            stanceSystem.ApplySnare(boundUid, System.TimeSpan.FromSeconds(5), 0f);
            damageableSystem.TryChangeDamage(bellUid, Blunt(30));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(damageableSystem.GetTotalDamage(bellUid), Is.EqualTo(FixedPoint2.Zero), "Golden Bell Qi let a blow through.");
                Assert.That(SComp<MovementSpeedModifierComponent>(boundUid).CurrentWalkSpeed, Is.Zero, "Binding vines did not hold their target.");
            }
        });
    }

    [Test]
    public async Task TestConcealmentBreaksOnTechniqueUse()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var cultivatorUid = SEntMan.SpawnEntity(Cultivator, map.GridCoords);
            var ev = new ConcealmentActionEvent { Performer = cultivatorUid };
            SEntMan.EventBus.RaiseLocalEvent(cultivatorUid, ev);
            Assert.That(SEntMan.HasComponent<CultivationConcealedComponent>(cultivatorUid), Is.True, "Concealment did not hide the cultivator.");

            SEntMan.System<CultivationSystem>().TryUseTechnique(cultivatorUid, 1f);
            Assert.That(SEntMan.HasComponent<CultivationConcealedComponent>(cultivatorUid), Is.False, "Using a technique did not reveal a concealed cultivator.");
        });
    }

    [Test]
    public async Task TestJadeSlipAwakensAMortal()
    {
        var map = await Pair.CreateTestMap();
        EntityUid mortalUid = default;

        await Server.WaitPost(() =>
        {
            mortalUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var slipUid = SEntMan.SpawnEntity("KsJadeTransmissionSlip", map.GridCoords);
            SComp<CultivationManualComponent>(slipUid).Law = "KsWood";
            SEntMan.System<SharedHandsSystem>().PickupOrDrop(mortalUid, slipUid, checkActionBlocker: false);
            SEntMan.EventBus.RaiseLocalEvent(slipUid, new UseInHandEvent(mortalUid));
        });

        await RunSeconds(11);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.TryGetComponent<CultivatorComponent>(mortalUid, out var cultivatorComponent), Is.True,
                "Studying a master's transmission did not awaken the mortal.");
            Assert.That(cultivatorComponent!.Laws, Does.Contain(new Robust.Shared.Prototypes.ProtoId<Content.Shared._KS14.Cultivation.Prototypes.CultivationLawPrototype>("KsWood")),
                "The awakened mortal did not learn the transmitted law.");
        });
    }

    [Test]
    public async Task TestSoulLeavesAndReturns()
    {
        var map = await Pair.CreateTestMap();
        EntityUid bodyUid = default;
        EntityUid mindId = default;

        await Server.WaitAssertion(() =>
        {
            var mindSystem = SEntMan.System<SharedMindSystem>();
            bodyUid = SEntMan.SpawnEntity(Cultivator, map.GridCoords);
            SEntMan.System<SharedCultivationSystem>().AddQi(bodyUid, 1000f);
            mindId = mindSystem.CreateMind(null).Owner;
            mindSystem.TransferTo(mindId, bodyUid);

            var ev = new BodyOutsideBodyActionEvent { Performer = bodyUid };
            SEntMan.EventBus.RaiseLocalEvent(bodyUid, ev);
            Assert.That(ev.Handled, Is.True, "The soul did not leave.");

            var soulUid = SComp<MindComponent>(mindId).OwnedEntity;
            Assert.That(soulUid, Is.Not.Null.And.Not.EqualTo(bodyUid), "The mind did not go with the soul.");
            Assert.That(SEntMan.HasComponent<NascentProjectionComponent>(soulUid!.Value), Is.True);

            SEntMan.System<NascentProjectionSystem>().Return(SEntity<NascentProjectionComponent>(soulUid.Value));
            Assert.That(SComp<MindComponent>(mindId).OwnedEntity, Is.EqualTo(bodyUid), "The mind did not come home.");
        });
    }

    [Test]
    public async Task TestSpiritBeastTakesItsMastersSide()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var masterUid = SEntMan.SpawnEntity(Cultivator, map.GridCoords);
            var beastUid = SEntMan.SpawnEntity("MobMouse", map.GridCoords);
            SEntMan.System<SharedCultivationSystem>().AddQi(masterUid, 1000f);

            var ev = new BeastContractActionEvent { Performer = masterUid, Target = beastUid };
            SEntMan.EventBus.RaiseLocalEvent(masterUid, ev);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(ev.Handled, Is.True, "The contract was refused.");
                Assert.That(SComp<SpiritBeastComponent>(beastUid).Master, Is.EqualTo(masterUid));
                Assert.That(SComp<Content.Server.NPC.HTN.HTNComponent>(beastUid).RootTask.Task.Id, Is.EqualTo("KsSpiritBeastCompound"),
                    "The beast did not take up its new behaviour.");
                Assert.That(SComp<Content.Shared.NPC.Components.NpcFactionMemberComponent>(masterUid).Factions, Is.Not.Empty,
                    "The master has no side to take, so the next check proves nothing.");
                Assert.That(SComp<Content.Shared.NPC.Components.NpcFactionMemberComponent>(beastUid).Factions,
                    Is.EquivalentTo(SComp<Content.Shared.NPC.Components.NpcFactionMemberComponent>(masterUid).Factions),
                    "The beast did not take its master's side.");
            }
        });
    }

    [Test]
    public async Task TestAscensionLeavesTheWorld()
    {
        var map = await Pair.CreateTestMap();
        EntityUid cultivatorUid = default;

        await Server.WaitPost(() =>
        {
            cultivatorUid = SEntMan.SpawnEntity(Cultivator, map.GridCoords);
            SEntMan.System<CultivationSystem>().Ascend(SEntity<CultivatorComponent>(cultivatorUid));
        });

        await RunTicksSync(2);

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.EntityExists(cultivatorUid), Is.False, "The ascended cultivator is still in this world."));
    }
}
