#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     Railings are obstacles: thin, along one edge of their tile, whichever way they face. One that climbs may go over them;
///         one that does not goes round, or has no way. Laid out as a row of railings across the grid, between an NPC 3
///         tiles one side and its goal 3 tiles the other, with a gap at one end or not. For a railing facing east or west
///         the whole layout is turned with it, so the row always stands across the way.
/// </summary>
public sealed class KsNpcRailingTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsTileDangerTestWalker
  id: KsRailingTestClimber
  components:
  - type: Climbing
  - type: DoAfter
";

    private const int Half = 6;

    /// <summary>
    ///     Where along the row the gap starts, if there is one: the two tiles from here to the end are left open.
    /// </summary>
    private const int GapStart = Half - 1;

    [TestCase(0, false, false, ExpectedResult = "none")]
    [TestCase(90, false, false, ExpectedResult = "none")]
    [TestCase(180, false, false, ExpectedResult = "none")]
    [TestCase(270, false, false, ExpectedResult = "none")]
    [TestCase(0, true, false, ExpectedResult = "round")]
    [TestCase(90, true, false, ExpectedResult = "round")]
    [TestCase(180, true, false, ExpectedResult = "round")]
    [TestCase(270, true, false, ExpectedResult = "round")]
    [TestCase(0, false, true, ExpectedResult = "over")]
    public Task<string> TestRailingsBlockPaths(int facing, bool gap, bool climbs)
    {
        return FindWay(facing, gap ? GapStart : null, climbs);
    }

    /// <summary>
    ///     How far each side of the row the NPC and its goal are, in tiles.
    /// </summary>
    private const int Across = 3;

    /// <summary>
    ///     What climbing over a railing costs a path on top of walking through its tile, in tiles of walking: the climb cost,
    ///         over the part of the tile the railing takes up. A railing's poly is one of the navmesh's four steps to a tile deep.
    /// </summary>
    private static float ClimbExtra => PathfindingSystem.ClimbCost / PathfindingSystem.SubStep;

    /// <summary>
    ///     How much further it is to walk round through a gap <paramref name="gapAt"/> tiles along the row than straight
    ///         across, in tiles, as the pathfinder counts distance: diagonals at root two.
    /// </summary>
    private static float GetDetourExtra(int gapAt)
    {
        var leg = MathF.Max(gapAt, Across) + (MathF.Sqrt(2f) - 1f) * MathF.Min(gapAt, Across);
        return 2f * leg - 2f * Across;
    }

    /// <summary>
    ///     A climber walks round where that is short - an open tile a step along the row - and climbs where it is long, rather
    ///         than climbing every time: a climb is slow, and stood still.
    /// </summary>
    /// <remarks>
    ///     Where "short" and "long" are is the climb cost's to say, so both gaps are placed from it: the near one a step along,
    ///         which has to be well under what a climb costs, and the far one where walking round is twice the climb or more.
    ///         Retuning <see cref="PathfindingSystem.ClimbCost"/> moves the far gap and leaves both sides of the trade tested.
    /// </remarks>
    [TestCase(true, ExpectedResult = "round")]
    [TestCase(false, ExpectedResult = "over")]
    public Task<string> TestClimberPrefersWalkingRound(bool near)
    {
        const int nearGap = 1;

        if (near)
        {
            // So cheap a climb that even a step aside is not clearly less: there is no walking round left to test.
            if (ClimbExtra < 1.5f * GetDetourExtra(nearGap))
                Assert.Ignore($"at a climb cost of {PathfindingSystem.ClimbCost}, no way round a railing is clearly cheaper than climbing it");

            return FindWay(facing: 0, nearGap, climbs: true);
        }

        var farGap = GapStart;
        while (GetDetourExtra(farGap) < 2f * ClimbExtra)
        {
            farGap++;
        }

        // The gap is two tiles, and the row runs to the edge of the grid.
        return FindWay(facing: 0, farGap, climbs: true, half: Math.Max(Half, farGap + 1));
    }

    private async Task<string> FindWay(int facing, int? gapAt, bool climbs, int half = Half)
    {
        var (entManager, gridUid, npcUid) = await SetUp(facing, gapAt, climbs, "KsSquadTestMobSyndicate", half: half);

        System.Threading.Tasks.Task<PathResultEvent> pathTask = default!;
        await Pair.Server.WaitPost(() => pathTask = entManager.System<PathfindingSystem>().GetPath(npcUid,
            entManager.GetComponent<TransformComponent>(npcUid).Coordinates,
            new EntityCoordinates(gridUid, Place(facing, 0, Across) + new Vector2(0.5f, 0.5f)),
            0.5f,
            default,
            entManager.System<PathfindingSystem>().GetFlags(npcUid)));

        for (var i = 0; i < 120 && !pathTask.IsCompleted; i++)
        {
            await Pair.RunTicksSync(1);
        }

        var path = await pathTask;
        if (path.Result != PathResult.Path)
            return "none";

        var points = new List<Vector2>();
        foreach (var poly in path.Path)
        {
            points.Add(poly.Box.Center);
        }

        return CrossesRow(facing, points, gapAt) ? "over" : "round";
    }

    /// <summary>
    ///     Steering keeps to the path: an NPC walking to a goal on the far side of a row of railings with a gap at the end
    ///         gets there through the gap, never pressing up against the railings on the way.
    /// </summary>
    /// <remarks>
    ///     Close by - the goal just over the railing, nearer than steering would ever ask for a path to - it still goes
    ///         round: walking straight at it would only press it up against the railing.
    /// </remarks>
    [TestCase(0, false)]
    [TestCase(90, false)]
    [TestCase(0, true)]
    public async Task TestSteeringGoesRoundRailings(int facing, bool close)
    {
        var (entManager, gridUid, npcUid) = await SetUp(facing, GapStart, climbs: false, "KsTileDangerTestWalker", startAcross: close ? -1 : -3);
        var goal = close
            ? Place(facing, 0, 0) + (facing % 180 == 0 ? new Vector2(0.5f, 0.75f) : new Vector2(0.75f, 0.5f))
            : Place(facing, 0, 3) + new Vector2(0.5f, 0.5f);

        await Pair.Server.WaitPost(() =>
        {
            entManager.System<NPCSteeringSystem>().Register(npcUid, new EntityCoordinates(gridUid, goal));
            entManager.System<NPCSystem>().WakeNPC(npcUid, entManager.GetComponent<HTNComponent>(npcUid));
        });

        var positions = new List<Vector2>();
        var position = Vector2.Zero;

        for (var i = 0; i < 600 && Vector2.Distance(position, goal) > 0.8f; i += 4)
        {
            await Pair.RunTicksSync(4);
            await Pair.Server.WaitPost(() => position = entManager.GetComponent<TransformComponent>(npcUid).LocalPosition);
            positions.Add(position);
        }

        Assert.That(Vector2.Distance(position, goal), Is.LessThan(0.8f),
            $"it should get there, through the gap; it got to {position}, by {string.Join(" ", positions)}");
        Assert.That(CrossesRow(facing, positions, GapStart), Is.False, "never over the railings");
    }

    /// <summary>
    ///     With no way round, a climber gets over the railings: steering climbs them where the path does.
    /// </summary>
    [Test]
    public async Task TestSteeringClimbsRailings()
    {
        var (entManager, gridUid, npcUid) = await SetUp(facing: 0, gapAt: null, climbs: true, "KsRailingTestClimber");
        var goal = Place(0, 0, 3) + new Vector2(0.5f, 0.5f);

        await Pair.Server.WaitPost(() =>
        {
            var steeringComponent = entManager.System<NPCSteeringSystem>().Register(npcUid, new EntityCoordinates(gridUid, goal));
            steeringComponent.Flags = entManager.System<PathfindingSystem>().GetFlags(npcUid);
            entManager.System<NPCSystem>().WakeNPC(npcUid, entManager.GetComponent<HTNComponent>(npcUid));
        });

        var position = Vector2.Zero;

        for (var i = 0; i < 900 && Vector2.Distance(position, goal) > 0.8f; i += 4)
        {
            await Pair.RunTicksSync(4);
            await Pair.Server.WaitPost(() => position = entManager.GetComponent<TransformComponent>(npcUid).LocalPosition);
        }

        Assert.That(Vector2.Distance(position, goal), Is.LessThan(0.8f), $"it should climb over and get there; it got to {position}");
    }

    /// <summary>
    ///     A goal that is itself anchored is not in its own way: walking up to a wall, a door or a machine from close by is
    ///         still a straight line, not a path asked for at every step. The same spot given as a place on the grid is
    ///         behind that wall, and is not.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestAnAnchoredGoalIsNotInItsOwnWay(bool goalIsTheWall)
    {
        var entManager = Pair.Server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = Pair.Server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default, npcUid = default;
        NPCSteeringComponent steeringComponent = default!;

        await Pair.Server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-2, -2), new Vector2i(2, 2)).Owner;
            var wallUid = SpawnAt(entManager, "WallSolid", gridUid, 0, 0);
            npcUid = SpawnAt(entManager, "KsTileDangerTestWalker", gridUid, 0, -1);
            Assert.That(entManager.GetComponent<TransformComponent>(wallUid).Anchored);

            // A tile away: well inside the range steering walks straight at a goal from.
            var goal = goalIsTheWall
                ? new EntityCoordinates(wallUid, Vector2.Zero)
                : new EntityCoordinates(gridUid, new Vector2(0.5f, 0.5f));

            steeringComponent = entManager.System<NPCSteeringSystem>().Register(npcUid, goal);
            Assert.That(steeringComponent.RepathRange, Is.GreaterThan(1f), "the test's goal has to be near enough to walk straight at");
            entManager.System<NPCSystem>().WakeNPC(npcUid, entManager.GetComponent<HTNComponent>(npcUid));
        });

        await WaitForNavmeshSettled(Pair, gridUid);
        await Pair.RunTicksSync(5);

        await Pair.Server.WaitAssertion(() =>
        {
            // Trusted for a while once found clear, and never set when not.
            Assert.That(steeringComponent.KsBeelineClearUntil > TimeSpan.Zero, Is.EqualTo(goalIsTheWall),
                goalIsTheWall ? "the wall it is walking up to is not in the way of itself" : "a spot behind a wall is not a clear line");
        });
    }

    private async Task<(IEntityManager EntManager, EntityUid GridUid, EntityUid NpcUid)> SetUp(int facing, int? gapAt, bool climbs, string mob, int startAcross = -Across, int half = Half)
    {
        var entManager = Pair.Server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = Pair.Server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default, npcUid = default;

        await Pair.Server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-half, -half), new Vector2i(half, half)).Owner;
            var transformSystem = entManager.System<SharedTransformSystem>();

            for (var along = -half; along <= half; along++)
            {
                if (along >= gapAt && along <= gapAt + 1)
                    continue;

                var tile = Place(facing, along, 0);
                var railingUid = SpawnAt(entManager, "Railing", gridUid, (int) tile.X, (int) tile.Y);
                transformSystem.SetLocalRotation(railingUid, Angle.FromDegrees(facing));
            }

            var start = Place(facing, 0, startAcross);
            npcUid = SpawnAt(entManager, mob, gridUid, (int) start.X, (int) start.Y);
            entManager.GetComponent<HTNComponent>(npcUid).Blackboard.SetValue(NPCBlackboard.NavClimb, climbs);
        });

        await WaitForNavmeshSettled(Pair, gridUid);
        return (entManager, gridUid, npcUid);
    }

    /// <summary>
    ///     The tile <paramref name="along"/> the row and <paramref name="across"/> it, for railings facing
    ///         <paramref name="facing"/>: the row runs east-west for those facing north or south, north-south otherwise.
    /// </summary>
    private static Vector2 Place(int facing, int along, int across)
    {
        return facing % 180 == 0 ? new Vector2(along, across) : new Vector2(across, along);
    }

    /// <summary>
    ///     Whether <paramref name="points"/> cross the railings' edge anywhere short of the gap. A railing facing 0 degrees
    ///         stands on the south edge of its tile; turned anticlockwise, on the east edge at 90, north at 180, west at 270.
    /// </summary>
    private static bool CrossesRow(int facing, List<Vector2> points, int? gapAt)
    {
        var edge = facing is 90 or 180 ? 1f : 0f;

        for (var i = 1; i < points.Count; i++)
        {
            var (alongA, acrossA) = Split(facing, points[i - 1]);
            var (alongB, acrossB) = Split(facing, points[i]);

            if ((acrossA - edge) * (acrossB - edge) >= 0f)
                continue;

            var along = (alongA + alongB) / 2f;
            if (gapAt is not { } gap || along < gap - 0.25f || along > gap + 2.25f)
                return true;
        }

        return false;
    }

    private static (float Along, float Across) Split(int facing, Vector2 point)
    {
        return facing % 180 == 0 ? (point.X, point.Y) : (point.Y, point.X);
    }
}
