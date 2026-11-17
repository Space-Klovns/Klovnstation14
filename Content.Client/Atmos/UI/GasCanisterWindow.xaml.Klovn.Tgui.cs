using Content.Client._KS14.Tgui.Windows.Atmos;

namespace Content.Client.Atmos.UI;

// The members IKsGasCanisterWindow asks of the stock window that it does not already have; the tgui window has its own.
public sealed partial class GasCanisterWindow : IKsGasCanisterWindow
{
    // The stock window disables its eject button when the tank is out, so it has no insert.
    public event Action? TankInsertButtonPressed { add { } remove { } }

    public void SetCanisterLimit(float pressureLimit)
    {
        // The stock window has no gauges.
    }

    public void SetTankLimits(float leakPressure, float fragmentPressure)
    {
        // The stock window has no gauges.
    }
}
