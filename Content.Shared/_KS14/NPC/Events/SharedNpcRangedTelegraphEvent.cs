using Robust.Shared.Serialization;

namespace Content.Shared._KS14.NPC.Events;

/// <summary>
/// Event raised to change NPC sprite for telegraphing.
/// Same-process only (ByRefEvent + RaiseLocalEvent) - server-side raises of this
/// never reach the client. Use NPCRangedTelegraphNetworkEvent for that.
/// </summary>
[ByRefEvent]
public readonly record struct NPCRangedTelegraphEvent(EntityUid Owner, string SpriteState);

/// <summary>
/// Networked version of NPCRangedTelegraphEvent. The server raises this via
/// RaiseNetworkEvent so the client-side visualizer system actually receives it.
/// </summary>
[Serializable, NetSerializable]
public sealed class NPCRangedTelegraphNetworkEvent : EntityEventArgs
{
    public readonly NetEntity Owner;
    public readonly string SpriteState;

    public NPCRangedTelegraphNetworkEvent(NetEntity owner, string spriteState)
    {
        Owner = owner;
        SpriteState = spriteState;
    }
}
