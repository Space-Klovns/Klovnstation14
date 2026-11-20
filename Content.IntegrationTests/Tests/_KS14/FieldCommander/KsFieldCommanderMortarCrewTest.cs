#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.FieldCommander;
using Content.Server._KS14.Mortar;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.FieldCommander;

/// <summary>
///     A mortar crewman bought by the commander holds by its mortar once it has set it up, taking no more orders, and
///         once the mortar is gone goes at the defenders again, right up to them, its stand-off forgotten.
/// </summary>
[TestFixture]
[TestOf(typeof(KsFieldCommanderRuleSystem))]
public sealed class KsFieldCommanderMortarCrewTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _rule = "KsTestFcMortarCrewRule";
    private readonly EntProtoId _defender = "KsTestFcMortarCrewDefender";
    private readonly EntProtoId _objective = "KsTestFcMortarCrewObjective";

    private const int HalfExtent = 30;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestFcMortarCrewDefender
  components:
  - type: MobState
  - type: ScenarioFactionMember
    id: Nanotrasen
  - type: NpcFactionMember
    factions:
    - NanoTrasen

- type: entity
  id: KsTestFcMortarCrewObjective
  components:
  - type: ScenarioObjective
    factionId: Nanotrasen

- type: entity
  id: KsTestFcMortarCrewMortar
  components:
  - type: Transform
    anchored: true
  - type: KsMortar
    shell: KsTestFcMortarCrewMortar # never fired: it has no rounds
    rounds: 0
    minRange: 1
    maxRange: 100

- type: entity
  id: KsTestFcMortarCrewUnit
  components:
  - type: MobState
  - type: NpcSensors
  - type: NpcFactionMember
    factions:
    - Syndicate
  - type: KsMortarCrew
    mortar: KsTestFcMortarCrewMortar
    setUpTime: 0.2

- type: entity
  parent: BaseGameRule
  id: KsTestFcMortarCrewRule
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
    - entity: KsTestFcMortarCrewUnit
      cost: 1
      standOff: 10
";

    [Test]
    public async Task TheCrewHoldsByItsMortarAndFightsOnWithoutIt()
    {
        var testMap = await Pair.CreateTestMap();
        var defenderPosition = new Vector2(0.5f, 0.5f);
        EntityUid ruleUid = default, unitUid = default;

        await Server.WaitPost(() =>
        {
            KsFieldCommanderTestHelpers.FillFloor(Server, testMap, HalfExtent);
            SEntMan.SpawnAtPosition(_objective, new EntityCoordinates(testMap.Grid, -25.5f, 25.5f));
            SEntMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, defenderPosition));
            ruleUid = KsFieldCommanderTestHelpers.StartRules(Server, _rule).CommanderUid;
        });

        await Pair.RunTicksSync(5);

        await Server.WaitPost(() =>
        {
            var ruleComponent = SEntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            SEntMan.System<KsFieldCommanderRuleSystem>().ForceWave((ruleUid, ruleComponent));

            var units = new List<EntityUid>();
            var query = SEntMan.AllEntityQueryEnumerator<KsFieldCommanderUnitComponent>();
            while (query.MoveNext(out var uid, out var unitComponent))
            {
                if (unitComponent.RuleUid == ruleUid)
                    units.Add(uid);
            }

            Assert.That(units, Has.Count.EqualTo(1), "the wave should have sent the crewman");
            unitUid = units[0];
            Assert.That(SEntMan.GetComponent<KsFieldCommanderUnitComponent>(unitUid).GiveOrders, Is.True, "precondition: it takes orders");
        });

        // It stands still with a hostile in range, so it sets up.
        EntityUid mortarUid = default;
        for (var i = 0; i < 20 && mortarUid == default; i++)
        {
            await RunSeconds(0.25f);
            await Server.WaitPost(() =>
            {
                var mortarQuery = SEntMan.AllEntityQueryEnumerator<KsMortarComponent>();
                if (mortarQuery.MoveNext(out var uid, out _))
                    mortarUid = uid;
            });
        }

        await RunSeconds(1.5f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(mortarUid, Is.Not.EqualTo(default(EntityUid)), "the crewman should have set up");
            var unitComponent = SEntMan.GetComponent<KsFieldCommanderUnitComponent>(unitUid);
            Assert.That(unitComponent.GiveOrders, Is.False, "a crewman with its mortar up should take no more orders");

            var transformSystem = SEntMan.System<SharedTransformSystem>();
            Assert.That(Vector2.Distance(transformSystem.ToMapCoordinates(unitComponent.OrderCoordinates!.Value).Position,
                transformSystem.GetWorldPosition(mortarUid)), Is.LessThan(0.5f), "it should be told to stay where its mortar is");

            SEntMan.DeleteEntity(mortarUid);
        });

        await RunSeconds(1.5f);

        await Server.WaitAssertion(() =>
        {
            var unitComponent = SEntMan.GetComponent<KsFieldCommanderUnitComponent>(unitUid);
            Assert.That(unitComponent.GiveOrders, Is.True, "with its mortar gone it should take orders again");
            Assert.That(unitComponent.IgnoreStandOff, Is.True, "and go right up to the defenders");

            var transformSystem = SEntMan.System<SharedTransformSystem>();
            var order = transformSystem.ToMapCoordinates(unitComponent.OrderCoordinates!.Value).Position;
            var defender = transformSystem.GetWorldPosition(testMap.Grid.Owner) + defenderPosition;
            Assert.That(Vector2.Distance(order, defender), Is.LessThan(1.5f), "its next order should be at the defender, not ten tiles off");
        });
    }
}
