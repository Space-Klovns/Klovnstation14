#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Dissolve;
using Content.Server._KS14.FieldCommander;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.FieldCommander;

/// <summary>
///     Units that get nowhere are found and dealt with in steps: ordered elsewhere, nudged, and at last taken off the
///         field and sent again, or refunded; never in front of a defender. And units that are not mobs still count
///         towards how many the commander has alive.
/// </summary>
[TestFixture]
[TestOf(typeof(KsFieldCommanderRuleSystem))]
public sealed class KsFieldCommanderStuckTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _rule = "KsTestFcStuckRule";
    private readonly EntProtoId _slowRule = "KsTestFcStuckSlowRule";
    private readonly EntProtoId _capRule = "KsTestFcStuckCapRule";
    private readonly EntProtoId _defender = "KsTestFcStuckDefender";
    private readonly EntProtoId _objective = "KsTestFcStuckObjective";

    private const int HalfExtent = 30;

    /// <summary>
    ///     How far east of the box the defender stands in the sight test.
    /// </summary>
    private const float DefenderOffset = 8f;

    /// <summary>
    ///     Units that stand where they are put, so that only the rule moves them. Checks every half second; the steps
    ///         a second apart.
    /// </summary>
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestFcStuckDefender
  components:
  - type: MobState
  - type: ScenarioFactionMember
    id: Nanotrasen

- type: entity
  id: KsTestFcStuckObjective
  components:
  - type: ScenarioObjective
    factionId: Nanotrasen

- type: entity
  id: KsTestFcStuckUnit
  components:
  - type: MobState
  - type: NpcSensors

- type: entity
  id: KsTestFcStuckDrone
  components:
  - type: NpcSensors

- type: entity
  parent: BaseGameRule
  id: KsTestFcStuckRule
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
    stuckReorderAfter: 1
    stuckNudgeAfter: 2
    stuckRelocateAfter: 3
    stuckContactRange: 2
    uplinkPrototype: null
    phases:
    - name: ks-fieldcommander-phase-probing
      start: 0
      budget: 1
    assets:
    - entity: KsTestFcStuckUnit
      cost: 1

- type: entity
  parent: KsTestFcStuckRule
  id: KsTestFcStuckSlowRule
  components:
  - type: KsFieldCommanderRule
    stuckReorderAfter: 100
    stuckNudgeAfter: 200
    stuckRelocateAfter: 300

- type: entity
  parent: KsTestFcStuckRule
  id: KsTestFcStuckCapRule
  components:
  - type: KsFieldCommanderRule
    maxAlive: 2
    hardMaxAlive: 2
    phases:
    - name: ks-fieldcommander-phase-probing
      start: 0
      budget: 10
    assets:
    - entity: KsTestFcStuckDrone
      cost: 1
