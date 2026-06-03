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
}

[Serializable, NetSerializable]
public sealed class KsZLevelComponentState(NetEntity[] stack) : ComponentState
{
    /// <summary>
    ///     LinkedListSerializer won't handle inheritors of LinkedList O ALGO.
    /// </summary>
    public NetEntity[] AssociatedStack = stack;
}
