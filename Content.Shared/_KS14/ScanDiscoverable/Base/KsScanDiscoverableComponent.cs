using Robust.Shared.GameStates;

namespace Content.Shared._KS14.ScanDiscoverable.Base;

/// <remarks>
///     Its fields are networked though nothing changes them, because a client that predicts a discovery removes this. The
///         state reset puts it back from the last state, so with none to read it would come back with the defaults below,
///         and the click replayed on top of it would discover the default name.
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class KsScanDiscoverableComponent : Component
{
    /// <summary>
    ///     New name after being discovered.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public LocId TrueName = "ks-scan-discovered-thing";

    /// <summary>
    ///     Locale shown when examining the entity before it's discovered.
    /// </summary>
    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public LocId? ExamineLoc = "ks-scan-discoverable-examined";

    /// <summary>
    ///     Locale for popup shown to someone after discovering this.
    ///         A param of 'name' is passed to this, which is the new name.
    /// </summary>
    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public LocId? DiscoveryPopupLoc = null;
}
