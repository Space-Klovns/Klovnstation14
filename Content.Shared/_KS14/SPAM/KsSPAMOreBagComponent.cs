using Robust.Shared.GameStates;

namespace Content.Shared._KS14.SPAM;

/// <summary>
///     An ore bag a S.P.A.M. tablet can be attached to. While one is in the slot, ore that enters the bag counts toward
///         the tablet's ore tasks.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class KsSPAMOreBagComponent : Component
{
    /// <summary>
    ///     The item slot the tablet goes in.
    /// </summary>
    [DataField]
    public string Slot = "ks_spam";
}
