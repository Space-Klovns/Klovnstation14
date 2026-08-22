using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.NPC;

/// <summary>
/// Sent server -> the toggling client only, telling it whether it is now subscribed to
/// <see cref="TacticalPositionDebugDataMessage"/>s.
/// </summary>
[Serializable, NetSerializable]
public sealed class TacticalPositionDebugStateMessage : EntityEventArgs
{
    public bool Enabled;
}

/// <summary>
/// Sent server -> every subscribed client whenever a TacticalPositionOperator finishes scoring a batch of
/// dynamically-flooded candidates. Only ever built/sent while at least one client is subscribed - see
/// <see cref="Content.Server._KS14.NPC.Systems.NpcTacticalPositionDebugSystem"/>.
/// </summary>
[Serializable, NetSerializable]
public sealed class TacticalPositionDebugDataMessage : EntityEventArgs
{
    public NetEntity Owner;
    public List<TacticalPositionDebugCandidate> Candidates = new();
    public NetCoordinates? Chosen;
    public List<TacticalPositionDebugClaim> Claims = new();
}

[Serializable, NetSerializable]
public readonly record struct TacticalPositionDebugCandidate(NetCoordinates Coordinates, float Score);

[Serializable, NetSerializable]
public readonly record struct TacticalPositionDebugClaim(NetCoordinates Coordinates, float ClearanceRadius);