";

    /// <summary>
    ///     Where a stuck unit is put: well inside the ring units are sent in from, so that it is not there by chance.
    /// </summary>
    private static readonly Vector2i Box = new(-8, 0);

    [Test]
    public async Task StagesComeInOrder()
    {
        await Server.WaitAssertion(() =>
        {
            Assert.That(SProtoMan.Index(_rule).TryComp<KsFieldCommanderRuleComponent>(out var component, SEntMan.ComponentFactory), Is.True);
            var epsilon = TimeSpan.FromMilliseconds(1);

            Assert.Multiple(() =>
            {
                Assert.That(KsFieldCommanderRuleSystem.GetStuckStage(component!, TimeSpan.Zero), Is.EqualTo(KsFieldCommanderStuckStage.None));
                Assert.That(KsFieldCommanderRuleSystem.GetStuckStage(component!, component!.StuckReorderAfter - epsilon), Is.EqualTo(KsFieldCommanderStuckStage.None));
                Assert.That(KsFieldCommanderRuleSystem.GetStuckStage(component, component.StuckReorderAfter), Is.EqualTo(KsFieldCommanderStuckStage.Reorder));
                Assert.That(KsFieldCommanderRuleSystem.GetStuckStage(component, component.StuckNudgeAfter - epsilon), Is.EqualTo(KsFieldCommanderStuckStage.Reorder));
                Assert.That(KsFieldCommanderRuleSystem.GetStuckStage(component, component.StuckNudgeAfter), Is.EqualTo(KsFieldCommanderStuckStage.Nudge));
                Assert.That(KsFieldCommanderRuleSystem.GetStuckStage(component, component.StuckRelocateAfter - epsilon), Is.EqualTo(KsFieldCommanderStuckStage.Nudge));
                Assert.That(KsFieldCommanderRuleSystem.GetStuckStage(component, component.StuckRelocateAfter), Is.EqualTo(KsFieldCommanderStuckStage.Relocate));
                Assert.That(KsFieldCommanderRuleSystem.GetStuckStage(component, component.StuckRelocateAfter * 10), Is.EqualTo(KsFieldCommanderStuckStage.Relocate));
            });
        });
    }

    /// <summary>
    ///     A floor with the defenders' objective in the middle and one defender, the rule started, and one unit sent.
    /// </summary>
    private async Task<(TestMapData Map, EntityUid RuleUid, EntityUid UnitUid)> SetUp(EntProtoId rule, Vector2 defenderPosition)
    {
        var testMap = await Pair.CreateTestMap();
        EntityUid ruleUid = default;

        await Server.WaitPost(() =>
        {
            KsFieldCommanderTestHelpers.FillFloor(Server, testMap, HalfExtent);
            SEntMan.SpawnAtPosition(_objective, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            SEntMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, defenderPosition));
            ruleUid = KsFieldCommanderTestHelpers.StartRules(Server, rule).CommanderUid;
        });

        await Pair.RunTicksSync(5);

        EntityUid unitUid = default;
        await Server.WaitPost(() =>
        {
            var system = SEntMan.System<KsFieldCommanderRuleSystem>();
            system.ForceWave((ruleUid, SEntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid)));

            var units = GetUnits(ruleUid);
            Assert.That(units, Has.Count.EqualTo(1), "the wave should have sent the one unit");
            unitUid = units[0];

            SEntMan.System<SharedTransformSystem>().SetCoordinates(unitUid, BoxCentre(testMap));
        });

        return (testMap, ruleUid, unitUid);
    }

    private static EntityCoordinates BoxCentre(TestMapData testMap)
    {
        return new EntityCoordinates(testMap.Grid, new Vector2(Box.X + 0.5f, Box.Y + 0.5f));
    }

    private List<EntityUid> GetUnits(EntityUid ruleUid)
    {
        var units = new List<EntityUid>();
        var query = SEntMan.AllEntityQueryEnumerator<KsFieldCommanderUnitComponent>();
        while (query.MoveNext(out var unitUid, out var unitComponent))
        {
            if (unitComponent.RuleUid == ruleUid)
                units.Add(unitUid);
        }

        return units;
    }

    /// <summary>
    ///     Walls on every tile round the box, so the unit can neither move nor be seen.
    /// </summary>
    private void BuildBox(TestMapData testMap)
    {
        for (var x = -1; x <= 1; x++)
        {
            for (var y = -1; y <= 1; y++)
            {
                if (x != 0 || y != 0)
                    SpawnAt(SEntMan, TestWall, testMap.Grid.Owner, Box.X + x, Box.Y + y);
            }
        }
    }

    /// <summary>
    ///     Takes the floor up everywhere but in and around the box, so there is nowhere left to send a unit from.
    /// </summary>
    private void ClearFloorButTheBox(TestMapData testMap)
    {
        var tiles = new List<(Vector2i, Tile)>();
        for (var x = -HalfExtent; x <= HalfExtent; x++)
        {
            for (var y = -HalfExtent; y <= HalfExtent; y++)
            {
                if (Math.Abs(x - Box.X) > 2 || Math.Abs(y - Box.Y) > 2)
                    tiles.Add((new Vector2i(x, y), Tile.Empty));
            }
        }

        SEntMan.System<SharedMapSystem>().SetTiles(testMap.Grid.Owner, testMap.Grid.Comp, tiles);
    }

    /// <summary>
    ///     A unit walled in where nothing sees it goes through every step and is taken off the field: sent again from
    ///         elsewhere if there is somewhere, or else refunded. Either way it stops counting at once.
    /// </summary>
    [Test]
    public async Task BoxedInUnitIsSentAgainOrRefunded([Values] bool somewhereToSend)
    {
        var (testMap, ruleUid, unitUid) = await SetUp(_rule, new Vector2(18.5f, 18.5f));
        var budgetBefore = 0f;
        var cost = 0f;

        await Server.WaitPost(() =>
        {
            BuildBox(testMap);
            if (!somewhereToSend)
                ClearFloorButTheBox(testMap);

            var ruleComponent = SEntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            budgetBefore = ruleComponent.CarriedBudget;
            cost = ruleComponent.Assets[0].Cost;
        });

        // Well past the last step, whatever the steps are set to.
        var relocateSeconds = 0f;
        await Server.WaitPost(() =>
        {
            var ruleComponent = SEntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            relocateSeconds = (float)(ruleComponent.StuckRelocateAfter + ruleComponent.OrderInterval * 3).TotalSeconds;
        });

        var removed = false;
        for (var i = 0; i < 40 && !removed; i++)
        {
            await RunSeconds(relocateSeconds / 10f);
            await Server.WaitPost(() => removed = !SEntMan.HasComponent<KsFieldCommanderUnitComponent>(unitUid));
        }

        await Server.WaitAssertion(() =>
        {
            var ruleComponent = SEntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            var units = GetUnits(ruleUid);

            Assert.That(removed, Is.True, "the boxed-in unit should have been taken off the field");
            Assert.That(SEntMan.Deleted(unitUid) || SEntMan.System<KsDissolveSystem>().IsDissolving(unitUid), Is.True,
                "it should be dissolving away");

            if (somewhereToSend)
            {
                Assert.That(ruleComponent.StuckUnitsRelocated, Is.EqualTo(1));
                Assert.That(units, Has.Count.EqualTo(1), "one like it should have been sent again in its place");
                var position = SEntMan.System<SharedTransformSystem>().GetMapCoordinates(units[0]).Position;
                Assert.That(Vector2.Distance(position, new Vector2(Box.X + 0.5f, Box.Y + 0.5f)), Is.GreaterThan(2f),
                    "the one sent again should be somewhere else");
            }
            else
            {
                Assert.That(ruleComponent.StuckUnitsRefunded, Is.EqualTo(1));
                Assert.That(units, Is.Empty, "with nowhere to send one again, the living count should drop");
                Assert.That(ruleComponent.CarriedBudget, Is.EqualTo(budgetBefore + cost).Within(0.001f),
                    "its cost should be back in the budget");
            }
        });
    }

    /// <summary>
    ///     A unit getting nowhere in plain sight of a defender goes as far as the last step, but is never taken off the
    ///         field in front of them.
    /// </summary>
    [Test]
    public async Task UnitInDefenderSightIsNeverDissolved()
    {
        // Eight tiles off in the open: in sight, and out of what counts as a fight.
        var (_, ruleUid, unitUid) = await SetUp(_rule, new Vector2(Box.X + DefenderOffset + 0.5f, Box.Y + 0.5f));

        var seconds = 0f;
        await Server.WaitPost(() =>
        {
            var ruleComponent = SEntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            Assert.That(ruleComponent.StuckContactRange, Is.LessThan(DefenderOffset), "the defender should be out of contact");
            Assert.That(ruleComponent.DefenderSightRange, Is.GreaterThan(DefenderOffset), "and in sight");
            seconds = (float)(ruleComponent.StuckRelocateAfter + ruleComponent.OrderInterval * 6).TotalSeconds;
        });

        await RunSeconds(seconds);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.TryGetComponent<KsFieldCommanderUnitComponent>(unitUid, out var unitComponent), Is.True,
                "a unit in a defender's sight should never be taken off the field");
            Assert.That(SEntMan.System<KsDissolveSystem>().IsDissolving(unitUid), Is.False, "nor dissolved in front of them");
            Assert.That(unitComponent!.StuckStage, Is.EqualTo(KsFieldCommanderStuckStage.Relocate),
                "it should still have been found stuck, all the way to the last step");
            Assert.That(GetUnits(ruleUid), Has.Count.EqualTo(1));
        });
    }

    /// <summary>
    ///     Steering that has given up on a path moves a unit on to the next step at once, long before its time is up.
    /// </summary>
    [Test]
    public async Task NoPathAdvancesAStepAtOnce()
    {
        var (testMap, ruleUid, unitUid) = await SetUp(_slowRule, new Vector2(18.5f, 18.5f));

        var seconds = 0f;
        await Server.WaitPost(() =>
        {
            var ruleComponent = SEntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            seconds = (float)(ruleComponent.OrderInterval * 4).TotalSeconds;
            Assert.That(TimeSpan.FromSeconds(seconds), Is.LessThan(ruleComponent.StuckReorderAfter),
                "the wait should be far shorter than the first step's time");

            var steeringComponent = SEntMan.System<NPCSteeringSystem>().Register(unitUid, new EntityCoordinates(testMap.Grid, 20.5f, -20.5f));
            steeringComponent.Status = SteeringStatus.NoPath;
        });

        await RunSeconds(seconds);

        await Server.WaitAssertion(() =>
        {
            var unitComponent = SEntMan.GetComponent<KsFieldCommanderUnitComponent>(unitUid);
            Assert.That(unitComponent.StuckStage, Is.GreaterThanOrEqualTo(KsFieldCommanderStuckStage.Reorder),
                "a unit whose steering found no path should not wait out the clock");
            Assert.That(unitComponent.StuckReason, Is.EqualTo(KsFieldCommanderStuckReason.NoPath));
        });
    }

    /// <summary>
    ///     Units with no mob state (borgs, drones) count towards the cap: a second wave does not add to a full field.
    /// </summary>
    [Test]
    public async Task UnitsWithoutMobStateCountTowardsTheCap()
    {
        var testMap = await Pair.CreateTestMap();
        EntityUid ruleUid = default;

        await Server.WaitPost(() =>
        {
            KsFieldCommanderTestHelpers.FillFloor(Server, testMap, HalfExtent);
            SEntMan.SpawnAtPosition(_objective, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            SEntMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, 18.5f, 18.5f));
            ruleUid = KsFieldCommanderTestHelpers.StartRules(Server, _capRule).CommanderUid;
        });

        await Pair.RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            var system = SEntMan.System<KsFieldCommanderRuleSystem>();
            var rule = new Entity<KsFieldCommanderRuleComponent>(ruleUid, SEntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid));
            var cap = KsFieldCommanderRuleSystem.GetMaxAlive(rule.Comp, 1);

            system.ForceWave(rule);
            Assert.That(GetUnits(ruleUid), Has.Count.EqualTo(cap), "the budget is far more than the cap, so the first wave should fill it");
            Assert.That(GetUnits(ruleUid).All(unitUid => !SEntMan.HasComponent<Content.Shared.Mobs.Components.MobStateComponent>(unitUid)), Is.True);

            system.ForceWave(rule);
            Assert.That(GetUnits(ruleUid), Has.Count.EqualTo(cap), "units with no mob state should still count towards the cap");
        });
    }
}
