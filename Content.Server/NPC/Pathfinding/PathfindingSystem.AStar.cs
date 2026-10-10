using System.Runtime.InteropServices; // KS14
using Content.Server._KS14.NPC.Pathfinding; // KS14
using Content.Shared.NPC;
using Robust.Shared.Map;
using Robust.Shared.Utility;

namespace Content.Server.NPC.Pathfinding;

public sealed partial class PathfindingSystem
{
    private PathResult UpdateAStarPath(AStarPathRequest request)
    {
        if (request.Start.Equals(request.End))
        {
            return PathResult.Path;
        }

        if (request.Task.IsCanceled)
        {
            return PathResult.NoPath;
        }

        // TODO: Need partial planning that uses best node.
        PathPoly? currentNode = null;
        var firstSlice = !request.Started; // KS14

        // First run
        if (!request.Started)
        {
            /* request.Frontier = new PriorityQueue<(float, PathPoly)>(PathPolyComparer); */ // KS14: pooled frontier, rented when queued
            request.PolyFrontier ??= new PathPolyFrontier(); // KS14
            request.Started = true;
        }
        // Re-validate nodes
        else
        {
            // Theoretically this shouldn't be happening, but practically...
            if (request.PolyFrontier! /* KS14: Frontier -> PolyFrontier */.Count == 0)
            {
                return PathResult.NoPath;
            }

            currentNode = request.PolyFrontier.Peek(); // KS14: (_, currentNode) = request.Frontier.Peek() -> PolyFrontier

            if (!currentNode.IsValid())
            {
                return PathResult.NoPath;
            }

            // Re-validate parents too.
            if (request.CameFrom.TryGetValue(currentNode, out var parentNode) && !parentNode.IsValid())
            {
                return PathResult.NoPath;
            }
        }

        DebugTools.Assert(!request.Task.IsCompleted);
        request.Stopwatch.Restart();

        var startNode = GetPoly(request.Start);
        var endNode = GetPoly(request.End);

        if (startNode == null || endNode == null)
        {
            return PathResult.NoPath;
        }

        currentNode = startNode;

        // KS14 start: seeded on the first slice only. A search too slow for one tick carries on where it left off;
        //      seeding it again re-expanded everything from the start on every slice
        if (firstSlice)
        {
            request.PolyFrontier.Add(0.0f, startNode);
            request.CostSoFar[startNode] = 0.0f;
        }
        // KS14 end
        /* request.Frontier.Add((0.0f, startNode)); */ // KS14: moved above
        /* request.CostSoFar[startNode] = 0.0f; */ // KS14: moved above
        var count = 0;
        var arrived = false;

        // KS14 start: where the end is, in the end poly's grid, worked out once a slice rather than resolving both
        //      sets of coordinates through their transforms for every node expanded
        var endLocalPosition = request.End.EntityId == endNode.GraphUid
            ? request.End.Position
            : _transform.WithEntityId(request.End, endNode.GraphUid).Position;
        var arrivalDistanceSquared = request.Distance * request.Distance;

        // The coarse maps' estimate, worked out before searching, over as many slices as it takes. A goal they show
        //      cannot be reached is given up on now, rather than after searching to the node limit. See
        //      PathfindingSystem.Klovn.Hierarchy.cs.
        if (UpdateHeuristic(request, startNode, endNode, endLocalPosition, firstSlice) is { } heuristicResult)
            return heuristicResult;

        var heuristic = request.KsHeuristic;
        // KS14 end

        while (request.PolyFrontier.Count /* KS14: Frontier -> PolyFrontier */ > 0 && count < _aStarNodeLimit /* KS14: NodeLimit -> cvar */)
        {
            // Handle whether we need to pause if we've taken too long
            if (count % 20 == 0 && count > 0 && request.Stopwatch.Elapsed > PathTime)
            {
                // I had this happen once in testing but I don't think it should be possible?
                DebugTools.Assert(request.PolyFrontier.Count /* KS14: Frontier -> PolyFrontier */ > 0);
                return PathResult.Continuing;
            }

            count++;
            request.KsExpansions++; // KS14

            // Actual pathfinding here
            currentNode = request.PolyFrontier.Take(); // KS14: (_, currentNode) = request.Frontier.Take() -> PolyFrontier

            // If we're inside the required distance OR we're at the end node.
            // KS14 start: against the end worked out above where the node is on the same grid; through the transforms
            //      only for one on another, past a portal
            if ((request.Distance > 0f &&
                (currentNode.GraphUid == endNode.GraphUid
                    ? (currentNode.Box.Center - endLocalPosition).LengthSquared() <= arrivalDistanceSquared
                    : currentNode.Coordinates.TryDistance(EntityManager, request.End, out var distance) && distance <= request.Distance)) ||
                ReferenceEquals(currentNode, endNode))
            // KS14 end
            /*
            if ((request.Distance > 0f &&
                currentNode.Coordinates.TryDistance(EntityManager, request.End, out var distance) &&
                distance <= request.Distance) ||
                currentNode.Equals(endNode))
            */
            {
                arrived = true;
                break;
            }

            var currentCost = request.CostSoFar[currentNode]; // KS14: read once a node, not once a neighbour

            foreach (var neighbor in currentNode.Neighbors)
            {
                var tileCost = GetTileCost(request, currentNode, neighbor);

                if (tileCost.Equals(0f))
                {
                    continue;
                }

                // f = g + h
                // gScore is distance to the start node
                // hScore is distance to the end node
                var gScore = currentCost /* KS14: request.CostSoFar[currentNode] -> currentCost */ + tileCost;

                // KS14 start: one lookup, not a read and then a write
                ref var neighborCost = ref CollectionsMarshal.GetValueRefOrAddDefault(request.CostSoFar, neighbor, out var reached);
                if (reached && gScore >= neighborCost)
                {
                    continue;
                }

                neighborCost = gScore;
                // KS14 end
                // KS14: replaced above
                /*
                if (request.CostSoFar.TryGetValue(neighbor, out var nextValue) && gScore >= nextValue)
                {
                    continue;
                }
                */

                request.CameFrom[neighbor] = currentNode;
                /* request.CostSoFar[neighbor] = gScore; */ // KS14: set above
                // pFactor is tie-breaker where the fscore is otherwise equal.
                // See http://theory.stanford.edu/~amitp/GameProgramming/Heuristics.html#breaking-ties
                // There's other ways to do it but future consideration
                // The closer the fScore is to the actual distance then the better the pathfinder will be
                // (i.e. somewhere between 1 and infinite)
                // Can use hierarchical pathfinder or whatever to improve the heuristic but this is fine for now.
                // KS14 start: the coarse maps' estimate where there is one. A poly they show cannot reach the goal is
                //      not worth queueing
                var estimate = heuristic == null ? -1f : EstimateRemaining(heuristic, neighbor);
                if (float.IsPositiveInfinity(estimate))
                    continue;

                var hScore = (estimate >= 0f ? estimate : OctileDistance(endNode, neighbor)) * (1.0f + 1.0f / 1000.0f);
                // KS14 end
                /* var hScore = OctileDistance(endNode, neighbor) * (1.0f + 1.0f / 1000.0f); */ // KS14: replaced above
                var fScore = gScore + hScore;
                request.PolyFrontier.Add(fScore, neighbor); // KS14: request.Frontier.Add((fScore, neighbor)) -> PolyFrontier
            }
        }

        if (!arrived)
        {
            return PathResult.NoPath;
        }

        var route = ReconstructPath(request.CameFrom, currentNode);
        /* var path = new Queue<EntityCoordinates>(route.Count); */ // KS14: built and never read

        foreach (var node in route)
        {
            // Due to partial planning some nodes may have been invalidated.
            if (!node.IsValid())
            {
                return PathResult.NoPath;
            }

            /* path.Enqueue(node.Coordinates); */ // KS14: see above
        }

        DebugTools.Assert(route.Count > 0);
        request.Polys = route;
        return PathResult.Path;
    }
}
