using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.ZLevel;

[RegisterComponent, NetworkedComponent]
[Access(typeof(KsZLevelSystem))]
public sealed partial class KsZLevelComponent : Component
{
    [ViewVariables(VVAccess.ReadOnly)]
    public KsZLevelStack AssociatedStack = [];

    [ViewVariables(VVAccess.ReadOnly)]
    public LinkedListNode<Entity<KsZLevelComponent>> Node;
}

/// <summary>
///     Raised on a z-level when it is being destroyed.
/// </summary>
[ByRefEvent]
public record struct KsZLevelRemoved(Entity<KsZLevelComponent> Entity);

/// <summary>
///     Represents a z-level stack—an ordered collection of z-level entities.
///
///     The first element of the list represents the bottom-most z-level of the stack,
///         and the last element represents the top-most one.
/// </summary>
[Access(typeof(KsZLevelSystem))]
public sealed class KsZLevelStack : LinkedList<Entity<KsZLevelComponent>>;

[Serializable, NetSerializable]
public sealed class KsZLevelComponentState(NetEntity[] stack) : ComponentState
{
    /// <summary>
    ///     LinkedListSerializer won't handle inheritors of LinkedList O ALGO.
    /// </summary>
    public NetEntity[] AssociatedStack = stack;
}
