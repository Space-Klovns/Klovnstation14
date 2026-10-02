using Content.Shared._KS14.NPC;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Squad.Tactics;

/// <summary>
///     A squad's hunt for one hostile it has lost: where it went, the room it went into, the ways into that room,
///         which of the room's floor has been seen and which has not, and the lockers worth opening - so nobody looks
///         anywhere twice.
/// </summary>
/// <remarks>
///     Everything here is kept relative to the grid it is on, never in world space: a hunt on a shuttle carries on
///         while the shuttle moves and turns.
/// </remarks>
public sealed class NpcHunt
{
    /// <summary>
    ///     The hostile being hunted, or null for a disturbance: something heard, nobody seen, which a cautious squad
    ///         searches rather than walks up to. A disturbance hunt skips the watch, since nobody is watching for
    ///         anyone, and has nobody to forget when it gives up.
    /// </summary>
    public required EntityUid? TargetUid;

    /// <summary>
    ///     The tuning the hunt runs to: its squad leader's, or the lone NPC's own.
    /// </summary>
    public required NpcSquadTacticsSettings Settings;

    /// <summary>
    ///     Where it was last seen.
    /// </summary>
    public required EntityCoordinates LastKnownCoordinates;

    /// <summary>
    ///     Where it should have got to by now, had it carried on as it was going.
    /// </summary>
    public required EntityCoordinates PredictedCoordinates;

    public required TimeSpan StartedAt;

    public NpcHuntPhase Phase = NpcHuntPhase.Watch;

    public TimeSpan PhaseStartedAt;

    /// <summary>
    ///     The grid the room is on, if the hostile was lost in a room.
    /// </summary>
    public EntityUid? GridUid;

    public readonly HashSet<Vector2i> RoomTiles = new();

    /// <summary>
    ///     The doorways into the room: not the room, but not anywhere to walk through on the way round it either.
    /// </summary>
    public readonly HashSet<Vector2i> ThresholdTiles = new();

    /// <summary>
    ///     Room tiles nobody has seen yet. The search is not over while too many are left.
    /// </summary>
    public readonly HashSet<Vector2i> UnseenTiles = new();

    public readonly List<NpcHuntEntrance> Entrances = new();

    public readonly List<NpcHuntSearchPoint> SearchPoints = new();

    /// <summary>
    ///     Which way in each staging member was sent to, and its way there.
    /// </summary>
    public readonly Dictionary<EntityUid, NpcHuntStaging> StagedMembers = new();

    /// <summary>
    ///     The unseen tile each searching member is heading for, once the lockers and spots are all taken.
    /// </summary>
    public readonly Dictionary<EntityUid, NpcHuntSweep> Sweeps = new();
}

/// <summary>
///     A way into the hunted room.
/// </summary>
/// <param name="StageCoordinates">Just outside it, where members wait to go in.</param>
/// <param name="BreachCoordinates">Just inside it, where members go when they go in.</param>
/// <param name="StageTile">The tile <paramref name="StageCoordinates"/> is on.</param>
/// <param name="LocalFacing">Into the room, relative to the grid.</param>
public readonly record struct NpcHuntEntrance(
    EntityCoordinates StageCoordinates,
    EntityCoordinates BreachCoordinates,
    Vector2i StageTile,
    Angle LocalFacing);

/// <summary>
///     One member's way to its waiting spot outside the room, round the room rather than through it.
/// </summary>
public sealed class NpcHuntStaging
{
    public required int EntranceIndex;

    /// <summary>
    ///     The turns along the way, in order, not counting the waiting spot itself.
    /// </summary>
    public readonly List<Vector2i> Waypoints = new();

    /// <summary>
    ///     The next turn to make; past the end once only the waiting spot is left.
    /// </summary>
    public int NextWaypoint;
}

/// <summary>
///     A member sweeping the room: the unseen tile it is going to look at.
/// </summary>
public readonly record struct NpcHuntSweep(Vector2i Tile, TimeSpan AssignedAt);

public enum NpcSearchPointKind : byte
{
    /// <summary>
    ///     A closed locker - the obvious place to hide.
    /// </summary>
    Locker,

    /// <summary>
    ///     Where the hostile should be by now.
    /// </summary>
    Predicted,

    /// <summary>
    ///     Where it was last seen.
    /// </summary>
    LastKnown,
}

/// <summary>
///     One spot worth checking on its own, apart from sweeping the room: a locker, or where the hostile was.
/// </summary>
public sealed class NpcHuntSearchPoint
{
    public required NpcSearchPointKind Kind;

    /// <summary>
    ///     Relative to the grid or map, or to the locker itself, so it stays with whatever it is on.
    /// </summary>
    public required EntityCoordinates Coordinates;

    /// <summary>
    ///     The locker, for <see cref="NpcSearchPointKind.Locker"/>.
    /// </summary>
    public EntityUid? StorageUid;

    public bool Cleared;

    /// <summary>
    ///     The member sent to check it, if any.
    /// </summary>
    public EntityUid? AssigneeUid;

    public TimeSpan AssignedAt;
}
