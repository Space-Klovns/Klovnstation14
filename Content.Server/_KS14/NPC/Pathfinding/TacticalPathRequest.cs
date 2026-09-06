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

    public readonly List<PathPoly> Candidates = new();

    /// <summary>
    /// Raw octile distance accumulated from <see cref="PathRequest.Start"/>, separate from
    /// <see cref="PathRequest.CostSoFar"/>'s door/smash/climb-weighted traversal cost - used to cap flood
    /// expansion by actual spatial range instead of cutting candidates off early just because a door or other
    /// costly tile sits between them and the start.
    /// </summary>
    public readonly Dictionary<PathPoly, float> DistanceSoFar = new();

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
    }
}
