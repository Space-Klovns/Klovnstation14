using Content.Shared._KS14.Chemistry.Separation;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Server._Starlight.Plumbing.EntitySystems;

public sealed partial class PlumbingSmartFridgeSystem
{
    [Dependency] private KsReagentSeparationSystem _ksReagentSeparationSystem = default!;

    /// <summary>
    ///     Fills a jug with an inseparable reagent's whole group, in proportion, as far as the group and the jug allow.
    ///         The labelled reagent's own stock doesn't limit it, and the popup names the group, as that's what moved.
    /// </summary>
    private void KsDispenseGroup(
        Entity<SolutionComponent> fridgeEntity,
        Solution fridgeSolution,
        Entity<SolutionComponent> jugEntity,
        ProtoId<KsReagentSeparationGroupPrototype> group,
        FixedPoint2 jugAvailable,
        EntityUid fridgeUid,
        EntityUid userUid)
    {
        var amount = FixedPoint2.Min(_ksReagentSeparationSystem.GroupQuantity(fridgeSolution, group), jugAvailable);
        var moved = _ksReagentSeparationSystem.SplitGroup(fridgeSolution, group, amount);
        _solutionSystem.UpdateChemicals(fridgeEntity);
        if (moved.Volume <= FixedPoint2.Zero)
            return;

        _solutionSystem.TryAddSolution(jugEntity, moved);
        _popup.PopupEntity(KsPopupMessage.Create("plumbing-smart-fridge-filled",
                ("reagent", KsPopupMessage.Create(ProtoMan.Index(group).Name)),
                ("amount", moved.Volume)),
            fridgeUid, userUid);
    }
}
