using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.Piping.Unary.Components;

namespace Content.Client.Atmos.UI;

public sealed partial class GasCanisterBoundUserInterface
{
    /// <summary>
    ///     Gives the tgui window's gauges their bands: where the canister's and the holding tank's safety valves vent,
    ///         and where each bursts. They come from the prototypes, so the client's copies are good.
    /// </summary>
    private void UpdatePressureLimits()
    {
        if (_window == null || !EntMan.TryGetComponent(Owner, out GasCanisterComponent? canister))
            return;

        _window.SetCanisterPressureLimits(canister.SafetyPressure, canister.Overpressure);

        if (canister.GasTankSlot.Item is { } tankUid && EntMan.TryGetComponent(tankUid, out GasTankComponent? tank))
            _window.SetTankPressureLimits(tank.SafetyPressure, tank.Overpressure);
    }
}
