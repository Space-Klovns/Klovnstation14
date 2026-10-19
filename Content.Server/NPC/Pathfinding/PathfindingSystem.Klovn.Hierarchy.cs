// KS14: added in this fork
using System.Numerics;
using Content.Server._KS14.NPC.Pathfinding;
using Content.Shared._KS14.CCVar;
using Microsoft.Extensions.ObjectPool;
using Robust.Shared.Timing;

namespace Content.Server.NPC.Pathfinding;

/// <summary>
///     Hierarchical help for A*. On its own, A* only knows the straight-line distance to the goal, so on a station - all
///         rooms and corridors - it searches everywhere closer to the goal before finding the way round, and runs out of
///         nodes long before a path across the station. Every request for somewhere that cannot be reached also searches
///         to its limit before giving up.
///
///     So each navmesh chunk gets a coarse map (<see cref="PathChunkAbstraction"/>), per path profile: its walkable polys
///         in connected regions, the clusters of border polys each region leaves the chunk through, and what it costs to
///         walk from every poly to every cluster of its region. A request then searches back from its goal over the
///         clusters, towards its start (<see cref="UpdateHeuristic"/>), which says what each cluster costs to reach the
///         goal from. A* reads that as its estimate of what is left to walk (<see cref="EstimateRemaining"/>): exact
///         where the search reached, so A* walks the path almost without straying, and a poly that cannot reach the goal
///         at all is never queued. A goal that cannot be reached is found out before A* starts.
///
///     The paths are the ones A* would find with no node limit: the estimate never overstates what is left, as it is
///         worked out from the same costs over the same navmesh. An NPC's own avoided tiles (doors it cannot get
///         through) only make the walk longer than the coarse maps think, which keeps the estimate a lower bound.
///
///     Coarse maps are built when a search first needs them, and dropped when their chunk or a neighbour is rebuilt.
///         See <see cref="KsCCVars.NpcPathHierarchical"/>.
/// </summary>
public sealed partial class PathfindingSystem
{
    [Dependency] private EntityQuery<GridPathfindingComponent> _gridPathfindingQuery = default!;

    /// <summary>
    ///     How many regions the quick check from the start floods before leaving the rest to the search from the goal. A
    ///         start shut in somewhere small is found out within it, however large the goal's side is. Only changed by
    ///         tests.
    /// </summary>
    internal int ReachRegionBudget = 32;

    private static readonly Vector2i[] ChunkNeighborOffsets =
    [
        new(-1, 0),
        new(1, 0),
        new(0, -1),
        new(0, 1),
    ];

    private bool _hierarchical = true;

    private readonly ObjectPool<PathHeuristic> _heuristicPool =
        new DefaultObjectPool<PathHeuristic>(new PathHeuristicPolicy(), SearchPoolSize);

    private void InitializeKlovnHierarchy()
    {
        Subs.CVar(_configurationManager, KsCCVars.NpcPathHierarchical, value => _hierarchical = value, true);
    }

    #region Coarse maps

    /// <summary>
    ///     Drops the coarse maps of the chunks just rebuilt, and of their neighbours, whose border polys they changed.
    ///         On the main thread, while no search runs.
    /// </summary>
    private static void DropAbstractions(Entity<GridPathfindingComponent> pathfinding, GridPathfindingChunk[] rebuiltChunks)
    {
        foreach (var chunk in rebuiltChunks)
        {
            chunk.KsAbstractions = null;

            foreach (var offset in ChunkNeighborOffsets)
            {
                if (pathfinding.Comp.Chunks.TryGetValue(chunk.Origin + offset, out var neighborChunk))
                    neighborChunk.KsAbstractions = null;
            }
        }
    }

    /// <summary>
    ///     The coarse map of the chunk at <paramref name="key"/> for <paramref name="heuristic"/>'s profile, or null if
    ///         there is no chunk there. Kept by the request, so it reads one chunk the same way for its whole search.
    /// </summary>
    private PathChunkAbstraction? GetAbstraction(PathHeuristic heuristic, PathChunkKey key)
    {
        if (heuristic.Abstractions.TryGetValue(key, out var abstraction))
            return abstraction;

        abstraction = null;
        if (_gridPathfindingQuery.TryComp(key.GraphUid, out var gridPathfindingComponent) &&
            gridPathfindingComponent.Chunks.TryGetValue(key.Origin, out var chunk))
        {
            abstraction = GetAbstraction(chunk, key, heuristic.Profile);
        }

        heuristic.Abstractions[key] = abstraction;
        return abstraction;
    }

