using Content.Client._KS14.Tgui;
using Content.Client._KS14.Tgui.Windows.Chem;
using Content.Shared.Chemistry;
using Content.Shared.Containers.ItemSlots;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;

namespace Content.Client.Chemistry.UI;

// The tgui-styled window, picked in place of the stock one when the theme is on.
public sealed partial class ChemMasterBoundUserInterface
{
    [ViewVariables]
    private KsTguiChemMasterWindow? _ksTguiWindow;

    /// <summary>
    ///     Opens the tgui window and wires its buttons to the same messages as the stock one. False when the theme is off.
    /// </summary>
    private bool TryOpenKsTgui()
    {
        if (!KsTguiTheme.ReadEnabled())
            return false;

        var window = this.CreateWindow<KsTguiChemMasterWindow>();
        _ksTguiWindow = window;
        window.Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName;

        window.OnInputEjectPressed += message => SendMessage(message);
        window.OnOutputEjectPressed += message => SendMessage(message);
        window.OnModePressed += mode => SendMessage(new ChemMasterSetModeMessage(mode));
        window.OnCreatePillsPressed += (dosage, number, label) => SendMessage(new ChemMasterCreatePillsMessage(dosage, number, label));
        window.OnCreateBottlePressed += (dosage, label) => SendMessage(new ChemMasterOutputToBottleMessage(dosage, label));
        window.OnSortPressed += () => SendMessage(new ChemMasterSortingTypeCycleMessage());
        window.OnDrawSourcePressed += source => SendMessage(new ChemMasterOutputDrawSourceMessage(source));
        window.OnPillTypePressed += pillType => SendMessage(new ChemMasterSetPillTypeMessage(pillType));
        window.OnReagentButtonPressed += (id, amount, isBuffer) => SendMessage(new ChemMasterReagentAmountButtonMessage(id, amount, isBuffer));
        window.OnToggleValveButtonPressed += () => SendMessage(new ChemMasterToggleValveMessage());
        return true;
    }
}
