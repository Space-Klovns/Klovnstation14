using Content.Server.Chemistry.Components;
using Content.Shared._KS14.Chemistry.InfiniteDispenser;
using Content.Shared.Atmos;
using Content.Shared.Chemistry;

namespace Content.Server.Chemistry.EntitySystems;

public sealed partial class ReagentDispenserSystem
{
    private void KsInitializeInfinite()
    {
        SubscribeLocalEvent<ReagentDispenserComponent, KsReagentDispenserDispenseInfiniteMessage>(KsOnDispenseInfiniteMessage);
    }

    /// <summary>
    ///     Makes the dispense amount of one of the dispenser's own reagents in the beaker, taking it from nowhere. It comes
    ///         out at room temperature, as a jug's would.
    /// </summary>
    private void KsOnDispenseInfiniteMessage(Entity<ReagentDispenserComponent> reagentDispenser, ref KsReagentDispenserDispenseInfiniteMessage message)
    {
        if (!TryComp<KsInfiniteReagentDispenserComponent>(reagentDispenser, out var infinite) || !infinite.Reagents.Contains(message.Reagent))
            return;

        var outputContainer = _itemSlotsSystem.GetItemOrNull(reagentDispenser, SharedReagentDispenser.OutputSlotName);
        if (outputContainer is not { Valid: true }
            || !_solutionContainerSystem.TryGetFitsInDispenser(outputContainer.Value, out _, out _)
            || !_solutionContainerSystem.TryGetRefillableSolution(outputContainer.Value, out var beaker, out _))
            return;

        _solutionContainerSystem.TryAddReagent(beaker.Value, message.Reagent, (int)reagentDispenser.Comp.DispenseAmount, out _, Atmospherics.T20C);

        UpdateUiState(reagentDispenser);
        ClickSound(reagentDispenser);
    }
}
