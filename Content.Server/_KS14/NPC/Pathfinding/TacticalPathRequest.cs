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
