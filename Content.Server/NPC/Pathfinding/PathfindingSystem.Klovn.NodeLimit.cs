// KS14: added in this fork
using Content.Server._KS14.NPC.Dangers;
using Content.Server._KS14.NPC.Pathfinding;
using Content.Shared._KS14.CCVar;
using Robust.Shared.Configuration;

namespace Content.Server.NPC.Pathfinding;

public sealed partial class PathfindingSystem
{
    [Dependency] private IConfigurationManager _configurationManager = default!;

    /// <summary>
    /// How many polys an A* search may expand before it gives up and reports no path. Upstream's
    /// <see cref="NodeLimit"/> by default, set by <see cref="KsCCVars.NpcPathNodeLimit"/>. Read from the worker threads
    /// searches run on; an int, so they never see a torn value.
    /// </summary>
    private int _aStarNodeLimit = NodeLimit;

    private void InitializeKlovnNodeLimit()
    {
        Subs.CVar(_configurationManager, KsCCVars.NpcPathNodeLimit, value => _aStarNodeLimit = Math.Max(1, value), true);
    }

    /// <summary>
    ///     The most a search may expand, over all its slices (<see cref="PathRequest.KsSearchedNodes"/>). A search steered by the coarse maps walks almost straight down its
    ///         path, which costs about one expansion a tile, so a path longer than the limit would run out of nodes
    ///         with the way known. It may go to twice what the best walk costs, so a detour the estimate does not know
    ///         about still has room, but never past <see cref="SteeredNodeLimit"/>. A search that avoids dangers has
    ///         the coarse maps' estimate of a walk that ignores them, so going round a fire that costs several times
    ///         what the floor does means looking at everything the detour could be: about the area of an ellipse as long
    ///         as the walk and as wide as the detour makes it, which grows with the square of the walk (700 polys for a
    ///         walk of 24, past the usual limit, so a fire 12 tiles ahead was walked into: the search gave up on it
    ///         and ignored it). Searches with no estimate keep <see cref="_aStarNodeLimit"/>, which bounds how much of
    ///         the map they flood.
    /// </summary>
    private int GetNodeLimit(PathRequest request, PathHeuristic? heuristic)
    {
        if (heuristic == null || !heuristic.SearchDone || float.IsPositiveInfinity(heuristic.BestFromStart))
            return _aStarNodeLimit;

        var best = heuristic.BestFromStart;
        var scaled = best * 2f + 64f;

        if (request.AvoidedDangers != NpcTileDangerKind.None)
            scaled = MathF.Max(scaled, 2f * best * best + 256f);

        return Math.Max(_aStarNodeLimit, (int)MathF.Min(scaled, SteeredNodeLimit));
    }

    private const int SteeredNodeLimit = 8192;
}
