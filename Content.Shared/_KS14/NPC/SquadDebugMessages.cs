using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.NPC;

/// <summary>
///     Sent server -> the toggling client only, telling it whether it now receives
///         <see cref="SquadDebugDataMessage"/>s.
/// </summary>
[Serializable, NetSerializable]
public sealed class SquadDebugStateMessage : EntityEventArgs
{
    public bool Enabled;
}

/// <summary>
///     A snapshot of every NPC squad, sent periodically to subscribed clients. Positions are sent as
///         coordinates rather than entities, so squads outside the viewer's PVS range still draw.
/// </summary>
[Serializable, NetSerializable]
public sealed class SquadDebugDataMessage : EntityEventArgs
{
    public List<SquadDebugSquad> Squads = new();

    /// <summary>
    ///     Every kill zone still in force: where NPCs' own went down, and they now keep out of.
    /// </summary>
    public List<SquadDebugKillZone> KillZones = new();
}

/// <summary>
///     One kill zone: the tiles it covers on its grid, each with how dangerous it is.
/// </summary>
[Serializable, NetSerializable]
public sealed class SquadDebugKillZone
{
    public NetEntity Grid;

    public Vector2i Center;

    public List<Vector2i> Tiles = new();

    /// <summary>
    ///     How dangerous each of <see cref="Tiles"/> is, in the same order, from 1 down towards 0.
    /// </summary>
    public List<float> Danger = new();

    /// <summary>
    ///     How long until NPCs stop avoiding it.
    /// </summary>
    public float SecondsLeft;
}

[Serializable, NetSerializable]
public sealed class SquadDebugSquad
{
    /// <summary>
    ///     Identifies the squad, and picks its colour.
    /// </summary>
    public NetEntity Squad;

    public NetCoordinates? Leader;

    /// <summary>
    ///     Every member other than the leader.
    /// </summary>
    public List<NetCoordinates> Members = new();

    public List<NetCoordinates> Thresholds = new();

    /// <summary>
    ///     The last reported threat, which the squad goes to while it is recent.
    /// </summary>
    public NetCoordinates? Threat;

    /// <summary>
    ///     Each member's current position paired with its assigned cover position.
    /// </summary>
    public List<SquadDebugAssignment> Assignments = new();

    /// <summary>
    ///     What each member believes about each hostile it knows of.
    /// </summary>
    public List<SquadDebugContact> Contacts = new();

    /// <summary>
    ///     How far along the squad's hunt for a hostile it lost is, if it is on one.
    /// </summary>
    public NpcHuntPhase? HuntPhase;

    /// <summary>
    ///     Where the hunted hostile should have got to.
    /// </summary>
    public NetCoordinates? HuntPredicted;

    /// <summary>
    ///     Where members wait outside each way into the hunted room.
    /// </summary>
    public List<NetCoordinates> HuntEntrances = new();

    public List<SquadDebugSearchPoint> SearchPoints = new();

    /// <summary>
    ///     Each member's order, if it has one.
    /// </summary>
    public List<SquadDebugOrder> Orders = new();

    /// <summary>
    ///     Every meter each member has a reading for - caution, say - with its current value.
    /// </summary>
    public List<SquadDebugMeter> Meters = new();
}

[Serializable, NetSerializable]
public readonly record struct SquadDebugAssignment(NetCoordinates Member, NetCoordinates Cover);

/// <summary>
///     One member's belief about one hostile: where the member is, where it believes the hostile is (the hostile
///         itself, where it was last seen, or the locker it is in), and, for a lost one, where it guesses it went.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct SquadDebugContact(NetCoordinates Member, NetCoordinates Believed, NpcContactState State, NetCoordinates? Predicted);

/// <summary>
///     One member's reading on one meter.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct SquadDebugMeter(NetCoordinates Member, string Meter, float Value, float Max);

/// <summary>
///     One spot a hunt means to check, and whether it has been.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct SquadDebugSearchPoint(NetCoordinates Coordinates, bool Cleared, bool Locker);

/// <summary>
///     One member's order: where the member is, and where it was told to go.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct SquadDebugOrder(NetCoordinates Member, NetCoordinates Target, NpcOrderKind Kind);
