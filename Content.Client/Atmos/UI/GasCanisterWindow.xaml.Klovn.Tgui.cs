using Content.Client._KS14.Tgui.Windows.Atmos;

namespace Content.Client.Atmos.UI;

// The members IKsGasCanisterWindow asks of the stock window that it does not already have; the tgui window has its own.
public sealed partial class GasCanisterWindow : IKsGasCanisterWindow
{
    public void SetKsFluid(Content.Shared._KS14.Fluidmos.KsFluidEntry[]? entries)
    {
        KsFluidList.SetEntries(entries);
    }
}
