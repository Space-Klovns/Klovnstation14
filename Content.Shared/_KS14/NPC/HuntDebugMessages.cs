using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.NPC;

/// <summary>
///     Sent server -> the toggling client only, telling it whether it now receives <see cref="HuntDebugDataMessage"/>s,
///         and whether for every hunt or only one squad's (or one NPC's own).
/// </summary>
[Serializable, NetSerializable]
public sealed class HuntDebugStateMessage : EntityEventArgs
{
    public bool Enabled;

    /// <summary>
    ///     Null = every hunt; otherwise only the hunt this squad, or this NPC or its squad, is on.
    /// </summary>
    public NetEntity? Target;
}

/// <summary>
///     Everything one hunt worked out in one update, sent as it is worked out: only while a client is watching that
///         hunt, and only when the squad's tactics actually update it. Tiles are grid-relative, so the client draws
///         them on the grid as it is, moving or not.
/// </summary>
[Serializable, NetSerializable]
public sealed class HuntDebugDataMessage : EntityEventArgs
{
    /// <summary>
    ///     The squad, or the NPC hunting on its own. Identifies the hunt.
    /// </summary>
    public NetEntity Issuer;

    public NpcHuntPhase Phase;

    public float PhaseElapsed;

    /// <summary>
    ///     How long the current phase may last before the hunt moves on regardless; 0 if it has no limit.
    /// </summary>
    public float PhaseLimit;

    public float HuntElapsed;

    public float HuntTimeout;

    /// <summary>
    ///     How much of the room's floor has been seen, and how much has to be for the room to count as searched.
    /// </summary>
    public float Coverage;

    public float RequiredCoverage;

    public NetCoordinates LastKnown;

    public NetCoordinates Predicted;

    /// <summary>
    ///     The grid the room is on, if the hostile was lost in a room. The tile lists are on it.
    /// </summary>
    public NetEntity? Grid;

    public List<Vector2i> RoomTiles = new();

    public List<Vector2i> UnseenTiles = new();

    public List<Vector2i> ThresholdTiles = new();

    public List<HuntDebugEntrance> Entrances = new();

    /// <summary>
    ///     Each staging member's way round the room: where it is, then each turn still ahead of it, then its waiting spot.
    /// </summary>
    public List<HuntDebugRoute> Routes = new();

    public List<HuntDebugSearchPoint> SearchPoints = new();

    /// <summary>
    ///     Members sweeping the room, and the unseen tile each is heading for.
    /// </summary>
    public List<HuntDebugLine> Sweeps = new();

    public List<SquadDebugOrder> Orders = new();
}

/// <summary>
///     The hunt is over: the client stops drawing it.
/// </summary>
[Serializable, NetSerializable]
public sealed class HuntDebugEndedMessage : EntityEventArgs
{
    public NetEntity Issuer;
}

[Serializable, NetSerializable]
public readonly record struct HuntDebugEntrance(NetCoordinates Stage, NetCoordinates Entry);

[Serializable, NetSerializable]
public sealed class HuntDebugRoute
{
    public List<NetCoordinates> Points = new();
}

[Serializable, NetSerializable]
public readonly record struct HuntDebugSearchPoint(NetCoordinates Coordinates, bool Locker, bool Cleared, NetCoordinates? Assignee);

[Serializable, NetSerializable]
public readonly record struct HuntDebugLine(NetCoordinates From, NetCoordinates To);
