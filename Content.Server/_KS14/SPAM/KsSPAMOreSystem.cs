using Content.Shared._KS14.SPAM;
using Content.Shared._KS14.Stacks;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Robust.Shared.Containers;

namespace Content.Server._KS14.SPAM;

/// <summary>
///     Counts ore that enters an ore bag toward the ore tasks of the S.P.A.M. attached to the bag.
///
///     Ore reaches a bag two ways: as its own entity, or as units merged into a stack already inside. Both are counted
///         here, and each unit only once: a stack remembers how many of its units have been counted.
/// </summary>
public sealed partial class KsSPAMOreSystem : EntitySystem
{
    [Dependency] private KsSPAMSystem _spamSystem = default!;
    [Dependency] private ItemSlotsSystem _itemSlotsSystem = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private EntityQuery<StackComponent> _stackQuery = default!;
    [Dependency] private EntityQuery<KsSPAMCountedOreComponent> _countedQuery = default!;
    [Dependency] private EntityQuery<KsSPAMOreBagComponent> _oreBagQuery = default!;
    [Dependency] private EntityQuery<KsSPAMComponent> _spamQuery = default!;
    [Dependency] private EntityQuery<StorageComponent> _storageQuery = default!;

    /// <summary>
    ///     How many units of the stack have been counted already.
    /// </summary>
    public int GetCounted(Entity<StackComponent> stack)
    {
        // Units that were used up took their count with them.
        return _countedQuery.TryComp(stack.Owner, out var countedComponent)
            ? Math.Min(countedComponent.Counted, stack.Comp.Count)
            : 0;
    }

    /// <summary>
    ///     Offers units of ore to the tasks of the tablet attached to the bag.
    /// </summary>
    /// <returns>Whether a task wanted them, which is when they stop being countable.</returns>
    private bool TryCount(Entity<KsSPAMOreBagComponent> bag, StackComponent stackComponent, int amount)
    {
        if (amount <= 0 ||
            _itemSlotsSystem.GetItemOrNull(bag.Owner, bag.Comp.Slot) is not { } tabletUid ||
            !_spamQuery.TryComp(tabletUid, out var spamComponent) ||
            !_spamSystem.TryGetAccount((tabletUid, spamComponent), out var account))
            return false;

        var stackType = stackComponent.StackTypeId;
        return _spamSystem.Advance<KsSPAMOreObjective>(account.AsNullable(), objective => objective.Stack == stackType, amount);
    }

    private bool IsInBagStorage(EntityUid uid, out Entity<KsSPAMOreBagComponent> bag)
    {
        bag = default;
        if (!_containerSystem.TryGetContainingContainer(uid, out var container) ||
            !_oreBagQuery.TryComp(container.Owner, out var oreBagComponent) ||
            !_storageQuery.TryComp(container.Owner, out var storageComponent) ||
            storageComponent.Container != container)
            return false;

        bag = (container.Owner, oreBagComponent);
        return true;
    }

    /// <summary>
    ///     Ore put in the bag as its own entity.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnInserted(Entity<KsSPAMOreBagComponent> entity, ref EntInsertedIntoContainerMessage args)
    {
        if (!_stackQuery.TryComp(args.Entity, out var stackComponent) ||
            !_storageQuery.TryComp(entity.Owner, out var storageComponent) ||
            storageComponent.Container != args.Container)
            return;

        var uncounted = stackComponent.Count - GetCounted((args.Entity, stackComponent));
        if (TryCount(entity, stackComponent, uncounted))
            EnsureComp<KsSPAMCountedOreComponent>(args.Entity).Counted = stackComponent.Count;
    }

    /// <summary>
    ///     Units moving from one stack to another: the counted ones stay counted, and the rest are counted now if they
    ///         are moving into a bag.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnTransfer(Entity<StackComponent> entity, ref KsStackTransferEvent args)
    {
        if (!_stackQuery.TryComp(args.Donor, out var donorStackComponent))
            return;

        // Counted units move first, so a stack can never hand its uncounted ones over twice.
        var movedCounted = Math.Min(GetCounted((args.Donor, donorStackComponent)), args.Amount);
        if (_countedQuery.TryComp(args.Donor, out var donorCountedComponent))
            donorCountedComponent.Counted = Math.Min(donorCountedComponent.Counted, donorStackComponent.Count) - movedCounted;

        var movedUncounted = args.Amount - movedCounted;
        if (movedUncounted > 0 && IsInBagStorage(entity.Owner, out var bag) && TryCount(bag, entity.Comp, movedUncounted))
            movedCounted = args.Amount;

        if (movedCounted <= 0)
            return;

        // Clamped as it is read: the receiving stack may have lost counted units since.
        var recipientCounted = GetCounted(entity);
        EnsureComp<KsSPAMCountedOreComponent>(entity.Owner).Counted = recipientCounted + movedCounted;
    }

    /// <summary>
    ///     A split leaves no way to tell which units went where, so both halves keep as many counted as they can hold.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnSplit(Entity<KsSPAMCountedOreComponent> entity, ref StackSplitEvent args)
    {
        if (!_stackQuery.TryComp(args.NewId, out var newStackComponent))
            return;

        var counted = Math.Min(entity.Comp.Counted, newStackComponent.Count);
        if (counted > 0)
            EnsureComp<KsSPAMCountedOreComponent>(args.NewId).Counted = counted;
    }
}
