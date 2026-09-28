using System.Threading;
using Content.Server.NPC.Pathfinding;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Pathfinding;

/// <summary>
/// Floods the poly graph from <see cref="PathRequest.Start"/> and collects up to <see cref="MaxCandidates"/>
/// reachable <see cref="PathPoly"/> nodes, for <see cref="TacticalPositionOperator"/>'s dynamic
/// camping/retreat/advance-position scoring. Unlike <see cref="BFSPathRequest"/>, this does not reconstruct
/// a route - callers only need candidate endpoints, not a path to one of them.
/// </summary>
public sealed class TacticalPathRequest : PathRequest
{
    /// <summary>
    /// How far away we're allowed to expand in distance.
    /// </summary>
    public float ExpansionRange;

    /// <summary>
    /// How many candidate nodes to return at most.
    /// </summary>
    public int MaxCandidates;

    public readonly List<PathPoly> Candidates;

    /// <summary>
    /// The flood's open set, used in place of <see cref="PathRequest.Frontier"/> so it can be pooled. Rented with the
    /// rest of the search state, see <see cref="PathfindingSystem.RentTacticalSearchState"/>.
    /// </summary>
    public TacticalFrontier TacticalFrontier = default!;

    /// <summary>
    /// How many polys the flood has expanded so far, over every tick it has run for.
    /// </summary>
    public int ExpandedCount;

    /// <summary>
    /// Raw octile distance accumulated from <see cref="PathRequest.Start"/>, separate from
    /// <see cref="PathRequest.CostSoFar"/>'s door/smash/climb-weighted traversal cost - used to cap flood
    /// expansion by actual spatial range instead of cutting candidates off early just because a door or other
    /// costly tile sits between them and the start. Rented from a pool with the rest of the search state, see
    /// <see cref="PathfindingSystem.RentTacticalSearchState"/>.
    /// </summary>
    public Dictionary<PathPoly, float> DistanceSoFar = default!;

    public TacticalPathRequest(
        EntityCoordinates start,
        float expansionRange,
        int maxCandidates,
        PathFlags flags,
        int layer,
        int mask,
        CancellationToken cancelToken) : base(start, flags, layer, mask, cancelToken)
    {
        ExpansionRange = expansionRange;
        MaxCandidates = maxCandidates;
        Candidates = new List<PathPoly>(Math.Clamp(maxCandidates, 0, MaxPresizedCandidates));
    }

    /// <summary>
    /// The most candidates <see cref="Candidates"/> is sized for up front, so a silly cap does not allocate a silly list.
    /// </summary>
    private const int MaxPresizedCandidates = 256;
}
