#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.NPC.Pathfinding;
using Content.Shared._KS14.CCVar;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     Hierarchical pathfinding (<c>PathfindingSystem.Klovn.Hierarchy.cs</c>): the coarse maps steer A* down long paths
///         it could not find on straight-line distance within its node limit, find the same paths an unlimited search
///         does, give up on goals that cannot be reached without searching, and keep up with the navmesh changing.
/// </summary>
public sealed class KsNpcHierarchicalPathTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string Wall = "WallSolid";

    /// <summary>
    ///     A winding corridor, back and forth across the grid, a few hundred tiles long: on straight-line distance A*
    ///         searches most of the grid before finding its way, and runs out of nodes. Steered by the coarse maps, it
    ///         walks straight down it.
    /// </summary>
    [TestCase(true, true)]
    [TestCase(false, false)]
    public async Task TestLongWindingPathIsFound(bool hierarchical, bool expectPath)
    {
        var gridUid = await MakeLayout(new Vector2i(0, 0), new Vector2i(39, 29), (x, y) =>
            // Every third row a wall, its gap at alternate ends.
            y % 3 == 2 && x != (y / 3 % 2 == 0 ? 39 : 0));

        await OverrideCVar(Side.Server, KsCCVars.NpcPathHierarchical, hierarchical);
        var result = await FindPaths(gridUid, [(new Vector2i(0, 0), new Vector2i(0, 29))]);

        Assert.That(result[0].Result, Is.EqualTo(expectPath ? PathResult.Path : PathResult.NoPath));
    }

    /// <summary>
    ///     Rooms with doorways - some walled up - and rubble: between random spots, steered searches find the same paths,
    ///         and the same spots unreachable, as an unsteered search with no node limit. And they do it expanding far
    ///         fewer polys.
    /// </summary>
    [Test]
    public async Task TestPathsMatchUnlimitedSearch()
    {
        var random = new System.Random(1234);
        var walls = new HashSet<Vector2i>();
        const int size = 47;

        for (var x = 0; x <= size; x++)
        {
            for (var y = 0; y <= size; y++)
            {
                if (x % 8 == 7 || y % 8 == 7 || random.NextDouble() < 0.08)
                    walls.Add(new Vector2i(x, y));
            }
        }

        // Doorways: most room walls get one, some none.
        for (var room = 7; room < size; room += 8)
        {
            for (var start = 0; start < size; start += 8)
            {
                if (random.NextDouble() < 0.6)
                    walls.Remove(new Vector2i(room, start + random.Next(7)));

                if (random.NextDouble() < 0.6)
                    walls.Remove(new Vector2i(start + random.Next(7), room));
            }
        }

        var gridUid = await MakeLayout(new Vector2i(0, 0), new Vector2i(size, size), (x, y) => walls.Contains(new Vector2i(x, y)));

        var floor = new List<Vector2i>();
        for (var x = 0; x <= size; x++)
        {
            for (var y = 0; y <= size; y++)
            {
                if (!walls.Contains(new Vector2i(x, y)))
                    floor.Add(new Vector2i(x, y));
            }
        }

        var pairs = new List<(Vector2i, Vector2i)>();
        for (var i = 0; i < 40; i++)
        {
            pairs.Add((floor[random.Next(floor.Count)], floor[random.Next(floor.Count)]));
        }

        await OverrideCVar(Side.Server, KsCCVars.NpcPathHierarchical, true, sync: false);
        var steered = await FindPaths(gridUid, pairs);

        await OverrideCVar(Side.Server, KsCCVars.NpcPathHierarchical, false, sync: false);
        await OverrideCVar(Side.Server, KsCCVars.NpcPathNodeLimit, 1_000_000, sync: false);
        var unlimited = await FindPaths(gridUid, pairs);

        var pathfindingSystem = SEntMan.System<PathfindingSystem>();
        Assert.Multiple(() =>
        {
            for (var i = 0; i < pairs.Count; i++)
            {
                Assert.That(steered[i].Result, Is.EqualTo(unlimited[i].Result), $"pair {i}, {pairs[i]}: whether there is a path");

                if (unlimited[i].Result != PathResult.Path)
                    continue;

                Assert.That(GetCost(pathfindingSystem, steered[i].Path), Is.EqualTo(GetCost(pathfindingSystem, unlimited[i].Path)).Within(1).Percent,
                    $"pair {i}, {pairs[i]}: the path's cost");
            }

            // Where there is a path: a goal that cannot be reached is given up on without searching, which would hide it.
            var connected = Enumerable.Range(0, pairs.Count).Where(i => unlimited[i].Result == PathResult.Path).ToList();
            Assert.That(connected.Sum(i => steered[i].Expansions), Is.LessThan(connected.Sum(i => unlimited[i].Expansions) / 3),
                "steered searches should expand far fewer polys");
            Assert.That(unlimited.Count(path => path.Result == PathResult.Path), Is.InRange(10, pairs.Count - 3),
                "the layout should have some pairs that connect and some that do not");
        });
    }

    /// <summary>
    ///     A goal walled in, or a start: no path, found out without expanding a single poly - by the quick flood from the
    ///         start, or, with that given no regions to flood, by the search from the goal. Unsteered, the search floods
    ///         everything it can reach first.
    /// </summary>
    [TestCase(true, true, 256)]
    [TestCase(true, false, 256)]
    [TestCase(true, true, 0)]
    [TestCase(true, false, 0)]
    [TestCase(false, true, 256)]
    public async Task TestUnreachableIsGivenUpWithoutSearching(bool hierarchical, bool goalWalledIn, int reachRegionBudget)
    {
        var gridUid = await MakeWalledInLayout();

        await OverrideCVar(Side.Server, KsCCVars.NpcPathHierarchical, hierarchical, sync: false);
        await OverrideCVar(Side.Server, KsCCVars.NpcPathNodeLimit, 1_000_000);

        var pathfindingSystem = SEntMan.System<PathfindingSystem>();
        var defaultBudget = pathfindingSystem.ReachRegionBudget;
        List<(PathResult Result, List<PathPoly> Path, int Expansions)> result;

        try
        {
            pathfindingSystem.ReachRegionBudget = reachRegionBudget;
            result = await FindPaths(gridUid, [goalWalledIn ? (OpenTile, WalledInTile) : (WalledInTile, OpenTile)]);
        }
        finally
        {
            pathfindingSystem.ReachRegionBudget = defaultBudget;
        }

        Assert.Multiple(() =>
        {
            Assert.That(result[0].Result, Is.EqualTo(PathResult.NoPath));

            if (hierarchical)
                Assert.That(result[0].Expansions, Is.Zero, "it should have known before searching");
            else
                Assert.That(result[0].Expansions, Is.GreaterThan(500), "unsteered, it should have searched the grid");
        });
    }

    /// <summary>
    ///     A start walled in is found out from its own side: nothing is worked out for the open floor round the goal,
    ///         however much of it there is.
    /// </summary>
    [Test]
    public async Task TestShutInStartIsFoundOutFromItsSide()
    {
        var gridUid = await MakeWalledInLayout();
        var result = await FindPaths(gridUid, [(WalledInTile, OpenTile)]);

        Assert.Multiple(() =>
        {
            Assert.That(result[0].Result, Is.EqualTo(PathResult.NoPath));

            // Away from both the start's chunks and the goal's.
            var farChunk = SEntMan.GetComponent<GridPathfindingComponent>(gridUid).Chunks[new Vector2i(0, 2)];
            Assert.That(farChunk.KsAbstractions, Is.Null, "the open floor should not have been searched");
        });
    }

    private static readonly Vector2i WalledInTile = new(30, 10);
    private static readonly Vector2i OpenTile = new(2, 2);

    /// <summary>
    ///     Open floor, with <see cref="WalledInTile"/> walled in.
    /// </summary>
    private Task<EntityUid> MakeWalledInLayout()
    {
        return MakeLayout(new Vector2i(0, 0), new Vector2i(39, 23), (x, y) =>
            System.Math.Max(System.Math.Abs(x - WalledInTile.X), System.Math.Abs(y - WalledInTile.Y)) == 2);
    }

    /// <summary>
    ///     Walling up the only way through, and opening it again - the wall deleted, or moved off - each time, once the
    ///         navmesh is rebuilt, the coarse maps know. Walled up, there is no path, known without searching; opened,
    ///         there is one again. Unsteered, the navmesh itself has to notice the wall going: a deleted one used to
    ///         leave its tile blocked for good.
    /// </summary>
    [TestCase(true, true)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task TestCoarseMapsFollowTheNavmesh(bool hierarchical, bool deleted)
    {
        await OverrideCVar(Side.Server, KsCCVars.NpcPathHierarchical, hierarchical, sync: false);

        // Two halves, joined by one gap, in a chunk of its own.
        var gap = new Vector2i(12, 4);
        var gridUid = await MakeLayout(new Vector2i(0, 0), new Vector2i(23, 15), (x, y) => x == gap.X && y != gap.Y);
        var pair = (new Vector2i(2, 12), new Vector2i(21, 12));

        Assert.That((await FindPaths(gridUid, [pair]))[0].Result, Is.EqualTo(PathResult.Path), "open, there should be a path");

        EntityUid plugUid = default;
        await Server.WaitPost(() => plugUid = SpawnAt(SEntMan, Wall, gridUid, gap.X, gap.Y));
        await Pair.RunTicksSync(90);

        var walledUp = (await FindPaths(gridUid, [pair]))[0];
        Assert.Multiple(() =>
        {
            Assert.That(walledUp.Result, Is.EqualTo(PathResult.NoPath), "walled up, there should be none");

            if (hierarchical)
                Assert.That(walledUp.Expansions, Is.Zero, "and it should have known before searching");
        });

        await Server.WaitPost(() =>
        {
            if (deleted)
            {
                SEntMan.DeleteEntity(plugUid);
                return;
            }

            var transformSystem = SEntMan.System<SharedTransformSystem>();
            transformSystem.Unanchor(plugUid);
            transformSystem.SetCoordinates(plugUid, new EntityCoordinates(gridUid, new Vector2(gap.X + 0.5f, -20f)));
        });
        await Pair.RunTicksSync(90);

        Assert.That((await FindPaths(gridUid, [pair]))[0].Result, Is.EqualTo(PathResult.Path), "opened again, there should be a path again");
    }

    /// <summary>
    ///     A grid over the tiles from <paramref name="min"/> to <paramref name="max"/>, with a wall wherever
    ///         <paramref name="isWall"/> says, and its navmesh built.
    /// </summary>
    private async Task<EntityUid> MakeLayout(Vector2i min, Vector2i max, System.Func<int, int, bool> isWall)
    {
        var tileDefinitionManager = Server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default;

        await Server.WaitPost(() =>
        {
            gridUid = MakeGrid(SEntMan, tileDefinitionManager, map.MapId, map.Grid, min, max).Owner;

            for (var x = min.X; x <= max.X; x++)
            {
                for (var y = min.Y; y <= max.Y; y++)
                {
                    if (isWall(x, y))
                        SpawnAt(SEntMan, Wall, gridUid, x, y);
                }
            }
        });

        await Pair.RunTicksSync(90); // navmesh
        return gridUid;
    }

    /// <summary>
    ///     A path for a mob between each pair of tiles, asked for all at once.
    /// </summary>
    private async Task<List<(PathResult Result, List<PathPoly> Path, int Expansions)>> FindPaths(EntityUid gridUid, List<(Vector2i From, Vector2i To)> pairs)
    {
        var pathfindingSystem = SEntMan.System<PathfindingSystem>();
        var requests = new List<AStarPathRequest>();
        var tasks = new List<System.Threading.Tasks.Task<PathResultEvent>>();

        await Server.WaitPost(() =>
        {
            foreach (var (from, to) in pairs)
            {
                var request = new AStarPathRequest(new EntityCoordinates(gridUid, new Vector2(from.X + 0.5f, from.Y + 0.5f)),
                    new EntityCoordinates(gridUid, new Vector2(to.X + 0.5f, to.Y + 0.5f)),
                    PathFlags.None,
                    0f,
                    PathfindingSystem.PathfindingCollisionLayer,
                    PathfindingSystem.PathfindingCollisionMask,
                    CancellationToken.None);

                requests.Add(request);
                tasks.Add(pathfindingSystem.GetPath(request));
            }
        });

        for (var i = 0; i < 600 && tasks.Any(task => !task.IsCompleted); i++)
        {
            await Pair.RunTicksSync(1);
        }

        Assert.That(tasks.All(task => task.IsCompletedSuccessfully), "every path request should have finished");

        var results = new List<(PathResult, List<PathPoly>, int)>();
        for (var i = 0; i < tasks.Count; i++)
        {
            var pathEvent = await tasks[i];
            results.Add((pathEvent.Result, pathEvent.Path, requests[i].KsExpansions));
        }

        return results;
    }

    private static float GetCost(PathfindingSystem pathfindingSystem, List<PathPoly> path)
    {
        var cost = 0f;
        for (var i = 1; i < path.Count; i++)
        {
            cost += PathfindingSystem.GetTileModifier(PathFlags.None,
                    PathfindingSystem.PathfindingCollisionLayer,
                    PathfindingSystem.PathfindingCollisionMask,
                    path[i]) *
                pathfindingSystem.OctileDistance(path[i], path[i - 1]);
        }

        return cost;
    }
}
