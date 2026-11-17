using Content.Client._KS14.Tgui.Windows.Atmos;

namespace Content.Client.Atmos.UI;

// The members IKsGasPressurePumpWindow asks of the stock window that it does not already have; the tgui window has its own.
public sealed partial class GasPressurePumpWindow : IKsGasPressurePumpWindow
{
    public EntityUid HeatDevice
    {
        set => HeatFootnote.Device = value;
    }
}
