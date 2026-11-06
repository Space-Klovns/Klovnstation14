using Content.Client._KS14.Tgui.Windows.Atmos;

namespace Content.Client.Atmos.UI;

// The members IKsGasMixerWindow asks of the stock window that it does not already have; the tgui window has its own.
public sealed partial class GasMixerWindow : IKsGasMixerWindow
{
    public EntityUid HeatDevice
    {
        set => HeatFootnote.Device = value;
    }

    bool IKsGasMixerWindow.NodeOneLastEdited => NodeOneLastEdited;
}
