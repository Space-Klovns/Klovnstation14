using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC.Pathfinding;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Squad.Tactics;

/// <summary>
///     On whoever issues a squad's orders: what the squad is doing together in a fight right now - a push or a flank - and
///         when it may next. See <c>NpcSquadTacticsSystem.Fight.cs</c>.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
[Access(typeof(NpcSquadTacticsSystem))]
public sealed partial class NpcSquadFightComponent : Component
{
    /// <summary>
    ///     The hostile being pushed on, while a push is on.
    /// </summary>
    [ViewVariables]
    public EntityUid? PushTargetUid;

    [ViewVariables, AutoPausedField]
    public TimeSpan PushStartedAt;

    [ViewVariables]
    public readonly List<EntityUid> Pushers = new();

    [ViewVariables, AutoPausedField]
    public TimeSpan NextPushAt;

    /// <summary>
    ///     The hostile being flanked, while a flank is on.
    /// </summary>
    [ViewVariables]
    public EntityUid? FlankTargetUid;

    [ViewVariables, AutoPausedField]
    public TimeSpan FlankStartedAt;

    [ViewVariables]
    public readonly List<EntityUid> Flankers = new();

    /// <summary>
    ///     The way each flanker goes round, by its index in <see cref="Flankers"/>.
    /// </summary>
    [ViewVariables]
    public readonly List<NpcFlankRoute> FlankRoutes = new();

    /// <summary>
    ///     Ways round being looked for, before a flank starts: one for each spot it might go to.
    /// </summary>
    [ViewVariables]
    public readonly List<NpcFlankProbe> FlankProbes = new();

    [ViewVariables]
    public EntityUid? FlankProbeTargetUid;

    [ViewVariables, AutoPausedField]
    public TimeSpan FlankProbeStartedAt;

    [ViewVariables, AutoPausedField]
    public TimeSpan NextFlankAt;

    /// <summary>
    ///     When a squad that was spread too wide to flank may next be looked at again.
    /// </summary>
    [ViewVariables, AutoPausedField]
    public TimeSpan NextFlankCheckAt;

    /// <summary>
    ///     Stops the path searches of <see cref="FlankProbes"/> when they are given up on. Runtime only, never a datafield.
    ///     <para>
    ///         Made by <c>StartFlankProbes</c> when the first probe is, and its token handed to every probe's path job (which
    ///             runs on a worker thread). Cancelled and disposed, only ever together and only by
    ///             <c>NpcSquadTacticsSystem.CancelFlankProbes</c>, when the probes end any way: they time out, the hostile
    ///             changes, the fight ends or a grenade is about (<c>EndFlank</c>, which does so even with no flank yet), the
    ///             answers are used, new probes start, or this component shuts down. Null when no probes are out.
    ///     </para>
    /// </summary>
    [ViewVariables]
    public CancellationTokenSource? FlankProbeCancel;

    /// <summary>
    ///     When and where one of the squad's grenades last went off.
    /// </summary>
    [ViewVariables, AutoPausedField]
    public TimeSpan LastBlastAt;

    [ViewVariables]
    public EntityCoordinates LastBlastCoordinates;
}

/// <summary>
///     The way a flanker goes round: the turns of the way it was found, ending at the spot it is going to.
/// </summary>
public sealed class NpcFlankRoute
{
    public required EntityUid FlankerUid;

    public readonly List<EntityCoordinates> Waypoints = new();

    /// <summary>
    ///     The waypoint it is on its way to.
    /// </summary>
    public int Next;

    /// <summary>
    ///     How far it had to go when it set off, in tiles: for telling one that cannot get round from one on its way.
    /// </summary>
    public float StartRemaining;

    /// <summary>
    ///     For each waypoint, how far it is on along the way from there to the last, in tiles. Worked out once, when the route is
    ///         made.
    /// </summary>
    public readonly List<float> TailLengths = new();
}

/// <summary>
///     A way round to one spot by a hostile's side, being looked for.
/// </summary>
public sealed class NpcFlankProbe
{
    public required EntityUid FlankerUid;

    public required EntityCoordinates Spot;

    /// <summary>
    ///     How far round the hostile the spot is from the squad, either way: for sending two flankers round different sides.
    /// </summary>
    public required Angle Turn;

    public required Task<PathResultEvent> Path;
}
