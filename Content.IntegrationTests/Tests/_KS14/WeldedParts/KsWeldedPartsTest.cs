#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Tests._KS14.FieldCommander;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.FieldCommander;
using Content.Server._KS14.WeldedParts;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking.Components;
using Content.Shared.NPC.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Dynamics.Joints;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.WeldedParts;

/// <summary>
///     <see cref="KsWeldedPartsComponent"/>: its parts are spawned welded on with the owner's factions, go together whichever
///         goes first, and a field commander unit built that way is counted, and pays, once.
/// </summary>
[TestFixture]
[TestOf(typeof(KsWeldedPartsSystem))]
public sealed class KsWeldedPartsTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId NanotrasenObjective = "KsScenarioObjectiveNanotrasen";

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _root = "KsTestWeldedRoot";
    private readonly EntProtoId _part = "KsTestWeldedPart";
    private readonly EntProtoId _rule = "KsTestWeldedRule";
    private readonly EntProtoId _defender = "KsTestWeldedDefender";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestWeldedPart
  components:
  - type: Physics
    bodyType: Dynamic
  - type: Fixtures
    fixtures:
      fix1:
        shape: !type:PhysShapeCircle
          radius: 0.3
        density: 10
  - type: Damageable
    damageContainer: Biological
  - type: Injurable
  - type: Destructible
    thresholds:
    - trigger:
        !type:DamageTrigger
        damage: 10
      behaviors:
      - !type:DoActsBehavior
        acts: [ 'Destruction' ]

- type: entity
  id: KsTestWeldedRoot
  components:
  - type: Physics
    bodyType: Dynamic
  - type: Fixtures
    fixtures:
      fix1:
        shape: !type:PhysShapeCircle
          radius: 0.4
        density: 10
  - type: NpcFactionMember
    factions:
    - Syndicate
  - type: Damageable
    damageContainer: Biological
  - type: Injurable
  - type: Destructible
    thresholds:
    - trigger:
        !type:DamageTrigger
        damage: 100
      behaviors:
      - !type:DoActsBehavior
        acts: [ 'Destruction' ]
  - type: KsWeldedParts
    parts:
    - prototype: KsTestWeldedPart
      offset: 0, 1

- type: entity
  id: KsTestWeldedDefender
  components:
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      100: Dead
  - type: Damageable
    damageContainer: Biological
  - type: Injurable
  - type: ScenarioFactionMember
    id: Nanotrasen

- type: entity
  parent: BaseGameRule
  id: KsTestWeldedRule
  components:
  - type: KsFieldCommanderRule
    minSpawnDistance: 6
    maxSpawnDistance: 12
    minDefenderDistance: 2
    minPodDefenderDistance: 2
    minPodObjectiveDistance: 6
    defenderSightRange: 1
    safeZoneRadius: 1
    uplinkPrototype: null
    briefingAnnouncement: null
    pointsPerCost: 25
    phases:
    - name: ks-fieldcommander-phase-probing
      start: 1000
      waveInterval: 1000
      firstWaveDelay: 1000
      budget: 2
    assets:
    - entity: KsTestWeldedRoot
      cost: 2
      giveOrders: false