    private PathChunkAbstraction GetAbstraction(GridPathfindingChunk chunk, PathChunkKey key, PathProfile profile)
    {
        // Searches run in parallel and may want the same chunk at once: one builds it, the others wait for it.
        lock (chunk)
        {
            chunk.KsAbstractions ??= new List<PathChunkAbstraction>();

            foreach (var existing in chunk.KsAbstractions)
            {
                if (existing.Profile == profile)
                    return existing;
            }

            var built = BuildAbstraction(chunk, key, profile);
            chunk.KsAbstractions.Add(built);
            return built;
        }
    }

    private PathChunkAbstraction BuildAbstraction(GridPathfindingChunk chunk, PathChunkKey key, PathProfile profile)
    {
        var abstraction = new PathChunkAbstraction(profile);
        var pending = new Stack<PathPoly>();

        // Regions: walkable polys joined without leaving the chunk.
        foreach (var tilePolys in chunk.Polygons)
        {
            foreach (var poly in tilePolys)
            {
                if (abstraction.RegionOf.ContainsKey(poly))
                    continue;

                var modifier = GetTileModifier(profile.Flags, profile.CollisionLayer, profile.CollisionMask, poly);
                if (modifier.Equals(0f))
                    continue;

                var region = new PathRegion(abstraction, key);
                abstraction.Regions.Add(region);
                AddToRegion(abstraction, region, poly, modifier);
                pending.Push(poly);

                while (pending.TryPop(out var current))
                {
                    foreach (var neighbor in current.Neighbors)
                    {
                        if (neighbor.GraphUid != key.GraphUid ||
                            neighbor.ChunkOrigin != key.Origin ||
                            abstraction.RegionOf.ContainsKey(neighbor))
                            continue;

                        var neighborModifier = GetTileModifier(profile.Flags, profile.CollisionLayer, profile.CollisionMask, neighbor);
                        if (neighborModifier.Equals(0f))
                            continue;

                        AddToRegion(abstraction, region, neighbor, neighborModifier);
                        pending.Push(neighbor);
                    }
                }
            }
        }

        var queue = new PriorityQueue<int, float>();

        foreach (var region in abstraction.Regions)
        {
            // Clusters: its polys with a neighbour in another chunk.
            for (var index = 0; index < region.Polys.Count; index++)
            {
                foreach (var neighbor in region.Polys[index].Neighbors)
                {
                    if (neighbor.GraphUid == key.GraphUid && neighbor.ChunkOrigin == key.Origin)
                        continue;

                    region.ClusterOfPoly[index] = region.Clusters.Count;
                    region.Clusters.Add(new PathCluster(region, region.Clusters.Count, index));
                    break;
                }
            }

            // What each poly costs to walk to each cluster from, and so each cluster to each other.
            foreach (var cluster in region.Clusters)
            {
                var distances = NewDistances(region.Polys.Count);
                distances[cluster.PolyIndex] = 0f;
                queue.Enqueue(cluster.PolyIndex, 0f);

                SearchRegion(region, distances, queue);
                cluster.DistanceTo = distances;
            }

            // The walks between clusters, less those as cheap by way of a third.
            foreach (var cluster in region.Clusters)
            {
                foreach (var from in region.Clusters)
                {
                    if (from == cluster)
                        continue;

                    var cost = cluster.DistanceTo[from.PolyIndex];
                    var tolerance = cost * 0.0001f + 0.0001f;
                    var redundant = false;

                    foreach (var via in region.Clusters)
                    {
                        if (via == cluster || via == from)
                            continue;

                        if (via.DistanceTo[from.PolyIndex] + cluster.DistanceTo[via.PolyIndex] <= cost + tolerance)
                        {
                            redundant = true;
                            break;
                        }
                    }

                    if (!redundant)
                        cluster.Incoming.Add((from.Index, cost));
                }
            }
        }

        return abstraction;
    }

