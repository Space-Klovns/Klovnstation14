using Content.Server.Chemistry.Components;
using Content.Shared._KS14.Chemistry.Separation;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;

namespace Content.Server.Chemistry.EntitySystems;

public sealed partial class ChemMasterSystem
{
    [Dependency] private KsReagentSeparationSystem _ksReagentSeparationSystem = default!;

    /// <summary>
    ///     Transfers an inseparable reagent as its whole group, in proportion. False for an ordinary reagent, which takes
    ///         upstream's path.
    /// </summary>
    private bool KsTryTransferGroup(
        Entity<ChemMasterComponent> chemMaster,
        ReagentId id,
        FixedPoint2 amount,
        bool fromBuffer,
        Entity<SolutionComponent> containerEntity,
        Solution containerSolution,
        Entity<SolutionComponent> bufferEntity,
        Solution bufferSolution)
    {
        if (!_ksReagentSeparationSystem.IsInseparable(id.Prototype))
            return false;

        bool expanded;
        if (fromBuffer)
        {
            var moved = _ksReagentSeparationSystem.SplitSelected(bufferSolution, id, FixedPoint2.Min(amount, containerSolution.AvailableVolume), out expanded);
            _solutionContainerSystem.TryAddSolution(containerEntity, moved);
        }
        else
        {
            var moved = _ksReagentSeparationSystem.SplitSelected(containerSolution, id, FixedPoint2.Min(amount, bufferSolution.AvailableVolume), out expanded);
            _solutionContainerSystem.UpdateChemicals(containerEntity);
            _solutionContainerSystem.TryAddSolution(bufferEntity, moved);
        }

        KsPopupGroupMoved(chemMaster, id, expanded);
        UpdateUiState(chemMaster, updateLabel: true);
        return true;
    }

    /// <summary>
    ///     Discards an inseparable reagent as its whole group, in proportion. False for an ordinary reagent, which takes
    ///         upstream's path.
    /// </summary>
    private bool KsTryDiscardGroup(Entity<ChemMasterComponent> chemMaster, ReagentId id, FixedPoint2 amount, bool fromBuffer)
    {
        if (!_ksReagentSeparationSystem.IsInseparable(id.Prototype))
            return false;

        bool expanded;
        if (fromBuffer)
        {
            if (!_solutionContainerSystem.TryGetSolution(chemMaster.Owner, SharedChemMaster.BufferSolutionName, out _, out var bufferSolution))
                return true;

            _ksReagentSeparationSystem.SplitSelected(bufferSolution, id, amount, out expanded);
        }
        else
        {
            if (_itemSlotsSystem.GetItemOrNull(chemMaster, SharedChemMaster.InputSlotName) is not { } container ||
                !_solutionContainerSystem.TryGetFitsInDispenser(container, out var containerEntity, out var containerSolution))
                return true;

            _ksReagentSeparationSystem.SplitSelected(containerSolution, id, amount, out expanded);
            _solutionContainerSystem.UpdateChemicals(containerEntity.Value);
        }

        KsPopupGroupMoved(chemMaster, id, expanded);
        UpdateUiState(chemMaster, updateLabel: fromBuffer);
        return true;
    }

    /// <summary>
    ///     Says that more than the selected reagent moved, so the player knows why.
    /// </summary>
    private void KsPopupGroupMoved(Entity<ChemMasterComponent> chemMaster, ReagentId id, bool expanded)
    {
        if (!expanded || !_ksReagentSeparationSystem.TryGetGroup(id.Prototype, out var group))
            return;

        _popupSystem.PopupEntity(
            KsPopupMessage.Create("ks-reagent-inseparable-group-moved", ("group", KsPopupMessage.Create(ProtoMan.Index(group.Value).Name))),
            chemMaster.Owner);
    }
}
