using Content.Client._KS14.Tgui;
using Content.Client._KS14.Tgui.Windows.Chem;
using Content.Client.UserInterface.Controls;
using Content.Shared.Chemistry;
using Content.Shared.Containers.ItemSlots;
using Robust.Client.UserInterface;

namespace Content.Client.Chemistry.UI;

// The tgui-styled window, picked in place of the stock one.
public sealed partial class ReagentDispenserBoundUserInterface
{
    [ViewVariables]
    private KsTguiReagentDispenserWindow? _ksTguiWindow;

    /// <summary>
    ///     Opens the tgui window and wires its buttons to the same messages as the stock one. False leaves the stock window to the caller.
    /// </summary>
    private bool TryOpenKsTgui()
    {
        if (!KsTguiTheme.UsePortedWindows())
            return false;

        var window = this.CreateWindow<KsTguiReagentDispenserWindow>();
        _ksTguiWindow = window;
        window.SetInfoFromEntity(EntMan, Owner);

        window.OnEjectPressed += message => SendMessage(message);
        window.OnClearPressed += () => SendMessage(new ReagentDispenserClearContainerSolutionMessage());
        window.OnAmountPressed += amount => SendMessage(new ReagentDispenserSetDispenseAmountMessage(amount));
        window.OnDispenseReagentButtonPressed += location => SendMessage(new ReagentDispenserDispenseReagentMessage(location));
        window.OnEjectJugButtonPressed += location => SendMessage(new ReagentDispenserEjectContainerMessage(location));
        window.OnToggleValveButtonPressed += () => SendMessage(new ReagentDispenserToggleValveMessage());
        return true;
    }
}