";

    private async Task<(EntityUid Root, EntityUid Part)> SpawnWelded()
    {
        var testMap = await Pair.CreateTestMap();
        EntityUid rootUid = default;
        await Server.WaitPost(() => rootUid = Server.EntMan.SpawnAtPosition(_root, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f)));
        await Pair.RunTicksSync(3);

        EntityUid partUid = default;
        await Server.WaitAssertion(() =>
        {
            var parts = Server.System<KsWeldedPartsSystem>().GetParts(rootUid);
            Assert.That(parts, Has.Count.EqualTo(1), "the part should have been spawned");
            partUid = parts[0];
        });

        return (rootUid, partUid);
    }

    [Test]
    public async Task PartsAreSpawnedWeldedOnWithTheOwnersFactions()
    {
        var (rootUid, partUid) = await SpawnWelded();

        await Server.WaitAssertion(() =>
        {
            var entMan = Server.EntMan;
            Assert.That(entMan.GetComponent<MetaDataComponent>(partUid).EntityPrototype?.ID, Is.EqualTo(_part.Id));

            var joints = entMan.TryGetComponent<JointComponent>(rootUid, out var jointComponent)
                ? jointComponent.GetJoints.Values.ToList()
                : new();
            Assert.That(joints.Any(joint => joint is WeldJoint && (joint.BodyAUid == partUid || joint.BodyBUid == partUid)), Is.True,
                "the part should be welded to its owner");

            Assert.That(entMan.GetComponent<NpcFactionMemberComponent>(partUid).Factions,
                Is.EquivalentTo(entMan.GetComponent<NpcFactionMemberComponent>(rootUid).Factions),
                "the part should take its owner's factions");

            var transformSystem = entMan.System<SharedTransformSystem>();
            var offset = transformSystem.GetWorldPosition(partUid) - transformSystem.GetWorldPosition(rootUid);
            Assert.That(offset.Y, Is.EqualTo(1f).Within(0.05f), "the part should sit at its offset");
        });
    }

    /// <summary>
    ///     On a turned grid the part still sits at its offset, turned with the owner: the offset is put on in the frame of
    ///         the coordinates it is added to, the grid's, not the world's.
    /// </summary>
    [Test]
    public async Task PartsSitAtTheirOffsetOnATurnedGrid()
    {
        var testMap = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var entMan = Server.EntMan;
            var transformSystem = entMan.System<SharedTransformSystem>();
            transformSystem.SetLocalRotation(testMap.Grid.Owner, Angle.FromDegrees(90));

            var rootUid = entMan.SpawnAtPosition(_root, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            var parts = Server.System<KsWeldedPartsSystem>().GetParts(rootUid);
            Assert.That(parts, Has.Count.EqualTo(1), "the part should have been spawned");

            var offset = entMan.GetComponent<KsWeldedPartsComponent>(rootUid).Parts[0].Offset;
            var expected = transformSystem.GetWorldRotation(rootUid).RotateVec(offset);
            var actual = transformSystem.GetWorldPosition(parts[0]) - transformSystem.GetWorldPosition(rootUid);
            Assert.That((actual - expected).Length(), Is.LessThan(0.01f),
                $"the part should sit at its offset turned with its owner, {expected}, not at {actual}");
        });
    }

    /// <summary>
    ///     A map save leaves a built welded whole out, its parts and its owner alike, so that no part is left behind on its
    ///         own and no owner welds a second set on when loaded; one not yet built, on a map not yet started, is saved.
    /// </summary>
    [Test]
    public async Task BuiltWholesAreNotSaved()
    {
        var (rootUid, partUid) = await SpawnWelded();
        var unbuiltMap = await Pair.CreateTestMap(initialized: false);

        await Server.WaitAssertion(() =>
        {
            var entMan = Server.EntMan;
            var mapLoaderSystem = entMan.System<Robust.Shared.EntitySerialization.Systems.MapLoaderSystem>();
            var gridUid = entMan.GetComponent<TransformComponent>(rootUid).GridUid!.Value;
            var writer = new System.IO.StringWriter();
            Assert.That(mapLoaderSystem.TrySaveGrid(gridUid, writer), Is.True);

            var saved = writer.ToString();
            Assert.That(saved, Does.Not.Contain($"proto: {_part.Id}"), "a welded part should be left out of a save");
            Assert.That(saved, Does.Not.Contain($"proto: {_root.Id}"), "and so should its owner, which would weld on another");
            Assert.That(entMan.Deleted(partUid), Is.False);

            var unbuiltUid = entMan.SpawnAtPosition(_root, new EntityCoordinates(unbuiltMap.Grid, 0.5f, 0.5f));
            Assert.That(Server.System<KsWeldedPartsSystem>().GetParts(unbuiltUid), Is.Empty, "precondition: not built before its map starts");
            writer = new System.IO.StringWriter();
            Assert.That(mapLoaderSystem.TrySaveGrid(unbuiltMap.Grid.Owner, writer), Is.True);
            Assert.That(writer.ToString(), Does.Contain($"proto: {_root.Id}"), "an owner not yet built should be saved, to build itself once loaded");
        });
    }

    [Test]
    public async Task DestroyingAPartDestroysTheWhole()
    {
        var (rootUid, partUid) = await SpawnWelded();

        await Server.WaitPost(() =>
        {
            var damage = new DamageSpecifier { DamageDict = { ["Blunt"] = FixedPoint2.New(50) } };
            Server.System<DamageableSystem>().ChangeDamage(partUid, damage, ignoreResistances: true);
        });

        await Pair.RunTicksSync(3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.EntMan.Deleted(partUid), Is.True, "the part should be destroyed");
            Assert.That(Server.EntMan.Deleted(rootUid), Is.True, "its owner should go with it");
        });
    }

    [Test]
    public async Task DeletingTheOwnerDeletesItsParts()
    {
        var (rootUid, partUid) = await SpawnWelded();

        await Server.WaitPost(() => Server.EntMan.DeleteEntity(rootUid));
        await Pair.RunTicksSync(3);

        await Server.WaitAssertion(() => Assert.That(Server.EntMan.Deleted(partUid), Is.True, "the part should go with its owner"));
    }

    /// <summary>
    ///     A field commander unit with a part welded on: one alive, and destroyed through its part, one kill paid once to
    ///         whoever shot the part.
    /// </summary>
    [Test]
    public async Task AWeldedUnitCountsAndPaysOnce()
    {
        var server = Server;
        var testMap = await Pair.CreateTestMap();
        EntityUid ruleUid = default, playerBody = default;

        await server.WaitPost(() =>
        {
            KsFieldCommanderTestHelpers.FillFloor(server, testMap, halfExtent: 16);
            server.EntMan.SpawnAtPosition(NanotrasenObjective, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            playerBody = server.EntMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, 2.5f, 0.5f));
            server.PlayerMan.SetAttachedEntity(Pair.Player!, playerBody);
            ruleUid = KsFieldCommanderTestHelpers.StartRules(server, _rule).CommanderUid;
        });

        await Pair.RunTicksSync(3);

        await server.WaitPost(() =>
        {
            var ruleComponent = server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            var ruleSystem = server.System<KsFieldCommanderRuleSystem>();
            ruleSystem.SetPhase((ruleUid, ruleComponent, server.EntMan.GetComponent<GameRuleComponent>(ruleUid)), 0);
            ruleSystem.ForceWave((ruleUid, ruleComponent));
        });

        await Pair.RunTicksSync(3);

        EntityUid unitUid = default, partUid = default;
        var expected = 0;
        await server.WaitAssertion(() =>
        {
            var ruleComponent = server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            var units = new List<EntityUid>();
            var unitQuery = server.EntMan.AllEntityQueryEnumerator<KsFieldCommanderUnitComponent>();
            while (unitQuery.MoveNext(out var uid, out _))
                units.Add(uid);

            Assert.That(units, Has.Count.EqualTo(1), "the hull alone should be a unit, not its part");
            unitUid = units[0];
            Assert.That(server.System<KsFieldCommanderRuleSystem>().CountLivingUnitsByAsset(ruleUid).GetValueOrDefault(0), Is.EqualTo(1),
                "a welded unit should count once against its asset");

            var parts = server.System<KsWeldedPartsSystem>().GetParts(unitUid);
            Assert.That(parts, Has.Count.EqualTo(1));
            partUid = parts[0];

            expected = KsFieldCommanderRuleSystem.GetKillPoints(ruleComponent, ruleComponent.Assets[0]);
            Assert.That(expected, Is.GreaterThan(0));

            // Destroyed through its part, in one hit.
            var damage = new DamageSpecifier { DamageDict = { ["Blunt"] = FixedPoint2.New(50) } };
            server.System<DamageableSystem>().ChangeDamage(partUid, damage, ignoreResistances: true, origin: playerBody);
        });

        await Pair.RunTicksSync(3);

        await server.WaitAssertion(() =>
        {
            var ruleComponent = server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            Assert.That(server.EntMan.Deleted(unitUid), Is.True, "the hull should go with its part");
            Assert.That(ruleComponent.UnitsKilled, Is.EqualTo(1), "the kill should be counted once");
            Assert.That(server.System<KsFieldCommanderRuleSystem>().GetPoints(ruleComponent, Pair.Player!.UserId), Is.EqualTo(expected),
                "destroying the part should pay the whole unit's kill, once, to whoever shot it");
        });
    }
}
