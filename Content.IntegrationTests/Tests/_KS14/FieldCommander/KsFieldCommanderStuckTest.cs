#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Dissolve;
using Content.Server._KS14.FieldCommander;
using Content.Server._KS14.NPC.PlayDead;
using Content.Server._KS14.NPC.Squad.Tactics;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared._KS14.NPC;
using Robust.Shared.Containers;
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
    private readonly EntProtoId _pairRule = "KsTestFcStuckPairRule";
    private readonly EntProtoId _playDeadRule = "KsTestFcStuckPlayDeadRule";
    private readonly EntProtoId _guardRule = "KsTestFcStuckGuardRule";
    private readonly EntProtoId _walkerRule = "KsTestFcStuckWalkerRule";
    private const string WalkTargetKey = "KsTestFcStuckWalkTarget";
    private readonly EntProtoId _nudgeRule = "KsTestFcStuckNudgeRule";
    private readonly EntProtoId _forcedNudgeRule = "KsTestFcStuckForcedNudgeRule";
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
  id: KsTestFcStuckNudgeRule
  components:
  - type: KsFieldCommanderRule
    stuckRelocateAfter: 1000
    stuckNudgeInSightAfter: 1000

- type: entity
  parent: KsTestFcStuckNudgeRule
  id: KsTestFcStuckForcedNudgeRule
  components:
  - type: KsFieldCommanderRule
    stuckNudgeInSightAfter: 3

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

# Two units for one purchase.
- type: entity
  parent: KsTestFcStuckRule
  id: KsTestFcStuckPairRule
  components:
  - type: KsFieldCommanderRule
    maxAlive: 2
    hardMaxAlive: 2
    assets:
    - entity: KsTestFcStuckUnit
      count: 2
      cost: 1

- type: npcMeter
  id: KsTestFcStuckMeter

# Plays dead the moment it is asked to decide.
- type: entity
  parent: KsTestFcStuckUnit
  id: KsTestFcStuckPlayDeadUnit
  components:
  - type: NpcPlayDead
    meter: KsTestFcStuckMeter
    threshold: 0
    chance: 1

- type: entity
  parent: KsTestFcStuckRule
  id: KsTestFcStuckPlayDeadRule
  components:
  - type: KsFieldCommanderRule
    assets:
    - entity: KsTestFcStuckPlayDeadUnit
      cost: 1

- type: entity
  id: KsTestFcStuckUplink

# An uplink with a guard bought for it.
- type: entity
  parent: KsTestFcStuckRule
  id: KsTestFcStuckGuardRule
  components:
  - type: KsFieldCommanderRule
    uplinkPrototype: KsTestFcStuckUplink
    phases:
    - name: ks-fieldcommander-phase-probing
      start: 0
      budget: 1
      uplinkGuardBudget: 1

# Walks where its blackboard says, through its own HTN and steering, finding its path as it goes.
- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsTestFcStuckWalker
  components:
  - type: InputMover
  - type: MobMover
  - type: MovementSpeedModifier
  - type: HTN
    enabled: false
    rootTask:
      task: KsTestFcStuckWalkCompound

- type: htnCompound
  id: KsTestFcStuckWalkCompound
  branches:
  - tasks:
    - !type:HTNPrimitiveTask
      operator: !type:MoveToOperator
        pathfindInPlanning: false
        targetKey: KsTestFcStuckWalkTarget

