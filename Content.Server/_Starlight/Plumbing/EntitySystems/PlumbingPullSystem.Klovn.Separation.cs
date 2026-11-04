using Content.Shared._KS14.Chemistry.Separation;
using Content.Shared._Starlight.Plumbing.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Server._Starlight.Plumbing.EntitySystems;

/// <summary>
///     Plumbing never picks an inseparable reagent group apart: a group is pulled whole and in proportion, wherever any
///         of its members may go.
/// </summary>
public sealed partial class PlumbingPullSystem
{
    [Dependency] private KsReagentSeparationSystem _ksReagentSeparationSystem = default!;

    /// <summary>
    ///     The groups already pulled during one <see cref="PullFromOutlet"/>.
    /// </summary>
    private readonly HashSet<ProtoId<KsReagentSeparationGroupPrototype>> _ksPulledGroups = new();

    /// <summary>
    ///     Allows every member of a group with any allowed member, so a filter's outlets each carry the whole group.
    /// </summary>
    private void KsAllowWholeGroups(Solution source, List<(ReagentId Reagent, FixedPoint2 Quantity)> allowed)
    {
        _ksPulledGroups.Clear();
        foreach (var (reagent, _) in allowed)
        {
            if (_ksReagentSeparationSystem.TryGetGroup(reagent.Prototype, out var group))
                _ksPulledGroups.Add(group.Value);
        }

        if (_ksPulledGroups.Count == 0)
            return;

        foreach (var quantity in source.Contents)
        {
            if (!_ksReagentSeparationSystem.TryGetGroup(quantity.Reagent.Prototype, out var group) || !_ksPulledGroups.Contains(group.Value))
                continue;

            var listed = false;
            foreach (var (reagent, _) in allowed)
            {
                if (reagent == quantity.Reagent)
                {
                    listed = true;
                    break;
                }
            }

            if (!listed)
                allowed.Add((quantity.Reagent, quantity.Quantity));
        }

        _ksPulledGroups.Clear();
    }

    /// <summary>
    ///     Pulls up to <paramref name="maxAmount"/> of a group into <paramref name="destination"/>, in proportion. The
    ///         puller may cap any member's share, which slows the whole group, or refuse one, which stops it.
    /// </summary>
    /// <returns>What arrived.</returns>
    private Solution KsPullGroup(
        EntityUid puller,
        EntityUid sourceOwner,
        Entity<SolutionComponent> sourceSoln,
        Entity<SolutionComponent> destination,
        ProtoId<KsReagentSeparationGroupPrototype> group,
        FixedPoint2 maxAmount)
    {
        var source = sourceSoln.Comp.Solution;
        var groupTotal = _ksReagentSeparationSystem.GroupQuantity(source, group);
        maxAmount = FixedPoint2.Min(maxAmount, destination.Comp.Solution.AvailableVolume);
        if (groupTotal <= FixedPoint2.Zero || maxAmount <= FixedPoint2.Zero)
            return new Solution();

        var fraction = MathF.Min(1f, (float)maxAmount / (float)groupTotal);
        foreach (var quantity in source.Contents)
        {
            if (!_ksReagentSeparationSystem.TryGetGroup(quantity.Reagent.Prototype, out var reagentGroup) || reagentGroup != group)
                continue;

            var share = FixedPoint2.New((float)quantity.Quantity * fraction);
            var intoEv = new PlumbingPullIntoAttemptEvent(sourceOwner, quantity.Reagent.Prototype, share);
            RaiseLocalEvent(puller, ref intoEv);

            if (intoEv.Cancelled)
                return new Solution();

            if (intoEv.MaxAllowed < share && quantity.Quantity > FixedPoint2.Zero)
                fraction = MathF.Min(fraction, (float)intoEv.MaxAllowed / (float)quantity.Quantity);
        }

        var moved = _ksReagentSeparationSystem.SplitGroup(source, group, FixedPoint2.New((float)groupTotal * fraction));
        if (moved.Volume <= FixedPoint2.Zero)
            return moved;

        _solutionSystem.UpdateChemicals(sourceSoln);
        if (!_solutionSystem.TryAddSolution(destination, moved))
        {
            // Never lose it: what does not fit goes back.
            _solutionSystem.TryAddSolution(sourceSoln, moved);
            return new Solution();
        }

        return moved;
    }

    /// <summary>
    ///     Pulls an inseparable target's group, in proportion, enough that the target itself arrives as needed.
    /// </summary>
    /// <returns>How much of the target arrived, and how much in all.</returns>
    private (FixedPoint2 Target, FixedPoint2 Total) KsPullGroupForTarget(
        EntityUid puller,
        EntityUid sourceOwner,
        Entity<SolutionComponent> sourceSoln,
        Entity<SolutionComponent> destination,
        ProtoId<KsReagentSeparationGroupPrototype> group,
        string target,
        FixedPoint2 targetNeeded,
        FixedPoint2 remaining,
        Dictionary<string, FixedPoint2> pulled)
    {
        var source = sourceSoln.Comp.Solution;
        var targetAvailable = source.GetTotalPrototypeQuantity(target);
        var groupTotal = _ksReagentSeparationSystem.GroupQuantity(source, group);
        if (targetAvailable <= FixedPoint2.Zero || groupTotal <= FixedPoint2.Zero)
            return (FixedPoint2.Zero, FixedPoint2.Zero);

        var bundle = FixedPoint2.Min(remaining, FixedPoint2.New((float)targetNeeded * (float)groupTotal / (float)targetAvailable));
        var moved = KsPullGroup(puller, sourceOwner, sourceSoln, destination, group, bundle);

        foreach (var quantity in moved.Contents)
        {
            pulled[quantity.Reagent.Prototype] = pulled.GetValueOrDefault(quantity.Reagent.Prototype, FixedPoint2.Zero) + quantity.Quantity;
        }

        return (moved.GetTotalPrototypeQuantity(target), moved.Volume);
    }
}
