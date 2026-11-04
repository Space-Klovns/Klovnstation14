using Content.Shared._KS14.Chemistry.Separation;
using Content.Shared._Starlight.Plumbing.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Server._Starlight.Plumbing.EntitySystems;

/// <summary>
///     A reactor's targets bring their inseparable groups with them, and those groups can't be picked apart on the way out.
///         So a group leaves whole, or it would pile up in the buffer until a target no longer fit.
/// </summary>
public sealed partial class PlumbingReactorSystem
{
    private readonly HashSet<ProtoId<KsReagentSeparationGroupPrototype>> _ksFlushedGroups = new();

    /// <summary>
    ///     Sends the groups of <paramref name="targets"/> from the buffer to the output, as far as the output has room.
    /// </summary>
    private void KsFlushGroups(
        Entity<SolutionComponent> bufferEntity,
        Solution buffer,
        Entity<SolutionComponent> outputEntity,
        IEnumerable<ProtoId<ReagentPrototype>> targets)
    {
        _ksFlushedGroups.Clear();
        foreach (var target in targets)
        {
            if (!_ksReagentSeparationSystem.TryGetGroup(target, out var group) || !_ksFlushedGroups.Add(group.Value))
                continue;

            var moved = _ksReagentSeparationSystem.SplitGroup(buffer, group.Value, outputEntity.Comp.Solution.AvailableVolume);
            if (moved.Volume <= FixedPoint2.Zero)
                continue;

            _solutionSystem.UpdateChemicals(bufferEntity);
            _solutionSystem.TryAddSolution(outputEntity, moved);
        }
    }

    /// <summary>
    ///     With the buffer full and a target still short, nothing more can arrive, so the group passes through unreacted
    ///         rather than leaving the reactor stuck.
    /// </summary>
    private void KsFlushGroupsIfTargetsCannotFit(
        Entity<PlumbingReactorComponent> entity,
        Entity<SolutionComponent> bufferEntity,
        Solution buffer,
        Entity<SolutionComponent> outputEntity)
    {
        if (buffer.AvailableVolume > FixedPoint2.Zero)
            return;

        foreach (var (target, amount) in entity.Comp.ReagentTargets)
        {
            if (buffer.GetTotalPrototypeQuantity(target.Id) >= amount)
                continue;

            KsFlushGroups(bufferEntity, buffer, outputEntity, entity.Comp.ReagentTargets.Keys);
            return;
        }
    }
}
