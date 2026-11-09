using Content.Server._KS14.NPC.Charges;
using Content.Shared._KS14.NPC;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Squad.Tactics;

/// <summary>
///     Somewhere a charge would make a way in. The rest of a charge's business is the same wherever it goes, so a new kind of
///         site only has to find one of these.
/// </summary>
/// <param name="TargetUid">What is to go: a door or a wall.</param>
/// <param name="Tile">The tile it is on.</param>
/// <param name="StandCoordinates">Where the planter stands to stick the charge on.</param>
/// <param name="StandTile">The tile <paramref name="StandCoordinates"/> is on.</param>
/// <param name="InsideCoordinates">Just inside the hostile's room, past it.</param>
/// <param name="LocalFacing">Into the room, relative to the grid.</param>
/// <param name="IsDoor">Whether it is the door of a way in that is already known about, which then needs no making.</param>
public readonly record struct NpcChargeSite(
    EntityUid TargetUid,
    Vector2i Tile,
    EntityCoordinates StandCoordinates,
    Vector2i StandTile,
    EntityCoordinates InsideCoordinates,
    Angle LocalFacing,
    bool IsDoor);

/// <summary>
///     Who could put which charge on which site, and how far they have to walk to do so.
/// </summary>
/// <param name="StandOffCoordinates">Where to press the activator from, for a wired charge: within reach of the wire.</param>
public readonly record struct NpcChargePick(
    NpcChargeSite Site,
    EntityUid PlanterUid,
    NpcCarriedCharge Charge,
    EntityCoordinates StandOffCoordinates,
    int RouteLength);

/// <summary>
///     A charge a squad is putting to use.
/// </summary>
public sealed class NpcSquadCharge
{
    public required NpcChargePick Pick;

    public NpcSquadChargePhase Phase = NpcSquadChargePhase.Planting;

    /// <summary>
    ///     How many steps each tile is from the spot in front of the charge, walking, as far out as the squad looks: how far
    ///         each member has to go, and so who is ahead of whom. Tiles of <see cref="RouteGridUid"/>.
    /// </summary>
    public readonly Dictionary<Vector2i, int> RouteSteps = new();

    public EntityUid RouteGridUid;

    /// <summary>
    ///     When <see cref="RouteSteps"/> was last worked out, if it has been.
    /// </summary>
    public TimeSpan? RouteBuiltAt;

    /// <summary>
    ///     The fewest steps the planter has had left to walk, so far.
    /// </summary>
    public int BestSteps = int.MaxValue;

    /// <summary>
    ///     Where the planter was when it last got anywhere, relative to its grid: for while it is further out than
    ///         <see cref="RouteSteps"/> goes, where any way it walks counts.
    /// </summary>
    public EntityCoordinates ProgressCoordinates;

    /// <summary>
    ///     When the planter last got any nearer the charge, or was busy with its hands on the way (a door) or at it.
    /// </summary>
    public TimeSpan ProgressAt;

    /// <summary>
    ///     When it went on.
    /// </summary>
    public TimeSpan PlantedAt;

    /// <summary>
    ///     When it was found to have gone off. An explosion takes a moment to work its way out.
    /// </summary>
    public TimeSpan BlastedAt;
}
