// KS14: added in this fork
using System.Threading;
using System.Threading.Tasks;
using Content.Server._KS14.NPC.Pathfinding;
using Microsoft.Extensions.ObjectPool;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Utility;

namespace Content.Server.NPC.Pathfinding;

public sealed partial class PathfindingSystem
{
    /// <summary>
    /// How many of each piece of tactical search state the pools keep. Enough for the requests usually in flight at
    /// once; past that, requests allocate their own and the extras are simply dropped when they finish.
    /// </summary>
    private const int TacticalPoolSize = 32;

    // Every tactical request grows these to several hundred entries, and an NPC holding a position makes one on
    //      every replan. Pooled, they are allocated once and reused; they are thread-safe, and requests run in parallel.
    private readonly ObjectPool<Dictionary<PathPoly, float>> _tacticalCostPool =
        new DefaultObjectPool<Dictionary<PathPoly, float>>(new DictPolicy<PathPoly, float>(), TacticalPoolSize);

    private readonly ObjectPool<Dictionary<PathPoly, float>> _tacticalDistancePool =
        new DefaultObjectPool<Dictionary<PathPoly, float>>(new DictPolicy<PathPoly, float>(), TacticalPoolSize);

    private readonly ObjectPool<TacticalFrontier> _tacticalFrontierPool =
        new DefaultObjectPool<TacticalFrontier>(new TacticalFrontierPolicy(), TacticalPoolSize);

    private readonly ObjectPool<List<PathPoly>> _tacticalTilePolyPool =
        new DefaultObjectPool<List<PathPoly>>(new ListPolicy<PathPoly>(), TacticalPoolSize);

    private readonly ObjectPool<HashSet<(EntityUid, Vector2i, byte)>> _tacticalSeenTilePool =
        new DefaultObjectPool<HashSet<(EntityUid, Vector2i, byte)>>(new SetPolicy<(EntityUid, Vector2i, byte)>(), TacticalPoolSize);

    /// <summary>
    /// Flood-fills the poly graph from <paramref name="reference"/> out to <paramref name="maxRange"/>,
    /// returning up to <paramref name="maxCandidates"/> reachable <see cref="PathPoly"/> nodes for tactical
    /// position scoring (camping/retreat/advance). Unlike <see cref="GetRandomPath"/>, this does not
    /// reconstruct a route - callers only need candidate endpoints.
    /// </summary>
    public async Task<List<PathPoly>> GetTacticalCandidates(
        EntityUid entity,
        EntityCoordinates reference,
        float maxRange,
        int maxCandidates,
        CancellationToken cancelToken,
        PathFlags flags = PathFlags.None)
    {
        var layer = 0;
        var mask = 0;

        if (TryComp<FixturesComponent>(entity, out var fixtures))
        {
            (layer, mask) = _physics.GetHardCollision(entity, fixtures);
        }

        var request = new TacticalPathRequest(reference, maxRange, maxCandidates, flags, layer, mask, cancelToken);
        RentTacticalSearchState(request);
        _pathRequests.Add(request);

        await request.Task;

        // Only filled in once the flood finds a path, so on any other result it is already the empty list that
        //      result needs - no new one.
        return request.Candidates;
    }

    /// <summary>
    /// Hands <paramref name="request"/> pooled search state: its cost and distance maps and its frontier. They are
    /// only needed until the flood finishes, so they go back in <see cref="ReturnTacticalSearchState"/>.
    /// </summary>
    internal void RentTacticalSearchState(TacticalPathRequest request)
    {
        request.CostSoFar = _tacticalCostPool.Get();
        request.DistanceSoFar = _tacticalDistancePool.Get();
        request.TacticalFrontier = _tacticalFrontierPool.Get();
    }

    /// <summary>
    /// Gives a finished request's search state back to the pools. Called once nothing reads it any more - after the
    /// breadcrumb debug has sent its costs - and leaves the request's references to it empty, so that anything
    /// touching it afterwards fails loudly rather than reading another request's search.
    /// </summary>
    private void ReturnTacticalSearchState(TacticalPathRequest request)
    {
        _tacticalCostPool.Return(request.CostSoFar);
        _tacticalDistancePool.Return(request.DistanceSoFar);
        _tacticalFrontierPool.Return(request.TacticalFrontier);

        request.CostSoFar = default!;
        request.DistanceSoFar = default!;
        request.TacticalFrontier = default!;
    }

