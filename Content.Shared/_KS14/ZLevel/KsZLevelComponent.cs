using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.ZLevel;

[RegisterComponent, NetworkedComponent]
[Access(typeof(KsZLevelSystem))]
public sealed partial class KsZLevelComponent : Component
{
    [ViewVariables(VVAccess.ReadOnly)]
    [DataField]
    public LinkedList<Entity<KsZLevelComponent>> AssociatedStack = [];

    // If AssociatedStack isnt empty this will be set automatically in ComponentInit
    [ViewVariables(VVAccess.ReadOnly)]
    public LinkedListNode<Entity<KsZLevelComponent>> Node;

    /// <summary>
    ///     How far this z-level sits below the one above it, in z-levels.
    ///     Drives both how far away this z-level is rendered and how long an entity takes to fall through it,
    ///         so a z-level with a Depth of 2 looks twice as far down and takes twice as long to cross.
    /// </summary>
    [DataField]
    public float Depth = 1f;
}

[Serializable, NetSerializable]
public sealed class KsZLevelComponentState(NetEntity[] stack, float depth) : ComponentState
{
    /// <summary>
    ///     LinkedListSerializer won't handle inheritors of LinkedList O ALGO.
    /// </summary>
    public NetEntity[] AssociatedStack = stack;

    public float Depth = depth;
}
