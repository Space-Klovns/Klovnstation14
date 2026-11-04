using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Collections;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Chemistry.Separation;

/// <summary>
///     Keeps machines from picking apart a <see cref="KsReagentSeparationGroupPrototype"/>: whatever selects one member of
///         a group takes every member present, in proportion, so the mixture leaves as it was. Nothing is refused, so oil
///         never strands in a tank and a filter never deadlocks; only physical separation splits the group.
/// </summary>
public sealed partial class KsReagentSeparationSystem : EntitySystem
{
    private FrozenDictionary<string, ProtoId<KsReagentSeparationGroupPrototype>> _groupOf =
        FrozenDictionary<string, ProtoId<KsReagentSeparationGroupPrototype>>.Empty;

    private FrozenDictionary<ProtoId<KsReagentSeparationGroupPrototype>, ProtoId<ReagentPrototype>[]> _members =
        FrozenDictionary<ProtoId<KsReagentSeparationGroupPrototype>, ProtoId<ReagentPrototype>[]>.Empty;

    public override void Initialize()
    {
        base.Initialize();

        Rebuild();
    }

    [SubscribeLocalEvent]
    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<ReagentPrototype>())
            Rebuild();
    }

    private void Rebuild()
    {
        var groupOf = new Dictionary<string, ProtoId<KsReagentSeparationGroupPrototype>>();
        var members = new Dictionary<ProtoId<KsReagentSeparationGroupPrototype>, List<ProtoId<ReagentPrototype>>>();
        foreach (var reagent in ProtoMan.EnumeratePrototypes<ReagentPrototype>())
        {
            if (reagent.KsInseparableGroup is not { } group)
                continue;

            groupOf[reagent.ID] = group;
            if (!members.TryGetValue(group, out var list))
                members[group] = list = new List<ProtoId<ReagentPrototype>>();

            list.Add(reagent.ID);
        }

        _groupOf = groupOf.ToFrozenDictionary();
        _members = members.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    }

    public bool TryGetGroup(string reagent, [NotNullWhen(true)] out ProtoId<KsReagentSeparationGroupPrototype>? group)
    {
        if (_groupOf.TryGetValue(reagent, out var found))
        {
            group = found;
            return true;
        }

        group = null;
        return false;
    }

    public bool IsInseparable(string reagent)
    {
        return _groupOf.ContainsKey(reagent);
    }

    /// <summary>
    ///     Whether <paramref name="reagent"/> is in the same group as any of <paramref name="others"/>, itself included.
    /// </summary>
    public bool SharesGroupWithAny(string reagent, IEnumerable<ProtoId<ReagentPrototype>> others)
    {
        if (!_groupOf.TryGetValue(reagent, out var group))
            return false;

        foreach (var other in others)
        {
            if (_groupOf.TryGetValue(other, out var otherGroup) && otherGroup == group)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Units of every member of <paramref name="group"/> in <paramref name="solution"/>, every data variant included.
    /// </summary>
    public FixedPoint2 GroupQuantity(Solution solution, ProtoId<KsReagentSeparationGroupPrototype> group)
    {
        var total = FixedPoint2.Zero;
        foreach (var quantity in solution.Contents)
        {
            if (_groupOf.TryGetValue(quantity.Reagent.Prototype, out var reagentGroup) && reagentGroup == group)
                total += quantity.Quantity;
        }

        return total;
    }

    /// <summary>
    ///     Takes up to <paramref name="amount"/> of <paramref name="selected"/> out of <paramref name="source"/>, keeping
    ///         its order: just that reagent if it is ordinary, or its whole group in proportion if not.
    /// </summary>
    /// <param name="expanded">Whether anything besides the selected reagent came with it.</param>
    /// <returns>What was taken, for the caller to put somewhere or discard.</returns>
    public Solution SplitSelected(Solution source, ReagentId selected, FixedPoint2 amount, out bool expanded)
    {
        expanded = false;
        if (!_groupOf.TryGetValue(selected.Prototype, out var group))
        {
            var taken = new Solution();
            var removed = source.RemoveReagent(selected, amount, preserveOrder: true);
            if (removed > FixedPoint2.Zero)
                taken.AddReagent(selected, removed);

            return taken;
        }

        var groupTaken = SplitGroup(source, group, amount);
        foreach (var quantity in groupTaken.Contents)
        {
            if (quantity.Reagent != selected)
            {
                expanded = true;
                break;
            }
        }

        return groupTaken;
    }

    /// <summary>
    ///     Takes up to <paramref name="amount"/> of <paramref name="group"/> out of <paramref name="source"/>, keeping its
    ///         order, with each member's share of what is taken matching its share of the group there.
    /// </summary>
    public Solution SplitGroup(Solution source, ProtoId<KsReagentSeparationGroupPrototype> group, FixedPoint2 amount)
    {
        var taken = new Solution();
        var groupTotal = GroupQuantity(source, group);
        amount = FixedPoint2.Min(amount, groupTotal);
        if (amount <= FixedPoint2.Zero)
            return taken;

        var members = new ValueList<ReagentQuantity>();
        var largest = -1;
        foreach (var quantity in source.Contents)
        {
            if (!_groupOf.TryGetValue(quantity.Reagent.Prototype, out var reagentGroup) || reagentGroup != group)
                continue;

            if (largest < 0 || quantity.Quantity > members[largest].Quantity)
                largest = members.Count;

            members.Add(quantity);
        }

        // Each member's share, truncated to what a solution holds; what truncation leaves goes to the largest member.
        var fraction = (float)amount / (float)groupTotal;
        var shares = new FixedPoint2[members.Count];
        var assigned = FixedPoint2.Zero;
        for (var i = 0; i < members.Count; i++)
        {
            shares[i] = FixedPoint2.Min(FixedPoint2.New((float)members[i].Quantity * fraction), members[i].Quantity);
            assigned += shares[i];
        }

        if (largest >= 0)
            shares[largest] = FixedPoint2.Min(shares[largest] + (amount - assigned), members[largest].Quantity);

        for (var i = 0; i < members.Count; i++)
        {
            if (shares[i] <= FixedPoint2.Zero)
                continue;

            var removed = source.RemoveReagent(members[i].Reagent, shares[i], preserveOrder: true);
            if (removed > FixedPoint2.Zero)
                taken.AddReagent(members[i].Reagent, removed);
        }

        return taken;
    }

    /// <summary>
    ///     Every reagent a whitelist of <paramref name="reagents"/> effectively picks: each listed one, and every member of
    ///         its group.
    /// </summary>
    public ProtoId<ReagentPrototype>[] ExpandToGroups(IEnumerable<ProtoId<ReagentPrototype>> reagents)
    {
        var expanded = new HashSet<ProtoId<ReagentPrototype>>();
        foreach (var reagent in reagents)
        {
            expanded.Add(reagent);
            if (!_groupOf.TryGetValue(reagent, out var group))
                continue;

            foreach (var member in _members[group])
            {
                expanded.Add(member);
            }
        }

        var result = new ProtoId<ReagentPrototype>[expanded.Count];
        expanded.CopyTo(result);
        return result;
    }
}
