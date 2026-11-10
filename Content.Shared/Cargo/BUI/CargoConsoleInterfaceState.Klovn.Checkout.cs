using Content.Shared._KS14.Cargo;

namespace Content.Shared.Cargo.BUI;

// Partial so the checkout list rides on the stock state without touching its constructor.
public sealed partial class CargoConsoleInterfaceState
{
    /// <summary>
    ///     The approved orders whose goods wait on a trade-station pallet or a telepad, for the tgui console's checkout.
    /// </summary>
    public List<KsCargoAwaitingOrder> KsAwaiting = new();
}
