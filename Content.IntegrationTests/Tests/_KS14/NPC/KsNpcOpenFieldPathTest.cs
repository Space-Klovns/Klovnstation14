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
///     Long paths across open ground, as on the Field Commander map. A search steered by the coarse maps walks about
///         one poly a tile down its path, however far the way round an obstacle goes, and a path longer than the usual
///         node limit is still found.
/// </summary>
public sealed class KsNpcOpenFieldPathTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    /// <summary>
    ///     A long wall across the field with a way round at one end: the straight line to the goal runs into it. Every
    ///         poly on the near side that can see straight to the start costs, by the coarse maps' least estimate, about
    ///         what the best walk does, so a search that looks at all of those expands the field (3418 polys for a 100
    ///         poly path here, 5787 for a 98 poly one on the real map). Steered properly it expands about the polys of
    ///         its path.
    /// </summary>
    [Test]
    public async Task TestDetourAroundWallExpandsAboutThePath()
    {
        const int size = 99;
        var walls = new HashSet<Vector2i>();

        for (var y = 0; y <= 79; y++)
            walls.Add(new Vector2i(size / 2, y));

        // Either side of the wall, close to it: the whole field is open to the start, and none of it nearer the goal
        //      than the way round.
        var start = new Vector2i(40, 40);
        var goal = new Vector2i(59, 40);

        var gridUid = await MakeLayout(new Vector2i(0, 0), new Vector2i(size, size), walls);
        await OverrideCVar(Side.Server, KsCCVars.NpcPathNodeLimit, 1_000_000);
        var result = await FindPath(gridUid, start, goal);

        Assert.Multiple(() =>
        {
            Assert.That(result.Result, Is.EqualTo(PathResult.Path));
            Assert.That(result.Path.Count, Is.GreaterThan(90), "the way round should be long");
            Assert.That(result.Expansions, Is.LessThan(result.Path.Count * 2), "it should expand about the polys of its path");
        });
    }

    /// <summary>
    ///     A way of more polys than the node limit, down a strip. The search walks it a poly a tile and needs a poly
    ///         expanded for each, so the limit has to grow with the path.
    /// </summary>
    [Test]
    public async Task TestPathLongerThanTheNodeLimitIsFound()
    {
        // A strip a good deal longer than the limit, whatever the limit is set to.
        var nodeLimit = Server.ResolveDependency<Robust.Shared.Configuration.IConfigurationManager>().GetCVar(KsCCVars.NpcPathNodeLimit);
        var end = nodeLimit + nodeLimit / 3;
        var gridUid = await MakeLayout(new Vector2i(0, 0), new Vector2i(end, 2), []);
        var result = await FindPath(gridUid, new Vector2i(0, 1), new Vector2i(end, 1));

        Assert.Multiple(() =>
        {
            Assert.That(result.Path.Count, Is.GreaterThan(nodeLimit), "the path should be longer than the node limit");
            Assert.That(result.Result, Is.EqualTo(PathResult.Path));
        });
    }

    private async Task<EntityUid> MakeLayout(Vector2i min, Vector2i max, HashSet<Vector2i> walls)
    {
        var tileDefinitionManager = Server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default;

        await Server.WaitPost(() =>
        {
            gridUid = MakeGrid(SEntMan, tileDefinitionManager, map.MapId, map.Grid, min, max).Owner;

            foreach (var wall in walls)
            {
                SpawnAt(SEntMan, TestWall, gridUid, wall.X, wall.Y);
            }
        });

        await Pair.RunTicksSync(90); // navmesh
        return gridUid;
    }

    private async Task<(PathResult Result, List<PathPoly> Path, int Expansions)> FindPath(EntityUid gridUid, Vector2i from, Vector2i to)
    {
        var pathfindingSystem = SEntMan.System<PathfindingSystem>();
        AStarPathRequest request = null!;
        System.Threading.Tasks.Task<PathResultEvent> task = null!;

        await Server.WaitPost(() =>
        {
            request = new AStarPathRequest(new EntityCoordinates(gridUid, new Vector2(from.X + 0.5f, from.Y + 0.5f)),
                new EntityCoordinates(gridUid, new Vector2(to.X + 0.5f, to.Y + 0.5f)),
                PathFlags.None,
                0f,
                PathfindingSystem.PathfindingCollisionLayer,
                PathfindingSystem.PathfindingCollisionMask,
                CancellationToken.None);
            task = pathfindingSystem.GetPath(request);
        });

        for (var i = 0; i < 2000 && !task.IsCompleted; i++)
        {
            await Pair.RunTicksSync(1);
        }

        Assert.That(task.IsCompletedSuccessfully, "the path request should have finished");
        var pathEvent = await task;
        return (pathEvent.Result, pathEvent.Path, request.KsExpansions);
    }
}
