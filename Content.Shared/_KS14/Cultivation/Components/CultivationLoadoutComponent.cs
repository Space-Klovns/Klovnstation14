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

    /// <summary>
    ///     The Techniques menu, for choosing favourites to keep on the bar. Off the bar itself: it's reached from the
    ///         category menus.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? MenuAction;

    /// <summary>
    ///     The four category menu buttons (offensive, defensive, mobility, utility), always on the bar.
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<EntityUid> CategoryActions = new();
}

/// <summary>
///     Raised on someone when cultivation grants them a technique action, so the loadout can file it.
/// </summary>
[ByRefEvent]
public readonly record struct CultivationTechniqueLearnedEvent(EntityUid Action);

public sealed partial class TechniqueMenuActionEvent : InstantActionEvent;

/// <summary>
///     Opens one category's menu of every art someone knows, at the mouse; picking one uses it. Client-side only.
/// </summary>
public sealed partial class CultivationArtsMenuActionEvent : InstantActionEvent
{
    [DataField(required: true)]
    public CultivationArtCategory Category;
}

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
