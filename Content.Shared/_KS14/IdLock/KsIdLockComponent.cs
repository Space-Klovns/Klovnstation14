using Robust.Shared.GameStates;

namespace Content.Shared._KS14.IdLock;

/// <summary>
///     Specifies an entity which can be locked/unlocked
///         depending on specific entities. Not exclusive to IDs unlike
///         what the name implies.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class KsIdLockComponent : Component
{
    /// <summary>
    ///     Which entities with <see cref="KsIdLockKeyComponent"/>
    ///         are allowed to unlock/lock this.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    [AutoNetworkedField]
    public HashSet<EntityUid> AllowedUids = [];

    /// <summary>
    ///     Whether the first key to be used on this entity
    ///         gets to be allowed, or not.
    ///
    ///     This is set to false when this happens.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    [AutoNetworkedField]
    public bool AllowClaiming = true;

    /// <summary>
    ///     Popup to be displayed when claiming this lock.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public LocId? ClaimPopupLoc = null;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public LocId? ToggleLockDeniedPopupLoc = null;
}
