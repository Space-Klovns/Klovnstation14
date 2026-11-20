#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Queries.Queries;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.FieldCommander;

/// <summary>
///     An objective capper lands well beyond its vision of any objective, and must still find the nearest one of
///         another side and walk to it, rather than stand idle; and with none left, go for the defenders.
/// </summary>
[TestFixture]
[TestOf(typeof(KsScenarioObjectiveQuery))]
public sealed class KsFieldCommanderCapperTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _walker = "KsTestCapperWalker";
    private readonly EntProtoId _nanotrasenObjective = "KsTestCapperObjectiveNanotrasen";
    private readonly EntProtoId _syndicateObjective = "KsTestCapperObjectiveSyndicate";
    private readonly EntProtoId _defender = "KsTestCapperDefender";
    private const string Root = "KsTestCapperRoot";

    // The private module's borg: run only where it is present.
    private readonly EntProtoId _realCapper = "KsFcMobObjectiveCapper";

    /// <summary>
    ///     Sees five tiles, and so finds nothing with a vision-bound query.
    /// </summary>
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsTestCapperWalker
  components:
  - type: InputMover
  - type: MobMover
  - type: MovementSpeedModifier
  - type: ScenarioFactionMember
    id: Syndicate
  - type: HTN
    enabled: false
    rootTask:
      task: IdleCompound
    blackboard:
      VisionRadius: !type:Single
        5
      AggroVisionRadius: !type:Single
        5

- type: entity
  id: KsTestCapperObjectiveNanotrasen
  components:
  - type: ScenarioObjective
    factionId: Nanotrasen

- type: entity
  id: KsTestCapperObjectiveSyndicate
  components:
  - type: ScenarioObjective
    factionId: Syndicate

- type: entity
  id: KsTestCapperDefender
  components:
  - type: MobState
  - type: ScenarioFactionMember
    id: Nanotrasen

- type: utilityQuery
  id: KsTestCapperObjectives
  query:
  - !type:KsScenarioObjectiveQuery
  considerations:
  - !type:KsTargetDistanceUnboundedCon
    curve: !type:QuadraticCurve

- type: htnCompound
  id: KsTestCapperRoot
  branches:
  - tasks:
    - !type:HTNPrimitiveTask
      operator: !type:UtilityOperator
        proto: KsTestCapperObjectives
        key: Objective
        keyCoordinates: ObjectiveCoordinates
    - !type:HTNPrimitiveTask
      operator: !type:MoveToOperator
        pathfindInPlanning: true
        targetKey: ObjectiveCoordinates
        pathfindKey: ObjectivePathfind
        rangeKey: MeleeRange