    private static void AddToRegion(PathChunkAbstraction abstraction, PathRegion region, PathPoly poly, float modifier)
    {
        abstraction.RegionOf[poly] = (region, region.Polys.Count);
        region.Polys.Add(poly);
        region.Modifiers.Add(modifier);
        region.ClusterOfPoly.Add(-1);
    }

    private static float[] NewDistances(int count)
    {
        var distances = new float[count];
        Array.Fill(distances, float.PositiveInfinity);
        return distances;
    }

    /// <summary>
    ///     Fills in <paramref name="distances"/>: what each poly of <paramref name="region"/> costs to walk, staying in
    ///         it, to the cheapest of those already queued in <paramref name="queue"/>. Searches backwards: stepping from
    ///         one poly onto another costs what <see cref="GetTileCost"/> says, which goes by the poly stepped onto.
    /// </summary>
    private void SearchRegion(PathRegion region, float[] distances, PriorityQueue<int, float> queue)
    {
        while (queue.TryDequeue(out var index, out var distance))
        {
            if (distance > distances[index])
                continue;

            var poly = region.Polys[index];
            var modifier = region.Modifiers[index];

            foreach (var neighbor in poly.Neighbors)
            {
                if (!region.Abstraction.RegionOf.TryGetValue(neighbor, out var entry) || entry.Region != region)
                    continue;

                var candidate = distance + modifier * OctileDistance(poly, neighbor);
                if (candidate >= distances[entry.Index])
                    continue;

                distances[entry.Index] = candidate;
                queue.Enqueue(entry.Index, candidate);
            }
        }
    }

    #endregion

    #region Per request

    /// <summary>
    ///     Works out <paramref name="request"/>'s estimate before A* starts searching: on its first slice, and on as many
    ///         after as the search from the goal takes, within each slice's time. Returns null once A* can go on, or
    ///         what the slice should return: <see cref="PathResult.Continuing"/> while the search from the goal is not
    ///         done, <see cref="PathResult.NoPath"/> if the goal cannot be reached from the start, whatever the NPC's
    ///         own avoided tiles. Leaves the request without an estimate, and A* on its straight-line one, when
    ///         hierarchical pathfinding is off or the start is already there.
    /// </summary>
    private PathResult? UpdateHeuristic(AStarPathRequest request,
        PathPoly startNode,
        PathPoly endNode,
        Vector2 endLocalPosition,
        bool firstSlice)
    {
        if (firstSlice && !StartHeuristic(request, startNode, endNode, endLocalPosition))
            return PathResult.NoPath;

        if (request.KsHeuristic is not { SearchDone: false } heuristic)
            return null;

        if (!SearchFromGoal(heuristic, request.Stopwatch))
            return PathResult.Continuing;

        return heuristic.BestFromStart < float.PositiveInfinity ? null : PathResult.NoPath;
    }

    /// <summary>
    ///     Sets up <paramref name="request"/>'s estimate, and the search from the goal. Returns false if the goal is
    ///         known to be out of reach already.
    /// </summary>
    private bool StartHeuristic(AStarPathRequest request, PathPoly startNode, PathPoly endNode, Vector2 endLocalPosition)
    {
        if (!_hierarchical || IsArrival(request, startNode, endNode, endLocalPosition))
            return true;

        var heuristic = _heuristicPool.Get();
        request.KsHeuristic = heuristic;
        heuristic.Profile = new PathProfile(request.Flags, request.CollisionLayer, request.CollisionMask);
        heuristic.StartGraphUid = startNode.GraphUid;
        heuristic.StartPosition = startNode.Box.Center;

        if (!FindGoal(request, heuristic, endNode, endLocalPosition) ||
            !FindStart(heuristic, startNode) ||
            !MayReach(heuristic))
            return false;

        foreach (var (region, goalDistances) in heuristic.GoalDistance)
        {
            foreach (var cluster in region.Clusters)
            {
                Relax(heuristic, cluster, goalDistances[cluster.PolyIndex]);
            }
        }

        heuristic.BestFromStart = GetBestFromStart(heuristic);
        return true;
    }

