// KS14: added in this fork
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
    ///     The most a search may expand, per slice. A search steered by the coarse maps walks almost straight down its
    ///         path, which costs about one expansion a tile, so a path longer than the limit would run out of nodes
    ///         with the way known. It may go to twice what the best walk costs, so a detour the estimate does not know
    ///         about still has room, but never past <see cref="SteeredNodeLimit"/>. Searches with no estimate keep
    ///         <see cref="_aStarNodeLimit"/>, which bounds how much of the map they flood.
    /// </summary>
    private int GetNodeLimit(PathHeuristic? heuristic)
    {
        if (heuristic == null || !heuristic.SearchDone || float.IsPositiveInfinity(heuristic.BestFromStart))
            return _aStarNodeLimit;

        var scaled = (int)MathF.Min(heuristic.BestFromStart * 2f + 64f, SteeredNodeLimit);
        return Math.Max(_aStarNodeLimit, scaled);
    }

    private const int SteeredNodeLimit = 8192;
}