";

    /// <summary>
    ///     Along a strip of floor 81 tiles long, so that the far objective is well past any vision.
    /// </summary>
    private static readonly Vector2i Min = new(-40, -3);
    private static readonly Vector2i Max = new(40, 3);

    private async Task<EntityUid> MakeStrip()
    {
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default;

        await Server.WaitPost(() =>
        {
            gridUid = MakeGrid(SEntMan, Server.ResolveDependency<ITileDefinitionManager>(), map.MapId, map.Grid, Min, Max).Owner;
            GiveGravity(SEntMan, gridUid);
        });

        await WaitForNavmesh(Pair, gridUid, Min, Max);
        return gridUid;
    }

    private static EntityCoordinates At(EntityUid gridUid, int x)
    {
        return new EntityCoordinates(gridUid, new Vector2(x + 0.5f, 0.5f));
    }

    private float DistanceBetween(EntityUid a, EntityUid b)
    {
        var transformSystem = SEntMan.System<SharedTransformSystem>();
        return Vector2.Distance(transformSystem.GetWorldPosition(a), transformSystem.GetWorldPosition(b));
    }

    /// <summary>
    ///     Runs until <paramref name="mover"/> is within <paramref name="within"/> tiles of <paramref name="target"/>, up to
    ///         a generous cap for the walk, and returns how near it got.
    /// </summary>
    private async Task<float> WalkTowards(EntityUid mover, EntityUid target, float within)
    {
        var distance = float.MaxValue;
        for (var i = 0; i < 120 && distance > within; i++)
        {
            await RunSeconds(0.5f);
            await Server.WaitPost(() => distance = DistanceBetween(mover, target));
        }

        return distance;
    }

    private void Wake(EntityUid npcUid, string? root)
    {
        var htnComponent = SEntMan.GetComponent<HTNComponent>(npcUid);
        if (root != null)
            htnComponent.RootTask = new HTNCompoundTask { Task = root };

        SEntMan.System<HTNSystem>().SetHTNEnabled((npcUid, htnComponent), true);
        SEntMan.System<NPCSystem>().WakeNPC(npcUid, htnComponent);
    }

    /// <summary>
    ///     With vision for five tiles, its own side's objective three tiles off and the other side's sixty: it picks the
    ///         other side's and walks there.
    /// </summary>
    [Test]
    public async Task PicksTheOtherSidesObjectiveBeyondSightAndClosesIn()
    {
        var gridUid = await MakeStrip();
        EntityUid walkerUid = default;
        EntityUid farUid = default;
        EntityUid nearUid = default;
        var startDistance = 0f;

        await Server.WaitPost(() =>
        {
            walkerUid = SEntMan.SpawnEntity(_walker, At(gridUid, -32));
            nearUid = SEntMan.SpawnEntity(_syndicateObjective, At(gridUid, -29));
            farUid = SEntMan.SpawnEntity(_nanotrasenObjective, At(gridUid, 28));
            startDistance = DistanceBetween(walkerUid, farUid);
            Wake(walkerUid, Root);
        });

        await RunSeconds(2f);

        await Server.WaitAssertion(() =>
        {
            var blackboard = SEntMan.GetComponent<HTNComponent>(walkerUid).Blackboard;
            Assert.That(blackboard.TryGetValue<EntityUid>("Objective", out var objectiveUid, SEntMan), Is.True,
                "it should have picked an objective, though none is in sight");
            Assert.That(objectiveUid, Is.EqualTo(farUid),
                $"it should go for the other side's objective, not its own {SEntMan.ToPrettyString(nearUid)} beside it");
        });

        var distance = await WalkTowards(walkerUid, farUid, startDistance / 2f);
        Assert.That(distance, Is.LessThanOrEqualTo(startDistance / 2f),
            $"it should have walked at least half of the {startDistance:0} tiles to the objective, but is still {distance:0.0} away");
    }

    /// <summary>
    ///     The real borg does the same: past its own side's objective to the defenders', sixty tiles off.
    /// </summary>
    [Test]
    public async Task RealCapperHeadsForADistantObjective()
    {
        if (!SProtoMan.HasIndex(_realCapper))
            Assert.Ignore("the objective borg is in the private module");

        var gridUid = await MakeStrip();
        EntityUid capperUid = default;
        EntityUid farUid = default;
        var startDistance = 0f;

        await Server.WaitPost(() =>
        {
            capperUid = SEntMan.SpawnEntity(_realCapper, At(gridUid, -32));
            SEntMan.SpawnEntity(_syndicateObjective, At(gridUid, -29));
            farUid = SEntMan.SpawnEntity(_nanotrasenObjective, At(gridUid, 28));
            startDistance = DistanceBetween(capperUid, farUid);
            Wake(capperUid, root: null);
        });

        var distance = await WalkTowards(capperUid, farUid, startDistance / 2f);
        Assert.That(distance, Is.LessThanOrEqualTo(startDistance / 2f),
            $"the borg should have walked at least half of the {startDistance:0} tiles to the objective, but is still {distance:0.0} away");
    }

    /// <summary>
    ///     The real borg with no objective left on the map goes for the nearest defender instead, at any distance.
    /// </summary>
    [Test]
    public async Task RealCapperWithNoObjectiveGoesForADefender()
    {
        if (!SProtoMan.HasIndex(_realCapper))
            Assert.Ignore("the objective borg is in the private module");

        var gridUid = await MakeStrip();
        EntityUid capperUid = default;
        EntityUid defenderUid = default;
        var startDistance = 0f;

        await Server.WaitPost(() =>
        {
            capperUid = SEntMan.SpawnEntity(_realCapper, At(gridUid, -32));
            defenderUid = SEntMan.SpawnEntity(_defender, At(gridUid, 20));
            startDistance = DistanceBetween(capperUid, defenderUid);
            Wake(capperUid, root: null);
        });

        var distance = await WalkTowards(capperUid, defenderUid, startDistance / 2f);
        Assert.That(distance, Is.LessThanOrEqualTo(startDistance / 2f),
            $"the borg should have gone for the defender {startDistance:0} tiles off, but is still {distance:0.0} away");
    }
}
