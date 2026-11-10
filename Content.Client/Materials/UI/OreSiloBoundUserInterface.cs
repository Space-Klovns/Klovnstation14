using Content.Client._KS14.Tgui; // KS14
using Content.Client._KS14.Tgui.Windows.Materials; // KS14
using Content.Shared.Materials.OreSilo;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client.Materials.UI;

[UsedImplicitly]
public sealed partial class OreSiloBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [ViewVariables]
    private IKsOreSiloWindow? _menu; // KS14: OreSiloMenu -> IKsOreSiloWindow, the tgui theme has a window of its own

    protected override void Open()
    {
        base.Open();

        // KS14 start: the tgui theme opens its own window
        _menu = KsTguiTheme.ReadEnabled()
            ? this.CreateWindow<KsTguiOreSiloWindow>()
            : this.CreateWindow<OreSiloMenu>();
        // KS14 end
        _menu.SetEntity(Owner);

        _menu.OnClientEntryPressed += netEnt =>
        {
            SendPredictedMessage(new ToggleOreSiloClientMessage(netEnt));
        };
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not OreSiloBuiState msg)
            return;
        _menu?.Update(msg);
    }
}
