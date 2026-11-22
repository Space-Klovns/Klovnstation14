#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.FieldCommander;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.FieldCommander;

/// <summary>
///     A unit bought as an asset with a stand-off is ordered to a spot that far from the defender it is sent at: in from
///         farther away, and back out when the defender is nearer.
/// </summary>
[TestFixture]
[TestOf(typeof(KsFieldCommanderRuleSystem))]
public sealed class KsFieldCommanderStandOffTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _rule = "KsTestFcStandOffRule";
    private readonly EntProtoId _defender = "KsTestFcStandOffDefender";
    private readonly EntProtoId _objective = "KsTestFcStandOffObjective";

    private const int HalfExtent = 30;

    /// <summary>
    ///     A unit that stands where it is put, so that only its orders say where it would go.
    /// </summary>
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestFcStandOffDefender
  components:
  - type: MobState
  - type: ScenarioFactionMember
    id: Nanotrasen

- type: entity
  id: KsTestFcStandOffObjective
  components:
  - type: ScenarioObjective
    factionId: Nanotrasen

- type: entity
  id: KsTestFcStandOffUnit
  components:
  - type: MobState
  - type: NpcSensors

- type: entity
  parent: BaseGameRule
  id: KsTestFcStandOffRule
  components:
  - type: KsFieldCommanderRule
    minSpawnDistance: 12
    maxSpawnDistance: 22
    minDefenderDistance: 2
    minPodDefenderDistance: 2
    minPodObjectiveDistance: 12
    defenderSightRange: 12
    maxAlive: 1
    maxAlivePerDefender: 0
    hardMaxAlive: 1
    orderInterval: 0.5
    stuckReorderAfter: 100
    stuckNudgeAfter: 200
    stuckRelocateAfter: 300
    uplinkPrototype: null
    phases:
    - name: ks-fieldcommander-phase-probing
      start: 0
      budget: 1
    assets:
    - entity: KsTestFcStandOffUnit
      cost: 1
      standOff: 10
";

    [Test]
    public async Task StandOffUnitIsOrderedToItsDistance([Values(25f, 3f)] float startDistance)
    {
        var testMap = await Pair.CreateTestMap();
        var defenderPosition = new Vector2(0.5f, 0.5f);
        EntityUid ruleUid = default;

        await Server.WaitPost(() =>
        {
            KsFieldCommanderTestHelpers.FillFloor(Server, testMap, HalfExtent);
            SEntMan.SpawnAtPosition(_objective, new EntityCoordinates(testMap.Grid, -25.5f, 25.5f));
            SEntMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, defenderPosition));
            ruleUid = KsFieldCommanderTestHelpers.StartRules(Server, _rule).CommanderUid;
        });

        await Pair.RunTicksSync(5);

        EntityUid unitUid = default;
        var standOff = 0f;
        var startPosition = defenderPosition + new Vector2(startDistance, 0f);

        await Server.WaitPost(() =>
        {
            var ruleComponent = SEntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            standOff = ruleComponent.Assets[0].StandOff;
            SEntMan.System<KsFieldCommanderRuleSystem>().ForceWave((ruleUid, ruleComponent));

            var units = new List<EntityUid>();
            var query = SEntMan.AllEntityQueryEnumerator<KsFieldCommanderUnitComponent>();
            while (query.MoveNext(out var uid, out var unitComponent))
            {
                if (unitComponent.RuleUid == ruleUid)
                    units.Add(uid);
            }

            Assert.That(units, Has.Count.EqualTo(1), "the wave should have sent the one unit");
            unitUid = units[0];
            SEntMan.System<SharedTransformSystem>().SetCoordinates(unitUid, new EntityCoordinates(testMap.Grid, startPosition));
        });

        Assert.That(standOff, Is.GreaterThan(0f), "precondition: the asset keeps a distance");

        // Long enough for the next orders, whenever they are due.
        await RunSeconds(1.5f);

        await Server.WaitAssertion(() =>
        {
            var unitComponent = SEntMan.GetComponent<KsFieldCommanderUnitComponent>(unitUid);
            Assert.That(unitComponent.OrderCoordinates, Is.Not.Null, "the unit should have been given orders");

            var order = SEntMan.System<SharedTransformSystem>().ToMapCoordinates(unitComponent.OrderCoordinates!.Value).Position;
            var gridOrigin = SEntMan.System<SharedTransformSystem>().GetWorldPosition(testMap.Grid.Owner);
            var defender = gridOrigin + defenderPosition;
            var start = gridOrigin + startPosition;

            // Snapped to the middle of a tile, so within a tile of the distance.
            Assert.That(Vector2.Distance(order, defender), Is.EqualTo(standOff).Within(1f),
                $"from {startDistance} tiles off, the unit should be sent {standOff} tiles from the defender");
            Assert.That(Vector2.Dot(order - defender, start - defender), Is.GreaterThan(0f),
                "it should be sent to its own side of the defender, not past them");
        });
    }
}
