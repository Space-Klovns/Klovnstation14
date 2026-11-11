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

    /// <summary>
    ///     Whether the ways in have already been handed out again, after one turned out not to open for the member
    ///         leading it. Only once a hunt, so a squad that keeps being refused does not go round in circles.
    /// </summary>
    public bool ReassignedForDoors;

    /// <summary>
    ///     A member kept out of the staging, with orders of its own. Only a plan sets this: see
    ///         <see cref="NpcSquadPlan.WatcherUid"/>.
    /// </summary>
    public EntityUid? HeldBackUid;

    /// <summary>
    ///     Whether a member with a charge can lead a way in shut to it. Only a plan sets this: a squad that has merely lost
    ///         someone does not go blowing doors in.
    /// </summary>
    public bool AllowCharges;

    /// <summary>
    ///     Whether the hunt was given up on because there is no getting there, rather than because it found nothing. Anything
    ///         sent to where the hostile is from then on would only walk into a wall, or fail to plan and stand there.
    /// </summary>
    public bool ShutOut;

    /// <summary>
    ///     Whether it is the squad that is shut in, walled off from where the hostile is, rather than the hostile shut away
    ///         from them. A charge then goes on a wall of their own room, towards it, and once out they hunt it from scratch.
    /// </summary>
    public bool SealedIn;

    /// <summary>
    ///     While making a way in with a charge: the plan that holds it, which is one nobody else sees. <see cref="RunCharge"/>'s
    ///         business is the same either way, so it gets one to work with.
    /// </summary>
    public NpcSquadPlan? Blast;

    public NpcSquadAction BlastAction;

    /// <summary>
    ///     Whether a charge has already been tried, for this not to go round in circles when it does not work.
    /// </summary>
    public bool BlastTried;

    /// <summary>
    ///     While putting something in ahead of going in (<see cref="NpcHuntPhase.Soften"/>): the plan that holds the throw, which
    ///         nobody else sees, as for <see cref="Blast"/>. What it throws, and where, goes by what the squad last saw of the
    ///         hostile, never the hostile itself.
    /// </summary>
    public NpcSquadPlan? Soften;

    public NpcSquadAction SoftenAction;

    /// <summary>
    ///     Whether softening has been tried on this hunt: once only.
    /// </summary>
    public bool SoftenTried;

    /// <summary>
    ///     Whether the throw came to nothing: nobody to throw it, nowhere safe, or too long about it. They go in without.
    /// </summary>
    public bool SoftenGaveUp;

    /// <summary>
    ///     Whether something went in ahead of them: nothing more needs throwing on the way in.
    /// </summary>
    public bool Softened;

    /// <summary>
    ///     With <see cref="ShutOut"/>, the nearest they can get to it, to wait at. Null for nowhere better than where they are.
    /// </summary>
    public EntityCoordinates? ShutOutCoordinates;

    /// <summary>
    ///     With <see cref="ShutOut"/>, how long it takes to walk there, on top of how long the area gets held.
    /// </summary>
    public TimeSpan ShutOutWalk;
}

/// <summary>
///     A way into the hunted room.
/// </summary>
/// <param name="StageCoordinates">Just outside it, where members wait to go in.</param>
/// <param name="EntryCoordinates">Just inside it, where members go when they go in.</param>
/// <param name="StageTile">The tile <paramref name="StageCoordinates"/> is on.</param>
/// <param name="LocalFacing">Into the room, relative to the grid.</param>
/// <param name="DoorUid">The door in it, if there is one: none for an archway or a gap in the wall.</param>
public readonly record struct NpcHuntEntrance(
    EntityCoordinates StageCoordinates,
    EntityCoordinates EntryCoordinates,
    Vector2i StageTile,
    Angle LocalFacing,
    EntityUid? DoorUid);

/// <summary>
///     One member's way to its waiting spot outside the room, round the room rather than through it.
/// </summary>
public sealed class NpcHuntStaging
{
    public required int EntranceIndex;

    /// <summary>
    ///     Whether this member gets the way in open for the others stacked up on it - by opening its door, or by forcing
    ///         it with <see cref="ToolUid"/>. One per way in; the rest wait behind it.
    /// </summary>
    public bool IsLead;

    /// <summary>
    ///     How the lead gets the door open: <see cref="Doors.NpcBreachMethod.None"/> when it opens it by hand, or there
    ///         is no door.
    /// </summary>
    public Doors.NpcBreachMethod Method;

    /// <summary>
    ///     What the lead forces the door with.
    /// </summary>
    public EntityUid? ToolUid;

    /// <summary>
    ///     The turns along the way, in order, not counting the waiting spot itself.
    /// </summary>
    public readonly List<Vector2i> Waypoints = new();

    /// <summary>
    ///     How far that way is, in tiles: the walk, and not how far the spot is as the crow flies.
    /// </summary>
    public int RouteLength;

    /// <summary>
    ///     How much further than straight at the hostile this way takes it, in tiles: the walk round, and on in from the
    ///         way in, past where it was when it set off. See <see cref="NpcSquadTacticsSettings.MaxDetour"/>.
    /// </summary>
    public float Detour;

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