    private PathResult UpdateTacticalPath(TacticalPathRequest request)
    {
        if (request.Task.IsCanceled)
        {
            return PathResult.NoPath;
        }

        PathPoly? currentNode;

        // Seeded once, on the first slice only. A flood too slow for one tick carries on where it left off on the
        //      next; seeding it again would re-expand from the start, and the same flood would find different
        //      candidates depending on how many ticks it happened to span.
        if (!request.Started)
        {
            request.Started = true;

            var startNode = GetPoly(request.Start);

            if (startNode == null)
            {
                return PathResult.NoPath;
            }

            request.TacticalFrontier.Add(0.0f, startNode);
            request.CostSoFar[startNode] = 0.0f;
            request.DistanceSoFar[startNode] = 0.0f;
        }
        else
        {
            if (request.TacticalFrontier.Count == 0)
            {
                return PathResult.NoPath;
            }

            currentNode = request.TacticalFrontier.Peek();

            if (!currentNode.IsValid())
            {
                return PathResult.NoPath;
            }
        }

        DebugTools.Assert(!request.Task.IsCompleted);
        request.Stopwatch.Restart();
        var sliceCount = 0;

        // Gated by NodeLimit alone, not MaxCandidates - capping expansion by the requested candidate count
        // would let a single large room exhaust the budget on its own floor tiles before the frontier ever
        // dequeues (and expands past) a farther doorway, making anything beyond it unreachable even though
        // it's well within ExpansionRange. MaxCandidates instead truncates the materialized list below.
        // NodeLimit is over the whole flood, not each slice of it, for the same reason the seed is only planted once.
        while (request.TacticalFrontier.Count > 0 && request.ExpandedCount < NodeLimit)
        {
            if (sliceCount % 20 == 0 && sliceCount > 0 && request.Stopwatch.Elapsed > PathTime)
            {
                return PathResult.Continuing;
            }

            sliceCount++;
            request.ExpandedCount++;

            currentNode = request.TacticalFrontier.Take();

            foreach (var neighbor in currentNode.Neighbors)
            {
                var tileCost = GetTileCost(request, currentNode, neighbor);

                if (tileCost.Equals(0f))
                {
                    continue;
                }

                // Raw spatial distance, independent of tileCost's door/smash/climb weighting - a door adds a
                // large additive modifier to tileCost (see GetTileCost) so that costlier routes are deprioritized
                // by the priority queue, but that same weighting must not be mistaken for physical distance, or
                // any candidate past a door (even one the NPC can freely open) would get cut off as if it were
                // far away.
                var distance = request.DistanceSoFar[currentNode] + OctileDistance(currentNode, neighbor);

                if (distance > request.ExpansionRange)
                {
                    continue;
                }

                var gScore = request.CostSoFar[currentNode] + tileCost;

                if (request.CostSoFar.TryGetValue(neighbor, out var nextValue) && gScore >= nextValue)
                {
                    continue;
                }

                request.CostSoFar[neighbor] = gScore;
                request.DistanceSoFar[neighbor] = distance;
                request.TacticalFrontier.Add(gScore, neighbor);
            }
        }

        if (request.CostSoFar.Count == 0)
        {
            return PathResult.NoPath;
        }

        request.Candidates.Clear();

        // One candidate per tile: a tile's polys are near-duplicates as positions, and would crowd the list.
        //      CostSoFar only ever has entries added or updated, never removed, so it enumerates in the order the
        //      flood reached each poly - nearest first.
        var tilePolys = _tacticalTilePolyPool.Get();
        var seenTiles = _tacticalSeenTilePool.Get();

        foreach (var (poly, _) in request.CostSoFar)
        {
            if (poly.IsValid() && seenTiles.Add((poly.GraphUid, poly.ChunkOrigin, poly.TileIndex)))
                tilePolys.Add(poly);
        }

        // Half the cap goes to the nearest tiles, every one of them, and the other half is spread evenly over the
        //      rest of the flood. Taking only the nearest would never offer anything past a few tiles, so no retreat
        //      could get further than that however much its scoring preferred distance; spreading all of it would
        //      skip most of the nearby tiles, which is where cover and flanking positions usually are.
        var nearestCount = Math.Min(tilePolys.Count, (request.MaxCandidates + 1) / 2);

        for (var i = 0; i < nearestCount; i++)
        {
            request.Candidates.Add(tilePolys[i]);
        }

        var remainingCount = tilePolys.Count - nearestCount;
        var spreadCount = request.MaxCandidates - nearestCount;

        if (remainingCount > 0 && spreadCount > 0)
        {
            var stride = MathF.Max(1f, remainingCount / (float)spreadCount);

            for (var i = 0f; i < remainingCount && request.Candidates.Count < request.MaxCandidates; i += stride)
            {
                request.Candidates.Add(tilePolys[nearestCount + (int)i]);
            }
        }

        _tacticalTilePolyPool.Return(tilePolys);
        _tacticalSeenTilePool.Return(seenTiles);

        return PathResult.Path;
    }

    private sealed class TacticalFrontierPolicy : PooledObjectPolicy<TacticalFrontier>
    {
        public override TacticalFrontier Create()
        {
            return new TacticalFrontier();
        }

        public override bool Return(TacticalFrontier frontier)
        {
            frontier.Clear();
            return true;
        }
    }
}
