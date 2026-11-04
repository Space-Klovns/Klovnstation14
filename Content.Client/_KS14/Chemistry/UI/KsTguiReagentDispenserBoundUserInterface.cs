using Content.Client._KS14.UserInterface.Tgui;
using Content.Shared._KS14.Chemistry.InfiniteDispenser;
using Content.Shared.Chemistry;
using Content.Shared.Containers.ItemSlots;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Chemistry.UI;

/// <summary>
///     The chem dispenser's interface: <see cref="KsTguiReagentDispenserWindow"/>, with the reagents its
///         <see cref="KsInfiniteReagentDispenserComponent"/> makes from nothing. The soda and booze dispensers keep the
///         upstream <c>ReagentDispenserBoundUserInterface</c> and its window.
/// </summary>
[UsedImplicitly]
public sealed class KsTguiReagentDispenserBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [ViewVariables]
    private KsTguiReagentDispenserWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<KsTguiReagentDispenserWindow>();
        _window.SetInfoFromEntity(EntMan, Owner);

        if (EntMan.TryGetComponent(Owner, out KsInfiniteReagentDispenserComponent? infinite))
            _window.SetInfiniteReagents(infinite.Reagents);

        _window.EjectButton.OnPressed += _ => SendMessage(new ItemSlotButtonPressedEvent(SharedReagentDispenser.OutputSlotName));
        _window.ClearButton.OnPressed += _ => SendMessage(new ReagentDispenserClearContainerSolutionMessage());
        _window.AmountGrid.OnButtonPressed += amount => SendMessage(new ReagentDispenserSetDispenseAmountMessage(amount));

        _window.OnDispenseInfiniteReagentButtonPressed += reagent => SendMessage(new KsReagentDispenserDispenseInfiniteMessage(reagent));
        _window.OnDispenseReagentButtonPressed += location => SendMessage(new ReagentDispenserDispenseReagentMessage(location));
        _window.OnEjectJugButtonPressed += location => SendMessage(new ReagentDispenserEjectContainerMessage(location));
        _window.OnToggleValveButtonPressed += () => SendMessage(new ReagentDispenserToggleValveMessage()); // Starlight's plumbing valve
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is ReagentDispenserBoundUserInterfaceState cast)
            _window?.UpdateState(cast);
    }
}
