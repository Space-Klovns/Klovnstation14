using Robust.Shared.GameStates;

namespace Content.Shared._KS14.SalvageTablet;

/// <summary>
///     An ore bag a S.P.A.M. tablet can be attached to. While one is in the slot, ore that enters the bag counts toward
///         the tablet's ore tasks.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class KsSalvageTabletOreBagComponent : Component
{
    /// <summary>
    ///     The item slot the tablet goes in.
    /// </summary>
    [DataField]
    public string Slot = "ks_salvage_tablet";
}
