using Robust.Shared.GameStates;

namespace Content.Shared._KS14.IdLock;

[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class KsIdLockKeyComponent : Component
{
    /// <summary>
    ///     Which locks can this be used on.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    [AutoNetworkedField]
    public HashSet<EntityUid> AttachedUids = [];

    /// <summary>
    ///     Whether you can click this with another key
    ///         to add all of this key's accesses to that one.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    [AutoNetworkedField]
    public bool Inheritable = false;

    /// <summary>
    ///     Popup to be displayed when inheriting access, if any.
    ///         Gets a param `count` which is the number of locks
    ///         this gets the access to.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public LocId? InheritPopupLoc = null;
}
