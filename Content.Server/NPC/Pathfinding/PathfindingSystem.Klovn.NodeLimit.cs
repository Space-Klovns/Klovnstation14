// KS14: added in this fork
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
}
