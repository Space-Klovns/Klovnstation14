using System.Numerics;
using Content.Server.NPC.Pathfinding;

namespace Content.Server._KS14.NPC.Pathfinding;

/// <summary>
///     One A* request's estimate of what is left to walk, from the coarse maps: what each cluster costs to reach the
///         goal from, found by searching back from the goal towards the start. Worked out on the request's first slice
///         and read by every slice after. Pooled.
/// </summary>
public sealed class PathHeuristic
{
    public PathProfile Profile;

    /// <summary>
    ///     The coarse maps this request has used, by chunk. Kept, rather than looked up afresh, so a chunk rebuilt
    ///         partway through the search is still read as it was searched. Null for a chunk with none.
    /// </summary>
    public readonly Dictionary<PathChunkKey, PathChunkAbstraction?> Abstractions = new();

    /// <summary>
    ///     The coarse maps the search back from the goal was run over. A cluster in one of these it never reached cannot
    ///         reach the goal; one in a map met only later is not known either way.
    /// </summary>
    public readonly HashSet<PathChunkAbstraction> Searched = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    ///     The cheapest walk from each reached cluster to the goal: exact once settled, the best found so far otherwise.
    /// </summary>
    public readonly Dictionary<PathCluster, float> Distance = new(ReferenceEqualityComparer.Instance);

    public readonly HashSet<PathCluster> Settled = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    ///     For each region the goal is in, the cheapest walk from each of its polys to the goal without leaving it.
    /// </summary>
    public readonly Dictionary<PathRegion, float[]> GoalDistance = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    ///     For each region A* has met, the least each of its clusters could cost to walk to the goal from, by cluster
    ///         index. Worked out the first time A* needs it, once the search from the goal is done and these no longer
    ///         change.
    /// </summary>
    public readonly Dictionary<PathRegion, float[]> ClusterBounds = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    ///     Where the search back from the goal stopped: no unsettled cluster is cheaper than this to go from the start
    ///         to the goal through. Infinite if it ran out of clusters, so every reachable one was settled.
    /// </summary>
    public float Frontier;

    public EntityUid StartGraphUid;
    public Vector2 StartPosition;

    /// <summary>
    ///     The cheapest walk from the start to the goal the search from the goal has found so far.
    /// </summary>
    public float BestFromStart;

    /// <summary>
    ///     Whether the search from the goal has finished. It may take several slices.
    /// </summary>
    public bool SearchDone;

    public readonly PriorityQueue<PathCluster, float> Queue = new();
    public readonly Queue<PathRegion> ReachQueue = new();
    public readonly HashSet<PathRegion> Reached = new(ReferenceEqualityComparer.Instance);
    public readonly PriorityQueue<int, float> RegionQueue = new();
    public readonly List<(PathRegion Region, int Index, float Offset)> StartEntries = new();
    public readonly Queue<PathPoly> PolyQueue = new();
    public readonly HashSet<PathPoly> PolySeen = new(ReferenceEqualityComparer.Instance);

    public void Clear()
    {
        Abstractions.Clear();
        Searched.Clear();
        Distance.Clear();
        Settled.Clear();
        GoalDistance.Clear();
        ClusterBounds.Clear();
        Frontier = 0f;
        StartGraphUid = default;
        StartPosition = default;
        BestFromStart = 0f;
        SearchDone = false;
        Queue.Clear();
        ReachQueue.Clear();
        Reached.Clear();
        RegionQueue.Clear();
        StartEntries.Clear();
        PolyQueue.Clear();
        PolySeen.Clear();
    }
}