- type: entity
  parent: KsTestFcStuckSlowRule
  id: KsTestFcStuckWalkerRule
  components:
  - type: KsFieldCommanderRule
    assets:
    - entity: KsTestFcStuckWalker
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
    private void BuildBox(TestMapData testMap, params Vector2i[] open)
    {
        for (var x = -1; x <= 1; x++)
        {
            for (var y = -1; y <= 1; y++)
            {
                if ((x != 0 || y != 0) && !open.Contains(new Vector2i(x, y)))
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
    ///     How far a unit stands from the middle of the box.
    /// </summary>
    private float DistanceFromBox(EntityUid unitUid)
    {
        var position = SEntMan.System<SharedTransformSystem>().GetMapCoordinates(unitUid).Position;
        return Vector2.Distance(position, new Vector2(Box.X + 0.5f, Box.Y + 0.5f));
    }

    /// <summary>
    ///     Runs the rule's checks until the unit has reached the nudge, up to well past its time.
    /// </summary>
    private async Task RunUntilNudge(EntityUid ruleUid, EntityUid unitUid)
    {
        var stepSeconds = 0f;
        var maxSteps = 0;
        await Server.WaitPost(() =>
        {
            var ruleComponent = SEntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            stepSeconds = (float)ruleComponent.OrderInterval.TotalSeconds / 2f;
            maxSteps = (int)MathF.Ceiling((float)((ruleComponent.StuckNudgeAfter + ruleComponent.OrderInterval * 6) / ruleComponent.OrderInterval)) * 2;
        });

        var reached = false;
        for (var i = 0; i < maxSteps && !reached; i++)
        {
            await RunSeconds(stepSeconds);
            await Server.WaitPost(() =>
                reached = SEntMan.GetComponent<KsFieldCommanderUnitComponent>(unitUid).StuckStage >= KsFieldCommanderStuckStage.Nudge);
        }

        Assert.That(reached, Is.True, "the unit should have been found stuck as far as the nudge");
    }

    /// <summary>
    ///     A unit in the open is nudged onto a tile beside it, but not while a defender can see it: then it stays
    ///         where it is, and the nudge waits.
    /// </summary>
    [Test]
    public async Task NudgeWaitsUntilNoDefenderSees([Values] bool seen)
    {
        var defenderPosition = seen
            ? new Vector2(Box.X + DefenderOffset + 0.5f, Box.Y + 0.5f)
            : new Vector2(18.5f, 18.5f);
        var (_, ruleUid, unitUid) = await SetUp(_nudgeRule, defenderPosition);

        await RunUntilNudge(ruleUid, unitUid);

        // A few more checks, for a held-back nudge to have had every chance to happen.
        var seconds = 0f;
        await Server.WaitPost(() =>
            seconds = (float)(SEntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid).OrderInterval * 4).TotalSeconds);
        await RunSeconds(seconds);

        await Server.WaitAssertion(() =>
        {
            var unitComponent = SEntMan.GetComponent<KsFieldCommanderUnitComponent>(unitUid);
            if (seen)
            {
                Assert.That(DistanceFromBox(unitUid), Is.LessThan(0.01f), "a unit a defender can see should not be nudged in front of them");
                Assert.That(unitComponent.NudgeHeldSince, Is.Not.Null, "its nudge should be waiting");
            }
            else
            {
                Assert.That(DistanceFromBox(unitUid), Is.InRange(0.9f, 1.5f), "a unit nobody sees should be nudged onto a tile beside it");
                Assert.That(unitComponent.NudgeHeldSince, Is.Null, "with nothing left waiting");
            }
        });
    }

    /// <summary>
    ///     A nudge held back by a defender watching for longer than the rule allows is made in their sight anyway, and
    ///         not before.
    /// </summary>
    [Test]
    public async Task NudgeHeldBackTooLongHappensInSight()
    {
        var (_, ruleUid, unitUid) = await SetUp(_forcedNudgeRule, new Vector2(Box.X + DefenderOffset + 0.5f, Box.Y + 0.5f));

        await RunUntilNudge(ruleUid, unitUid);

        var stepSeconds = 0f;
        var maxSteps = 0;
        var cap = TimeSpan.Zero;
        TimeSpan? heldSince = null;
        await Server.WaitPost(() =>
        {
            var ruleComponent = SEntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            cap = ruleComponent.StuckNudgeInSightAfter;
            stepSeconds = (float)ruleComponent.OrderInterval.TotalSeconds / 2f;
            maxSteps = (int)MathF.Ceiling((float)((cap + ruleComponent.OrderInterval * 6) / ruleComponent.OrderInterval)) * 2;
            heldSince = SEntMan.GetComponent<KsFieldCommanderUnitComponent>(unitUid).NudgeHeldSince;
        });

        Assert.That(heldSince, Is.Not.Null, "the nudge should first have been held back, in sight");

        var moved = false;
        var movedAfter = TimeSpan.Zero;
        for (var i = 0; i < maxSteps && !moved; i++)
        {
            await RunSeconds(stepSeconds);
            await Server.WaitPost(() =>
            {
                moved = DistanceFromBox(unitUid) > 0.01f;
                movedAfter = Server.Timing.CurTime - heldSince!.Value;
            });
        }

        Assert.Multiple(() =>
        {
            Assert.That(moved, Is.True, "a nudge held back past the cap should be made in sight");
            Assert.That(movedAfter, Is.GreaterThanOrEqualTo(cap), "but not before the cap is up");
        });
    }

    /// <summary>
    ///     The rule started on a floor with one defender, a wave sent, and every unit it sent put in the box.
    /// </summary>
    private async Task<(TestMapData Map, EntityUid RuleUid, List<EntityUid> UnitUids)> SetUpUnits(EntProtoId rule, Vector2 defenderPosition)
    {
        var testMap = await Pair.CreateTestMap();
        EntityUid ruleUid = default;
        var unitUids = new List<EntityUid>();

        await Server.WaitPost(() =>
        {
            KsFieldCommanderTestHelpers.FillFloor(Server, testMap, HalfExtent);
            SEntMan.SpawnAtPosition(_objective, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            SEntMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, defenderPosition));
            ruleUid = KsFieldCommanderTestHelpers.StartRules(Server, rule).CommanderUid;
        });

        await Pair.RunTicksSync(5);

        await Server.WaitPost(() =>
        {
            SEntMan.System<KsFieldCommanderRuleSystem>().ForceWave((ruleUid, SEntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid)));
            unitUids.AddRange(GetUnits(ruleUid));
            foreach (var unitUid in unitUids)
            {
                SEntMan.System<SharedTransformSystem>().SetCoordinates(unitUid, BoxCentre(testMap));
            }
        });

        return (testMap, ruleUid, unitUids);
    }

    /// <summary>
    ///     Runs the rule's checks until none of <paramref name="unitUids"/> is the commander's any more, up to well past
    ///         the last step. Whether they all went.
    /// </summary>
    private async Task<bool> RunUntilTakenOff(EntityUid ruleUid, List<EntityUid> unitUids)
    {
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
            await Server.WaitPost(() => removed = unitUids.All(unitUid => !SEntMan.HasComponent<KsFieldCommanderUnitComponent>(unitUid)));
        }

        return removed;
    }

    /// <summary>
    ///     Units that come two for one purchase, refunded, give back the purchase once between them: each its share.
    /// </summary>
    [Test]
    public async Task RefundIsTheUnitsShareOfItsPurchase()
    {
        var (testMap, ruleUid, unitUids) = await SetUpUnits(_pairRule, new Vector2(18.5f, 18.5f));
        var budgetBefore = 0f;
        var cost = 0f;

        await Server.WaitPost(() =>
        {
            var ruleComponent = SEntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            Assert.That(ruleComponent.Assets[0].Count, Is.GreaterThan(1), "the test needs a purchase of more than one unit");
            Assert.That(unitUids, Has.Count.EqualTo(ruleComponent.Assets[0].Count), "the wave should have bought one purchase");

            BuildBox(testMap);
            ClearFloorButTheBox(testMap);
            budgetBefore = ruleComponent.CarriedBudget;
            cost = ruleComponent.Assets[0].Cost;
        });

        Assert.That(await RunUntilTakenOff(ruleUid, unitUids), Is.True, "both boxed-in units should have been taken off the field");

        await Server.WaitAssertion(() =>
        {
            var ruleComponent = SEntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            Assert.That(ruleComponent.StuckUnitsRefunded, Is.EqualTo(unitUids.Count), "with nowhere to send them, each should be refunded");
            Assert.That(ruleComponent.CarriedBudget, Is.EqualTo(budgetBefore + cost).Within(0.001f),
                "between them they should give back what the one purchase cost, not that for each");
        });
    }

    /// <summary>
    ///     What keeps a unit still on purpose, besides a fight.
    /// </summary>
    public enum StillReason
    {
        /// <summary>The control: nothing, so it is taken off the field.</summary>
        None,
        /// <summary>Inside something, as in a locker it hid in.</summary>
        InContainer,
        /// <summary>Holding a spot a hunt told it to hold.</summary>
        HoldingForAHunt,
        /// <summary>Playing dead.</summary>
        PlayingDead,
    }

    /// <summary>
    ///     A unit in the box that is still on purpose (inside something, holding a spot for a hunt, playing dead) is never
    ///         found stuck, and so never pulled out, nudged or taken off the field; one that is still for nothing is.
    /// </summary>
    [Test]
    public async Task UnitStillOnPurposeIsNotStuck([Values] StillReason reason)
    {
        var (testMap, ruleUid, unitUids) = await SetUpUnits(reason == StillReason.PlayingDead ? _playDeadRule : _rule, new Vector2(18.5f, 18.5f));
        Assert.That(unitUids, Has.Count.EqualTo(1));
        var unitUid = unitUids[0];
        EntityUid holderUid = default;

        await Server.WaitPost(() =>
        {
            BuildBox(testMap);

            switch (reason)
            {
                case StillReason.InContainer:
                    var containerSystem = SEntMan.System<SharedContainerSystem>();
                    holderUid = SEntMan.SpawnEntity(null, BoxCentre(testMap));
                    var container = containerSystem.EnsureContainer<ContainerSlot>(holderUid, "ks-test-hold");
                    Assert.That(containerSystem.Insert(unitUid, container), Is.True);
                    break;
                case StillReason.HoldingForAHunt:
                    var tacticsSystem = SEntMan.System<NpcSquadTacticsSystem>();
                    tacticsSystem.UpdatesPaused = true; // undone at round restart; the pair is dirty
                    tacticsSystem.IssueOrder(unitUid, NpcOrderKind.Stage, BoxCentre(testMap));
                    break;
                case StillReason.PlayingDead:
                    var playDeadSystem = SEntMan.System<NpcPlayDeadSystem>();
                    playDeadSystem.UpdateNow(unitUid);
                    Assert.That(playDeadSystem.IsPlayingDead(unitUid), Is.True, "the unit should be playing dead");
                    break;
            }
        });

        var removed = await RunUntilTakenOff(ruleUid, unitUids);

        await Server.WaitAssertion(() =>
        {
            if (reason == StillReason.None)
            {
                Assert.That(removed, Is.True, "control: a unit still for no reason should have been taken off the field");
                return;
            }

            Assert.That(removed, Is.False, $"a unit still on purpose ({reason}) should never be taken off the field");
            Assert.That(SEntMan.GetComponent<KsFieldCommanderUnitComponent>(unitUid).StuckStage, Is.EqualTo(KsFieldCommanderStuckStage.None),
                "nor found stuck at all");

            if (reason == StillReason.InContainer)
                Assert.That(SEntMan.System<SharedContainerSystem>().IsEntityInContainer(unitUid), Is.True, "and it should still be inside");
        });
    }

    /// <summary>
    ///     A guard put by the uplink to stay there is never taken off the field and sent from somewhere else, nor
    ///         refunded into the waves' budget, however stuck it gets.
    /// </summary>
    [Test]
    public async Task UplinkGuardIsNeverTakenOffTheField()
    {
        var testMap = await Pair.CreateTestMap();
        EntityUid ruleUid = default;

        await Server.WaitPost(() =>
        {
            KsFieldCommanderTestHelpers.FillFloor(Server, testMap, HalfExtent);
            SEntMan.SpawnAtPosition(_objective, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            SEntMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, 18.5f, 18.5f));
            ruleUid = KsFieldCommanderTestHelpers.StartRules(Server, _guardRule).CommanderUid;
        });

        await Pair.RunTicksSync(5);

        var unitUids = new List<EntityUid>();
        var budgetBefore = 0f;
        await Server.WaitPost(() =>
        {
            var ruleComponent = SEntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            SEntMan.System<KsFieldCommanderRuleSystem>().ForceUplink((ruleUid, ruleComponent));
            unitUids.AddRange(GetUnits(ruleUid));
            Assert.That(unitUids, Has.Count.EqualTo(1), "the uplink should have been given one guard");

            // Boxed in, with somewhere it would be sent from.
            SEntMan.System<SharedTransformSystem>().SetCoordinates(unitUids[0], BoxCentre(testMap));
            BuildBox(testMap);
            budgetBefore = ruleComponent.CarriedBudget;
        });

        var relocateSeconds = 0f;
        await Server.WaitPost(() =>
        {
            var ruleComponent = SEntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            relocateSeconds = (float)(ruleComponent.StuckRelocateAfter + ruleComponent.OrderInterval * 3).TotalSeconds;
        });

        // Going somewhere it cannot get to, as its own HTN would keep it: steering thrown away is taken up again.
        var removed = false;
        for (var i = 0; i < 40 && !removed; i++)
        {
            await Server.WaitPost(() =>
            {
                if (!SEntMan.HasComponent<NPCSteeringComponent>(unitUids[0]))
                    SEntMan.System<NPCSteeringSystem>().Register(unitUids[0], new EntityCoordinates(testMap.Grid, 20.5f, -20.5f));
            });

            await RunSeconds(relocateSeconds / 10f);
            await Server.WaitPost(() => removed = !SEntMan.HasComponent<KsFieldCommanderUnitComponent>(unitUids[0]));
        }

        await Server.WaitAssertion(() =>
        {
            var ruleComponent = SEntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            Assert.That(removed, Is.False, "a guard should never be taken off the field for being stuck");
            Assert.That(SEntMan.System<KsDissolveSystem>().IsDissolving(unitUids[0]), Is.False);
            Assert.That(ruleComponent.StuckUnitsRelocated + ruleComponent.StuckUnitsRefunded, Is.Zero, "nor sent again or refunded");
            Assert.That(ruleComponent.CarriedBudget, Is.EqualTo(budgetBefore).Within(0.001f), "the waves' budget never paid for it");
        });
    }

    /// <summary>
    ///     A unit boxed in on all sides but one corner, with both tiles beside that corner walled, is not nudged through
    ///         it to the far side; with one of them open, it is nudged.
    /// </summary>
    [Test]
    public async Task NudgeDoesNotSlipThroughACorner([Values] bool besideOpen)
    {
        // The defender far off to the north-east, which is where the unit is going, through the open corner.
        var (testMap, ruleUid, unitUid) = await SetUp(_nudgeRule, new Vector2(18.5f, 18.5f));

        await Server.WaitPost(() =>
        {
            if (besideOpen)
                BuildBox(testMap, new Vector2i(1, 1), new Vector2i(1, 0));
            else
                BuildBox(testMap, new Vector2i(1, 1));
        });

        await RunUntilNudge(ruleUid, unitUid);

        await Server.WaitAssertion(() =>
        {
            if (besideOpen)
                Assert.That(DistanceFromBox(unitUid), Is.InRange(0.9f, 1.5f), "control: with a way past the corner, it should be nudged");
            else
                Assert.That(DistanceFromBox(unitUid), Is.LessThan(0.01f), "it should not be nudged corner to corner between two walls");
        });
    }

    /// <summary>
    ///     A unit whose move only had to get within a longer range than the rule's arrival distance, and did, is there:
    ///         not stuck for standing at that range.
    /// </summary>
    [Test]
    public async Task UnitInRangeOfALongerMoveHasArrived()
    {
        // The unit five tiles west of the defender it is ordered at: out of contact, further than the rule's arrival.
        var defenderPosition = new Vector2(Box.X + 5.5f, Box.Y + 0.5f);
        var (testMap, ruleUid, unitUid) = await SetUp(_rule, defenderPosition);
        var defenderCoordinates = new EntityCoordinates(testMap.Grid, defenderPosition);

        var seconds = 0f;
        await Server.WaitPost(() =>
        {
            var ruleComponent = SEntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            Assert.That(ruleComponent.StuckArrivedDistance, Is.LessThan(5f), "the test needs the unit further off than the rule's arrival distance");
            Assert.That(ruleComponent.StuckContactRange, Is.LessThan(5f), "and out of contact");
            seconds = (float)(ruleComponent.StuckRelocateAfter + ruleComponent.OrderInterval * 6).TotalSeconds;

            // A move to within six tiles of the defender, which has ended there, in range.
            var steeringSystem = SEntMan.System<NPCSteeringSystem>();
            var steeringComponent = steeringSystem.Register(unitUid, defenderCoordinates);
            steeringComponent.Range = 6f;
            steeringComponent.Status = SteeringStatus.InRange;
            steeringSystem.Unregister(unitUid);
        });

        // The rule orders it at the defender, the same spot its move was to.
        var worst = KsFieldCommanderStuckStage.None;
        for (var i = 0; i < 20; i++)
        {
            await RunSeconds(seconds / 20f);
            await Server.WaitPost(() =>
            {
                var stage = SEntMan.GetComponent<KsFieldCommanderUnitComponent>(unitUid).StuckStage;
                if (stage > worst)
                    worst = stage;
            });
        }

        Assert.That(worst, Is.EqualTo(KsFieldCommanderStuckStage.None), "a unit standing within its move's range of where it was going should not be stuck");
    }

    /// <summary>
    ///     A unit whose own HTN move finds no path (the move fails, and takes its steering with it, well before the next
    ///         check) moves on to the next step at once, long before its time is up.
    /// </summary>
    [Test]
    public async Task NoPathAdvancesAStepAtOnce()
    {
        var (testMap, ruleUid, unitUid) = await SetUp(_walkerRule, new Vector2(18.5f, 18.5f));

        await Server.WaitPost(() => BuildBox(testMap));
        await WaitForNavmeshSettled(Pair, testMap.Grid.Owner);

        var seconds = 0f;
        await Server.WaitPost(() =>
        {
            var ruleComponent = SEntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            seconds = (float)(ruleComponent.OrderInterval * 8).TotalSeconds;
            Assert.That(TimeSpan.FromSeconds(seconds), Is.LessThan(ruleComponent.StuckReorderAfter),
                "the wait should be far shorter than the first step's time");

            // Out of the box, where there is no way to.
            var htnComponent = SEntMan.GetComponent<HTNComponent>(unitUid);
            htnComponent.Blackboard.SetValue(WalkTargetKey, new EntityCoordinates(testMap.Grid, 20.5f, -20.5f));
            SEntMan.System<HTNSystem>().SetHTNEnabled((unitUid, htnComponent), true);
            SEntMan.System<NPCSystem>().WakeNPC(unitUid, htnComponent);
        });

        var stage = KsFieldCommanderStuckStage.None;
        var stuckReason = KsFieldCommanderStuckReason.None;
        for (var i = 0; i < 16 && stage == KsFieldCommanderStuckStage.None; i++)
        {
            await RunSeconds(seconds / 16f);
            await Server.WaitPost(() =>
            {
                var unitComponent = SEntMan.GetComponent<KsFieldCommanderUnitComponent>(unitUid);
                stage = unitComponent.StuckStage;
                stuckReason = unitComponent.StuckReason;
            });
        }

        Assert.Multiple(() =>
        {
            Assert.That(stage, Is.GreaterThanOrEqualTo(KsFieldCommanderStuckStage.Reorder),
                "a unit whose move found no path should not wait out the clock");
            Assert.That(stuckReason, Is.EqualTo(KsFieldCommanderStuckReason.NoPath));
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
