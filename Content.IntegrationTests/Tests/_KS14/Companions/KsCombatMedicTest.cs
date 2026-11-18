#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Companions;
using Content.Shared._KS14.Companions;
using Content.Shared._KS14.Perks;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.NPC.Systems;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Companions;

/// <summary>
///     The combat medic (<see cref="KsCombatMedicSystem"/>) on its own test bot, and the Healing perk's live medibot
///         companion: enemies can target it, and it leaves nothing behind when destroyed.
/// </summary>
[TestFixture]
[TestOf(typeof(KsCombatMedicSystem))]
public sealed class KsCombatMedicTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _medic = "KsTestCombatMedic";
    private readonly EntProtoId _friend = "KsTestCombatMedicFriend";
    private readonly EntProtoId _enemy = "KsTestCombatMedicEnemy";
    private readonly ProtoId<KsPerkPrototype> _medibotPerk = "KsTestCombatMedicMedibotPerk";

    // The live companion, as the Healing perk lists it.
    private static readonly EntProtoId Medibot = "KsMobCompanionMedibot";

    private static readonly ProtoId<ReagentPrototype> Water = "Water";
    private static readonly ProtoId<DamageTypePrototype> Blunt = "Blunt";

    [TestPrototypes]
    private const string Prototypes = @"
- type: npcFaction
  id: KsTestCombatMedicSideA
  hostile:
  - KsTestCombatMedicSideB

- type: npcFaction
  id: KsTestCombatMedicSideB
  hostile:
  - KsTestCombatMedicSideA

- type: entity
  id: KsTestCombatMedicMobBase
  abstract: true
  components:
  - type: Physics
    bodyType: KinematicController
  - type: Fixtures
    fixtures:
      fix1:
        shape: !type:PhysShapeCircle
          radius: 0.35
        density: 185
        mask:
        - MobMask
        layer:
        - MobLayer
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      500: Critical
      1000: Dead
  - type: Damageable
  - type: Injurable
    damageContainer: Biological

- type: entity
  parent: KsTestCombatMedicMobBase
  id: KsTestCombatMedicFriend
  components:
  - type: SolutionContainerManager
  - type: Bloodstream
    bloodlossDamage:
      types:
        Bloodloss: 1
    bloodlossHealDamage:
      types:
        Bloodloss: -1
  - type: NpcFactionMember
    factions:
    - KsTestCombatMedicSideA

- type: entity
  parent: KsTestCombatMedicFriend
  id: KsTestCombatMedicEnemy
  components:
  - type: NpcFactionMember
    factions:
    - KsTestCombatMedicSideB

- type: entity
  parent: KsTestCombatMedicMobBase
  id: KsTestCombatMedic
  components:
  - type: NpcFactionMember
    factions:
    - KsTestCombatMedicSideA
  - type: KsCombatMedic
    minDamage: 5
    seekRange: 5
    injectRange: 1.5
    windup: 0.5
    cooldown: 5
    injection:
      reagents:
      - ReagentId: Water
        Quantity: 5
    criticalInjection:
      reagents:
      - ReagentId: Water
        Quantity: 5

- type: ksPerk
  id: KsTestCombatMedicMedibotPerk
  name: ks-perk-healing-name
  description: ks-perk-healing-desc
  icon: { sprite: _KS14/Interface/Perks/perks.rsi, state: healing }
  components:
  - type: KsCompanions
    companions:
    - prototype: KsMobCompanionMedibot