    private static bool IsArrival(AStarPathRequest request, PathPoly poly, PathPoly endNode, Vector2 endLocalPosition)
    {
        return ReferenceEquals(poly, endNode) ||
            request.Distance > 0f &&
            poly.GraphUid == endNode.GraphUid &&
            (poly.Box.Center - endLocalPosition).LengthSquared() <= request.Distance * request.Distance;
    }

    /// <summary>
    ///     The walkable polys A* would count as arriving - the end's, or any within the request's distance of the end -
    ///         and what each poly of their regions costs to walk to one.
    /// </summary>
    private bool FindGoal(AStarPathRequest request, PathHeuristic heuristic, PathPoly endNode, Vector2 endLocalPosition)
    {
        heuristic.PolyQueue.Enqueue(endNode);
        heuristic.PolySeen.Add(endNode);

        while (heuristic.PolyQueue.TryDequeue(out var poly))
        {
            if (GetAbstraction(heuristic, PathChunkKey.Of(poly)) is { } abstraction &&
                abstraction.RegionOf.TryGetValue(poly, out var entry))
            {
                if (!heuristic.GoalDistance.TryGetValue(entry.Region, out var goalDistances))
                {
                    goalDistances = NewDistances(entry.Region.Polys.Count);
                    heuristic.GoalDistance[entry.Region] = goalDistances;
                }

                goalDistances[entry.Index] = 0f;
            }

            if (request.Distance <= 0f)
                continue;

            foreach (var neighbor in poly.Neighbors)
            {
                if (IsArrival(request, neighbor, endNode, endLocalPosition) && heuristic.PolySeen.Add(neighbor))
                    heuristic.PolyQueue.Enqueue(neighbor);
            }
        }

        foreach (var (region, goalDistances) in heuristic.GoalDistance)
        {
            for (var index = 0; index < goalDistances.Length; index++)
            {
                if (goalDistances[index] == 0f)
                    heuristic.RegionQueue.Enqueue(index, 0f);
            }

            SearchRegion(region, goalDistances, heuristic.RegionQueue);
        }

        return heuristic.GoalDistance.Count > 0;
    }

    /// <summary>
    ///     Where A* starts from, in the coarse maps: the start poly's region, or, for a start A* never has to step onto
    ///         - an NPC stood half in a doorway, say - each walkable neighbour's, at the cost of stepping onto it.
    /// </summary>
    private bool FindStart(PathHeuristic heuristic, PathPoly startNode)
    {
        if (GetAbstraction(heuristic, PathChunkKey.Of(startNode)) is { } startAbstraction &&
            startAbstraction.RegionOf.TryGetValue(startNode, out var startEntry))
        {
            heuristic.StartEntries.Add((startEntry.Region, startEntry.Index, 0f));
            return true;
        }

        foreach (var neighbor in startNode.Neighbors)
        {
            if (GetAbstraction(heuristic, PathChunkKey.Of(neighbor)) is { } abstraction &&
                abstraction.RegionOf.TryGetValue(neighbor, out var entry))
            {
                heuristic.StartEntries.Add((entry.Region, entry.Index,
                    entry.Region.Modifiers[entry.Index] * OctileDistance(neighbor, startNode)));
            }
        }

        return heuristic.StartEntries.Count > 0;
    }

    /// <summary>
    ///     Floods the regions reachable from the start, up to <see cref="ReachRegionBudget"/> of them. False only if it
    ///         ran out of regions without meeting the goal's: the start is shut in, apart from the goal.
    /// </summary>
    private bool MayReach(PathHeuristic heuristic)
    {
        foreach (var (region, _, _) in heuristic.StartEntries)
        {
            if (heuristic.Reached.Add(region))
                heuristic.ReachQueue.Enqueue(region);
        }

        while (heuristic.ReachQueue.TryDequeue(out var region))
        {
            if (heuristic.GoalDistance.ContainsKey(region) || heuristic.Reached.Count > ReachRegionBudget)
                return true;

            foreach (var cluster in region.Clusters)
            {
                foreach (var neighbor in region.Polys[cluster.PolyIndex].Neighbors)
                {
                    if (TryGetOtherChunkEntry(heuristic, region, neighbor, out var entry) && heuristic.Reached.Add(entry.Region))
                        heuristic.ReachQueue.Enqueue(entry.Region);
                }
            }
        }

        return false;
    }

