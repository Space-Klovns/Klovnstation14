using Robust.Shared.GameStates;

namespace Content.Shared._KS14.ScanDiscoverable.Base;

[RegisterComponent, NetworkedComponent]
public sealed partial class KsScanDiscoverableComponent : Component
{
    /// <summary>
    ///     New name after being discovered.
    /// </summary>
    [DataField(required: true)]
    [ViewVariables(VVAccess.ReadWrite)]
    public string TrueName = "discovered thing";

    /// <summary>
    ///     Locale shown when examining the entity before it's discovered.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public LocId? ExamineLoc = "ks-scan-discoverable-examined";

    /// <summary>
    ///     Locale for popup shown to someone after discovering this.
    ///         A param of 'name' is passed to this, which is the new name.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public LocId? DiscoveryPopupLoc = null;
}
