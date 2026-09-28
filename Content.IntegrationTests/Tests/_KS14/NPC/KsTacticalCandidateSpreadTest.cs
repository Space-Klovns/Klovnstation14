#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server.NPC.Pathfinding;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     Tactical position candidates cover the whole area the flood reached, not just its nearest few tiles -
///         otherwise no retreat can get further than a few tiles, however much it prefers distance - while still
///         offering every tile close by, where cover and flanking positions usually are.
/// </summary>
public sealed class KsTacticalCandidateSpreadTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    /// <summary>
    ///     Tiles within this many tiles of the origin, squared - every one of them should be a candidate. Corners two
    ///         tiles out diagonally are left out: the flood reaches some tiles three out before them.
    /// </summary>
    private const int NearbyTileRangeSquared = 5;

    [Test]
    public async Task TestCandidatesReachFar()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var pathfindingSystem = entManager.System<PathfindingSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();
        var map = await Pair.CreateTestMap();

        EntityUid gridUid = default;
        EntityUid mobUid = default;

        await server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-15, -15), new Vector2i(15, 15)).Owner;
            mobUid = SpawnAt(entManager, SyndicateMob, gridUid, 0, 0);
        });

        // Let the navmesh build.
        await Pair.RunTicksSync(90);

        Task<List<PathPoly>> candidatesTask = default!;
        var origin = new EntityCoordinates(gridUid, new Vector2(0.5f, 0.5f));

        await server.WaitPost(() =>
            candidatesTask = pathfindingSystem.GetTacticalCandidates(mobUid, origin, 15f, 64, default));

        for (var i = 0; i < 60 && !candidatesTask.IsCompleted; i++)
        {
            await Pair.RunTicksSync(1);
        }

        Assert.That(candidatesTask.IsCompletedSuccessfully, "the candidate flood never finished");

        var candidates = await candidatesTask;
        var farthest = 0f;
        var nearbyTiles = new HashSet<Vector2i>();

        await server.WaitPost(() =>
        {
            var originPosition = transformSystem.ToMapCoordinates(origin).Position;
            farthest = candidates.Max(poly => Vector2.Distance(transformSystem.ToMapCoordinates(poly.Coordinates).Position, originPosition));

            foreach (var poly in candidates)
            {
                var tile = (Vector2i)poly.Coordinates.Position.Floored();
                if (tile.X * tile.X + tile.Y * tile.Y <= NearbyTileRangeSquared)
                    nearbyTiles.Add(tile);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(candidates, Has.Count.GreaterThan(32), "there should be plenty of candidates on open floor");
            Assert.That(farthest, Is.GreaterThanOrEqualTo(8f),
                $"the farthest candidate is only {farthest:F1} tiles away; candidates are bunched around the origin");
            Assert.That(nearbyTiles, Has.Count.EqualTo(21),
                "every tile right around the origin should be a candidate, not a sample of them");
        });
    }

    /// <summary>
    ///     Search state is pooled and reused from one request to the next, so nothing of an earlier search may leak
    ///         into a later one: the same search asked again, after one somewhere else and alongside several at
    ///         once, finds exactly the same candidates.
    /// </summary>
    [Test]
    public async Task TestPooledSearchStateDoesNotLeak()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var pathfindingSystem = entManager.System<PathfindingSystem>();
        var map = await Pair.CreateTestMap();

        EntityUid gridUid = default;
        EntityUid mobUid = default;

        await server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-30, -15), new Vector2i(30, 15)).Owner;
            mobUid = SpawnAt(entManager, SyndicateMob, gridUid, 0, 0);
        });

        await Pair.RunTicksSync(90);

        var here = new EntityCoordinates(gridUid, new Vector2(-15.5f, 0.5f));
        var elsewhere = new EntityCoordinates(gridUid, new Vector2(15.5f, 0.5f));

        var first = await Search(here);
        await Search(elsewhere);
        var again = await Search(here);
        var together = await SearchAll(here, elsewhere, here, elsewhere);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.Empty);
            Assert.That(again, Is.EqualTo(first), "a search after another one somewhere else should find the same candidates");
            Assert.That(together[0], Is.EqualTo(first), "a search run alongside others should find the same candidates");
            Assert.That(together[2], Is.EqualTo(first), "a search run alongside others should find the same candidates");
            Assert.That(together[1], Is.EqualTo(together[3]));
        });

        async Task<List<Vector2>> Search(EntityCoordinates origin)
        {
            return (await SearchAll(origin))[0];
        }

        async Task<List<Vector2>[]> SearchAll(params EntityCoordinates[] origins)
        {
            var tasks = new Task<List<PathPoly>>[origins.Length];

            await server.WaitPost(() =>
            {
                for (var i = 0; i < origins.Length; i++)
                {
                    tasks[i] = pathfindingSystem.GetTacticalCandidates(mobUid, origins[i], 15f, 64, default);
                }
            });

            for (var i = 0; i < 60 && !tasks.All(task => task.IsCompleted); i++)
            {
                await Pair.RunTicksSync(1);
            }

            Assert.That(tasks.All(task => task.IsCompletedSuccessfully), "a candidate flood never finished");
            var results = await Task.WhenAll(tasks);
            return results.Select(candidates => candidates.Select(poly => poly.Coordinates.Position).ToList()).ToArray();
        }
    }
}