    /// <summary>
    ///     Searches back from the goal's clusters over the coarse maps, towards the start, until nothing left unsettled
    ///         could be on a cheaper way from the start than the best found. Every cluster settled by then knows exactly
    ///         what it costs to walk to the goal from, and <see cref="PathHeuristic.BestFromStart"/> is the cheapest walk
    ///         from the start: infinite if there is none. Returns false if the slice's time ran out first; the next slice
    ///         carries on.
    /// </summary>
    private bool SearchFromGoal(PathHeuristic heuristic, Stopwatch stopwatch)
    {
        var settledThisSlice = 0;
        heuristic.Frontier = float.PositiveInfinity;

        while (heuristic.Queue.TryPeek(out var cluster, out var priority))
        {
            if (priority >= heuristic.BestFromStart)
            {
                heuristic.Frontier = priority;
                break;
            }

            if (++settledThisSlice % 32 == 0 && stopwatch.Elapsed > PathTime)
                return false;

            heuristic.Queue.Dequeue();

            var distance = heuristic.Distance[cluster];

            // Queued again since, cheaper.
            if (heuristic.Settled.Contains(cluster) || priority > distance + GetStartEstimate(heuristic, cluster) + 0.001f)
                continue;

            heuristic.Settled.Add(cluster);

            if (IsStartRegion(heuristic, cluster.Region))
                heuristic.BestFromStart = GetBestFromStart(heuristic);

            // Across the region, from its other clusters.
            foreach (var (index, cost) in cluster.Incoming)
            {
                Relax(heuristic, cluster.Region.Clusters[index], distance + cost);
            }

            // Onto this one from its neighbours in other chunks, each of which is on its own chunk's edge too.
            var poly = cluster.Region.Polys[cluster.PolyIndex];
            var modifier = cluster.Region.Modifiers[cluster.PolyIndex];

            foreach (var neighbor in poly.Neighbors)
            {
                if (!TryGetOtherChunkEntry(heuristic, cluster.Region, neighbor, out var entry) ||
                    entry.Region.ClusterOfPoly[entry.Index] is not (>= 0 and var neighborClusterIndex))
                    continue;

                Relax(heuristic, entry.Region.Clusters[neighborClusterIndex], distance + modifier * OctileDistance(poly, neighbor));
            }
        }

        foreach (var abstraction in heuristic.Abstractions.Values)
        {
            if (abstraction != null)
                heuristic.Searched.Add(abstraction);
        }

        heuristic.SearchDone = true;
        return true;
    }

    /// <summary>
    ///     <paramref name="neighbor"/>'s region and index there, if it is walkable and in a chunk other than
    ///         <paramref name="region"/>'s.
    /// </summary>
    private bool TryGetOtherChunkEntry(PathHeuristic heuristic, PathRegion region, PathPoly neighbor, out (PathRegion Region, int Index) entry)
    {
        entry = default;
        var neighborChunk = PathChunkKey.Of(neighbor);

        return neighborChunk != region.Chunk &&
            GetAbstraction(heuristic, neighborChunk) is { } abstraction &&
            abstraction.RegionOf.TryGetValue(neighbor, out entry);
    }

    private void Relax(PathHeuristic heuristic, PathCluster cluster, float distance)
    {
        if (float.IsPositiveInfinity(distance) ||
            heuristic.Distance.TryGetValue(cluster, out var existing) && existing <= distance)
            return;

        heuristic.Distance[cluster] = distance;
        heuristic.Settled.Remove(cluster);
        heuristic.Queue.Enqueue(cluster, distance + GetStartEstimate(heuristic, cluster));
    }

    /// <summary>
    ///     The cheapest walk from the start to the goal found so far.
    /// </summary>
    private static float GetBestFromStart(PathHeuristic heuristic)
    {
        var best = float.PositiveInfinity;

        foreach (var (region, index, offset) in heuristic.StartEntries)
        {
            var fromHere = heuristic.GoalDistance.TryGetValue(region, out var goalDistances)
                ? goalDistances[index]
                : float.PositiveInfinity;

            foreach (var cluster in region.Clusters)
            {
                if (heuristic.Distance.TryGetValue(cluster, out var distance))
                    fromHere = MathF.Min(fromHere, cluster.DistanceTo[index] + distance);
            }

            best = MathF.Min(best, offset + fromHere);
        }

        return best;
    }