";

    private int Ticks(float seconds) => (int)MathF.Ceiling(seconds * (float)Pair.Server.ResolveDependency<IGameTiming>().TickRate);

    private void Hurt(EntityUid uid, float amount)
    {
        var damage = new DamageSpecifier(Pair.Server.ProtoMan.Index(Blunt), FixedPoint2.New(amount));
        Pair.Server.System<DamageableSystem>().TryChangeDamage(uid, damage, ignoreResistances: true);
    }

    private float BloodWater(EntityUid uid)
    {
        var entMan = Pair.Server.EntMan;
        var bloodstream = entMan.GetComponent<BloodstreamComponent>(uid);
        Entity<Content.Shared.Chemistry.Components.SolutionComponent>? solutionEntity = null;
        Assert.That(entMan.System<SharedSolutionContainerSystem>().ResolveSolution(uid, bloodstream.BloodSolutionName, ref solutionEntity, out var solution));
        return (float)solution!.GetTotalPrototypeQuantity(Water);
    }

    [Test]
    public async Task MedicReadiesThenInjectsAHurtFriendNotAnEnemy()
    {
        var server = Pair.Server;
        var entMan = server.EntMan;
        var testMap = await Pair.CreateTestMap();
        EntityUid medicUid = default;
        EntityUid friendUid = default;
        EntityUid enemyUid = default;

        await server.WaitPost(() =>
        {
            medicUid = entMan.SpawnAtPosition(_medic, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            // The enemy is closer and more hurt: still not a patient.
            enemyUid = entMan.SpawnAtPosition(_enemy, new EntityCoordinates(testMap.Grid, 0.5f, 1.3f));
            friendUid = entMan.SpawnAtPosition(_friend, new EntityCoordinates(testMap.Grid, 1.5f, 0.5f));
            Hurt(enemyUid, 60);
            Hurt(friendUid, 30);
        });

        var medic = entMan.GetComponent<KsCombatMedicComponent>(medicUid);
        var dose = (float)medic.Injection.Volume;

        // It scans at once: long enough to ready, not to inject.
        Assert.That(Ticks((float)medic.Windup.TotalSeconds), Is.GreaterThan(3), "the test medic's windup should span a few ticks");
        await Pair.RunTicksSync(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(medic.Patient, Is.EqualTo(friendUid), "the medic should pick the hurt friend");
            Assert.That(medic.InjectAt, Is.Not.Null, "the medic should ready its injector first");
            Assert.That(BloodWater(friendUid), Is.Zero, "nothing should go in before the windup is over");
        });

        await Pair.RunTicksSync(Ticks((float)medic.Windup.TotalSeconds) + 2);
        await server.WaitAssertion(() =>
        {
            Assert.That(BloodWater(friendUid), Is.EqualTo(dose).Within(0.5f), "the friend should be injected after the windup");
            Assert.That(BloodWater(enemyUid), Is.Zero, "the enemy should never be injected");
        });
    }

    [Test]
    public async Task MedibotCompanionCanBeTargetedAndLeavesNothingWhenDestroyed()
    {
        var server = Pair.Server;
        var entMan = server.EntMan;
        var testMap = await Pair.CreateTestMap();
        EntityUid ownerUid = default;
        EntityUid enemyUid = default;
        EntityUid medibotUid = default;

        await server.WaitPost(() =>
        {
            ownerUid = entMan.SpawnAtPosition(_friend, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            enemyUid = entMan.SpawnAtPosition(_enemy, new EntityCoordinates(testMap.Grid, 4.5f, 0.5f));
            Assert.That(server.System<SharedKsPerkSystem>().TryAddPerk(ownerUid, _medibotPerk));
        });
        await Pair.RunTicksSync(2);

        HashSet<EntityUid> before = default!;
        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<KsCompanionSystem>().TryGetCompanion(ownerUid, 0, out var companionUid), "the medibot should be out");
            medibotUid = companionUid!.Value;
            Assert.That(entMan.GetComponent<MetaDataComponent>(medibotUid).EntityPrototype?.ID, Is.EqualTo(Medibot.Id));

            var hostiles = server.System<NpcFactionSystem>().GetNearbyHostiles(enemyUid, 10f).ToList();
            Assert.That(hostiles, Does.Contain(medibotUid), "enemies should be able to target the medibot");

            before = MapEntities(testMap.MapId);
            // Far past anything a medibot could take.
            Hurt(medibotUid, 10000);
        });

        await Pair.RunTicksSync(Ticks(3f));
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(medibotUid), "the destroyed medibot should be gone");
            var left = MapEntities(testMap.MapId);
            left.ExceptWith(before);
            Assert.That(left, Is.Empty, "a destroyed medibot should leave nothing behind");
        });
    }

    private HashSet<EntityUid> MapEntities(MapId mapId)
    {
        var entMan = Pair.Server.EntMan;
        var result = new HashSet<EntityUid>();
        var query = entMan.AllEntityQueryEnumerator<TransformComponent>();
        while (query.MoveNext(out var uid, out var transform))
        {
            // Sounds are fine: they go when they finish.
            if (transform.MapID == mapId && !entMan.HasComponent<AudioComponent>(uid))
                result.Add(uid);
        }

        return result;
    }
}
