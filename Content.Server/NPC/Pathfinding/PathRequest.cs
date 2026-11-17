using System.Threading;
using System.Threading.Tasks;
using Content.Server._KS14.NPC.Dangers; // KS14
using Content.Server._KS14.NPC.Pathfinding; // KS14
using Content.Shared.NPC;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.NPC.Pathfinding;

/// <summary>
/// Stores the in-progress data of a pathfinding request.
/// </summary>
public abstract class PathRequest
{
    public EntityCoordinates Start;

    public Task<PathResult> Task => Tcs.Task;
    public readonly TaskCompletionSource<PathResult> Tcs;

    public List<PathPoly> Polys = new();

    public bool Started = false;

    #region Pathfinding state

    public readonly Stopwatch Stopwatch = new();
    public PriorityQueue<ValueTuple<float, PathPoly>> Frontier = default!;
    public /* KS14: readonly removed, tactical requests swap in a pooled one */ Dictionary<PathPoly, float> CostSoFar = PathfindingSystem.NewPolyDictionary<float>() /* KS14: new() -> keyed by identity */;
    public /* KS14: readonly removed, A* requests swap in a pooled one */ Dictionary<PathPoly, PathPoly> CameFrom = PathfindingSystem.NewPolyDictionary<PathPoly>() /* KS14: new() -> keyed by identity */;

    #endregion

    #region Data

    public readonly PathFlags Flags;
    public readonly int CollisionLayer;
    public readonly int CollisionMask;

    // KS14 start
    /// <summary>
    ///     Door tiles this request's NPC has found it cannot get through, by grid: walls, as far as it is concerned. Built
    ///         on the main thread when the request is made and only read after, so the worker threads may share it.
    ///         Null when there are none. See <c>PathfindingSystem.Klovn.Avoid.cs</c>.
    /// </summary>
    public HashSet<(EntityUid Grid, Vector2i Tile)>? AvoidedTiles;

    /// <summary>
    ///     The dangers this request's NPC steers clear of, and how many times over a tile posing one, or touching one,
    ///         costs. See <c>PathfindingSystem.Klovn.Danger.cs</c>.
    /// </summary>
    public NpcTileDangerKind AvoidedDangers;

    public float DangerCost = 1f;
    public float DangerMarginCost = 1f;

    /// <summary>
    ///     Whether the search ran out of nodes going round dangers and went again without them, so that the path may go
    ///         through one it could have gone round. See <c>PathfindingSystem.RestartIgnoringDangers</c>.
    /// </summary>
    public bool IgnoredDangers;
    // KS14 end

    #endregion

    public PathRequest(EntityCoordinates start, PathFlags flags, int layer, int mask, CancellationToken cancelToken)
    {
        Start = start;
        Flags = flags;
        CollisionLayer = layer;
        CollisionMask = mask;
        Tcs = new TaskCompletionSource<PathResult>(cancelToken);
    }
}

public sealed class AStarPathRequest : PathRequest
{
    public EntityCoordinates End;

    /// <summary>
    /// How close we need to be to the end node to be considered as arrived.
    /// </summary>
    public float Distance;

    // KS14 start
    /// <summary>
    ///     The search's open set, used in place of <see cref="PathRequest.Frontier"/> so it can be pooled. Rented with
    ///         the rest of the search state when the request is queued, or made on the first slice if it was not.
    /// </summary>
    public PathPolyFrontier? PolyFrontier;

    /// <summary>
    ///     The coarse maps' estimate of what is left to walk, worked out on the first slice. Null when there is none, and
    ///         A* uses the straight-line distance. See <c>PathfindingSystem.Klovn.Hierarchy.cs</c>.
    /// </summary>
    public PathHeuristic? KsHeuristic;

    /// <summary>
    ///     How many polys the search has expanded, over all its slices. For tests and benchmarks.
    /// </summary>
    public int KsExpansions;

    /// <summary>
    ///     KS14: polys this search has expanded, over every slice it took: what the node limit goes by. Counted per slice,
    ///         as it was, a search suspended for time got a fresh limit on each one, so the busier the server the further
    ///         it searched. Started over when the search is (<c>RestartIgnoringDangers</c>).
    /// </summary>
    public int KsSearchedNodes;
    // KS14 end

    public AStarPathRequest(
        EntityCoordinates start,
        EntityCoordinates end,
        PathFlags flags,
        float distance,
        int layer,
        int mask,
        CancellationToken cancelToken) : base(start, flags, layer, mask, cancelToken)
    {
        Distance = distance;
        End = end;
    }
}

public sealed class BFSPathRequest : PathRequest
{
    /// <summary>
    /// How far away we're allowed to expand in distance.
    /// </summary>
    public float ExpansionRange;

    /// <summary>
    /// How many nodes we're allowed to expand
    /// </summary>
    public int ExpansionLimit;

    public BFSPathRequest(
        float expansionRange,
        int expansionLimit,
        EntityCoordinates start,
        PathFlags flags,
        int layer,
        int mask,
        CancellationToken cancelToken) : base(start, flags, layer, mask, cancelToken)
        {
            ExpansionRange = expansionRange;
            ExpansionLimit = expansionLimit;
        }
}

/// <summary>
/// Stores the final result of a pathfinding request
/// </summary>
public sealed class PathResultEvent
{
    public PathResult Result;
    public readonly List<PathPoly> Path;

    /// <summary>
    /// KS14: see <see cref="PathRequest.IgnoredDangers"/>.
    /// </summary>
    public bool KsIgnoredDangers;

    public PathResultEvent(PathResult result, List<PathPoly> path)
    {
        Result = result;
        Path = path;
    }
}