    private static bool IsStartRegion(PathHeuristic heuristic, PathRegion region)
    {
        foreach (var (startRegion, _, _) in heuristic.StartEntries)
        {
            if (startRegion == region)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     The least the start could be from <paramref name="cluster"/>: the straight-line distance, on the same grid.
    /// </summary>
    private static float GetStartEstimate(PathHeuristic heuristic, PathCluster cluster)
    {
        if (cluster.Region.Chunk.GraphUid != heuristic.StartGraphUid)
            return 0f;

        var difference = Vector2.Abs(cluster.Region.Polys[cluster.PolyIndex].Box.Center - heuristic.StartPosition);
        return difference.X + difference.Y + (1.41f - 2) * MathF.Min(difference.X, difference.Y);
    }

    /// <summary>
    ///     The least <paramref name="cluster"/> could cost to walk to the goal from. Exact once settled. Infinite for one
    ///         the search never reached, when it ran out of clusters to search: it cannot reach the goal at all.
    /// </summary>
    private static float GetLowerBound(PathHeuristic heuristic, PathCluster cluster)
    {
        if (heuristic.Settled.Contains(cluster))
            return heuristic.Distance[cluster];

        // Met only after the search, in a chunk rebuilt since: nothing is known of it.
        if (!heuristic.Searched.Contains(cluster.Region.Abstraction))
            return 0f;

        if (float.IsPositiveInfinity(heuristic.Frontier))
            return float.PositiveInfinity;

        // Everything unsettled is at least as far from the start, through the goal, as where the search stopped.
        var bound = MathF.Max(0f, heuristic.Frontier - GetStartEstimate(heuristic, cluster));
        return heuristic.Distance.TryGetValue(cluster, out var found) ? MathF.Min(bound, found) : bound;
    }

    /// <summary>
    ///     What A* has left to walk from <paramref name="poly"/> to the goal, at least: the cheapest way out of its region
    ///         and on from there, or straight to the goal if it is in the goal's region. Infinite if it cannot reach the
    ///         goal; negative if the coarse maps know nothing of it - not walkable, or in a chunk rebuilt since - in which
    ///         case A* falls back on the straight-line distance.
    /// </summary>
    private float EstimateRemaining(PathHeuristic heuristic, PathPoly poly)
    {
        if (GetAbstraction(heuristic, PathChunkKey.Of(poly)) is not { } abstraction ||
            !abstraction.RegionOf.TryGetValue(poly, out var entry))
            return -1f;

        var region = entry.Region;
        var best = heuristic.GoalDistance.TryGetValue(region, out var goalDistances)
            ? goalDistances[entry.Index]
            : float.PositiveInfinity;

        if (!heuristic.ClusterBounds.TryGetValue(region, out var bounds))
        {
            bounds = new float[region.Clusters.Count];
            for (var i = 0; i < bounds.Length; i++)
            {
                bounds[i] = GetLowerBound(heuristic, region.Clusters[i]);
            }

            heuristic.ClusterBounds[region] = bounds;
        }

        for (var i = 0; i < bounds.Length; i++)
        {
            var toCluster = region.Clusters[i].DistanceTo[entry.Index];
            if (toCluster < best)
                best = MathF.Min(best, toCluster + bounds[i]);
        }

        return best;
    }

    private void ReturnHeuristic(AStarPathRequest request)
    {
        if (request.KsHeuristic == null)
            return;

        _heuristicPool.Return(request.KsHeuristic);
        request.KsHeuristic = null;
    }

    private sealed class PathHeuristicPolicy : PooledObjectPolicy<PathHeuristic>
    {
        public override PathHeuristic Create()
        {
            return new PathHeuristic();
        }

        public override bool Return(PathHeuristic heuristic)
        {
            heuristic.Clear();
            return true;
        }
    }

    #endregion
}
