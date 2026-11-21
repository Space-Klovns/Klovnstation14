using Content.Shared.Actions;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Cultivation.Components;

/// <summary>
///     Which of their techniques, arts and jianghu actions a cultivator has on their action bar. Everything they learn
///         is kept, but only <see cref="Slots"/> are equipped at a time; the rest wait in the Techniques menu.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class CultivationLoadoutComponent : Component
{
    /// <summary>
    ///     Every technique action they know, equipped or not.
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<EntityUid> Learned = new();

    /// <summary>
    ///     The ones on their action bar.
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<EntityUid> Equipped = new();

    [DataField, AutoNetworkedField]
    public int Slots = 5;

    /// <summary>
    ///     Actions that always stay on the bar without using a slot (Forge the Body, the Tribulation of Flesh). They're
    ///         listed in the Techniques menu too, and picking one there puts it back on the bar if it's ever missing.
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<EntityUid> Pinned = new();

    [DataField]
    public EntityUid? MenuAction;
}

/// <summary>
///     Raised on someone when cultivation grants them a technique action, so the loadout can file it.
/// </summary>
[ByRefEvent]
public readonly record struct CultivationTechniqueLearnedEvent(EntityUid Action);

public sealed partial class TechniqueMenuActionEvent : InstantActionEvent;

[Serializable, NetSerializable]
public sealed class TechniqueLoadoutToggleMessage(NetEntity action) : BoundUserInterfaceMessage
{
    public readonly NetEntity Action = action;
}

[Serializable, NetSerializable]
public enum TechniqueLoadoutUiKey : byte
{
    Key,
}
