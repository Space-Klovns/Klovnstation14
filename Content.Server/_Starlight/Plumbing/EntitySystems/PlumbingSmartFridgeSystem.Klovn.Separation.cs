using Content.Shared._KS14.Chemistry.Separation;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;

namespace Content.Server._Starlight.Plumbing.EntitySystems;

public sealed partial class PlumbingSmartFridgeSystem
{
    [Dependency] private KsReagentSeparationSystem _ksReagentSeparationSystem = default!;

    /// <summary>
    ///     Fills a jug with an inseparable reagent's whole group, in proportion, rather than just the labelled reagent.
    /// </summary>
    private void KsDispenseGroup(
        Entity<SolutionComponent> fridgeEntity,
        Solution fridgeSolution,
        Entity<SolutionComponent> jugEntity,
        ReagentId reagent,
        FixedPoint2 amount,
        string reagentId,
        EntityUid fridgeUid,
        EntityUid userUid)
    {
        var moved = _ksReagentSeparationSystem.SplitSelected(fridgeSolution, reagent, amount, out _);
        _solutionSystem.UpdateChemicals(fridgeEntity);
        if (moved.Volume <= FixedPoint2.Zero)
            return;

        _solutionSystem.TryAddSolution(jugEntity, moved);
        _popup.PopupEntity(KsPopupMessage.Create("plumbing-smart-fridge-filled",
                ("reagent", ProtoMan.Index<ReagentPrototype>(reagentId).LocalizedName),
                ("amount", moved.Volume)),
            fridgeUid, userUid);
    }
}
