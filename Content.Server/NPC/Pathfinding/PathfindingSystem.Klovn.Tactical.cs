// KS14: added in this fork
using System.Threading;
using System.Threading.Tasks;
using Content.Server._KS14.NPC.Pathfinding;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Utility;

namespace Content.Server.NPC.Pathfinding;

public sealed partial class PathfindingSystem
{
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
        _pathRequests.Add(request);

        await request.Task;

        if (!request.Task.IsCompletedSuccessfully)
            return new List<PathPoly>();

        // Same context as MoveToOperator/PickAccessibleOperator's awaits and not synchronously blocking.
#pragma warning disable RA0004
        var result = request.Task.Result;
#pragma warning restore RA0004

        if (result != PathResult.Path)
            return new List<PathPoly>();

        return request.Candidates;
    }

    private PathResult UpdateTacticalPath(TacticalPathRequest request)
    {
        if (request.Task.IsCanceled)
        {
            return PathResult.NoPath;
        }

        PathPoly? currentNode;

        if (!request.Started)
        {
            request.Frontier = new PriorityQueue<(float, PathPoly)>(PathPolyComparer);
            request.Started = true;
        }
        else
        {
            if (request.Frontier.Count == 0)
            {
                return PathResult.NoPath;
            }

            (_, currentNode) = request.Frontier.Peek();

            if (!currentNode.IsValid())
            {
                return PathResult.NoPath;
            }
        }

        DebugTools.Assert(!request.Task.IsCompleted);
        request.Stopwatch.Restart();

        var startNode = GetPoly(request.Start);

        if (startNode == null)
        {
            return PathResult.NoPath;
        }

        request.Frontier.Add((0.0f, startNode));
        request.CostSoFar[startNode] = 0.0f;
        request.DistanceSoFar[startNode] = 0.0f;
        var count = 0;

        // Gated by NodeLimit alone, not MaxCandidates - capping expansion by the requested candidate count
        // would let a single large room exhaust the budget on its own floor tiles before the frontier ever
        // dequeues (and expands past) a farther doorway, making anything beyond it unreachable even though
        // it's well within ExpansionRange. MaxCandidates instead truncates the materialized list below.
        while (request.Frontier.Count > 0 && count < NodeLimit)
        {
            if (count % 20 == 0 && count > 0 && request.Stopwatch.Elapsed > PathTime)
            {
                return PathResult.Continuing;
            }

            count++;

            (_, currentNode) = request.Frontier.Take();

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
                request.Frontier.Add((gScore, neighbor));
            }
        }

        if (request.CostSoFar.Count == 0)
        {
            return PathResult.NoPath;
        }

        request.Candidates.Clear();

        foreach (var (poly, _) in request.CostSoFar)
        {
            if (!poly.IsValid())
                continue;

            request.Candidates.Add(poly);

            if (request.Candidates.Count >= request.MaxCandidates)
                break;
        }

        return PathResult.Path;
    }
}
