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
}

[Serializable, NetSerializable]
public readonly record struct SquadDebugAssignment(NetCoordinates Member, NetCoordinates Cover);
