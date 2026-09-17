using System.Diagnostics.CodeAnalysis;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.Shared._KS14.ZLevel;

public sealed partial class KsZLevelSystem : EntitySystem
{
    /// <summary>
    ///     Where a z-level is being inserted relative to the target, for <see cref="TryInsertIntoStack"/>.
    /// </summary>
    private enum KsZLevelInsertion : byte
    {
        DirectlyUnder,
        DirectlyAbove,
        UnderStack,
        AboveStack,
    }

    /// <summary>
    ///     Tries to get the get the z-level (map) that the entity is on, if any.
    /// </summary>
    /// <param name="zLevelEntity">Only valid if <see langword="true"/> is returned.</param>
    public bool TryGetZLevel(Entity<TransformComponent?> entity, [NotNullWhen(true)] out Entity<KsZLevelComponent>? zLevelEntity)
    {
        DebugTools.Assert(!HasComp<MapComponent>(entity), "`TryGetZLevel` was run on a map entity, however it is only for children of that map entity");

        if (!EntityManager.TransformQuery.Resolve(ref entity, logMissing: true) ||
            entity.Comp!.MapUid is not { } mapUid ||
            !_zLevelQuery.TryGetComponent(mapUid, out var zLevelComponent))
        {
            zLevelEntity = null;
            return false;
        }

        zLevelEntity = (mapUid, zLevelComponent);
        return true;
    }

    /// <summary>
    ///     Gets the z-level entity that the entity is on. Will <see langword="throw"/>  if there is none.
    /// </summary>
    public Entity<KsZLevelComponent> GetZLevel(Entity<TransformComponent?> entity)
    {
        // Throw if necessary
        var transformComponent = entity.Comp ?? Transform(entity);
        var mapUid = transformComponent.MapUid;

        return (mapUid!.Value, _zLevelQuery.GetComponent(mapUid.Value));
    }

    /// <summary>
    ///     Sets a z-level to be directly under another.
    ///         Any z-levels adjacent to the added one before it is added
    ///         will not be moved.
    /// </summary>
    /// <returns>Whether the z-level was added.</returns>
    public bool AddZLevelDirectlyUnder(Entity<KsZLevelComponent?> targetEntity, Entity<KsZLevelComponent?> addedEntity)
    {
        return TryInsertIntoStack(targetEntity, addedEntity, KsZLevelInsertion.DirectlyUnder);
    }

    /// <summary>
    ///     Sets a z-level to be directly above another.
    ///         Any z-levels adjacent to the added one before it is added
    ///         will not be moved.
    /// </summary>
    /// <returns>Whether the z-level was added.</returns>
    public bool AddZLevelDirectlyAbove(Entity<KsZLevelComponent?> targetEntity, Entity<KsZLevelComponent?> addedEntity)
    {
        return TryInsertIntoStack(targetEntity, addedEntity, KsZLevelInsertion.DirectlyAbove);
    }

    /// <summary>
    ///     Sets a z-level to be under an entire z-level stack.
    ///         Any z-levels adjacent to the added one before it is added
    ///         will not be moved.
    /// </summary>
    /// <returns>Whether the z-level was added.</returns>
    public bool AddZLevelUnderStack(Entity<KsZLevelComponent?> targetEntity, Entity<KsZLevelComponent?> addedEntity)
    {
        return TryInsertIntoStack(targetEntity, addedEntity, KsZLevelInsertion.UnderStack);
    }

    /// <summary>
    ///     Sets a z-level to be above an entire z-level stack.
    ///         Any z-levels adjacent to the added one before it is added
    ///         will not be moved.
    /// </summary>
    /// <returns>Whether the z-level was added.</returns>
    public bool AddZLevelAboveStack(Entity<KsZLevelComponent?> targetEntity, Entity<KsZLevelComponent?> addedEntity)
    {
        return TryInsertIntoStack(targetEntity, addedEntity, KsZLevelInsertion.AboveStack);
    }

    /// <summary>
    ///     The single place a z-level stack is actually mutated: migrates <paramref name="addedEntity"/> out of
    ///         its own stack and into <paramref name="targetEntity"/>'s, at the requested position.
    /// </summary>
    /// <returns>Whether the z-level was added.</returns>
    private bool TryInsertIntoStack(
        Entity<KsZLevelComponent?> targetEntity,
        Entity<KsZLevelComponent?> addedEntity,
        KsZLevelInsertion insertion)
    {
        if (!_zLevelQuery.Resolve(ref targetEntity) ||
            !_zLevelQuery.Resolve(ref addedEntity))
            return false;

        // A z-level is in exactly one stack, so adding it to a stack it is already in would have it migrate out
        //      of and back into the same list - losing it, or duplicating it, depending on the order. Adding one
        //      to itself is the degenerate case of the same thing.
        if (targetEntity.Owner == addedEntity.Owner)
        {
            Log.Error($"Tried to add z-level {ToPrettyString(addedEntity.Owner)} to a stack relative to itself.");
            return false;
        }

        var stack = targetEntity.Comp!.AssociatedStack;
        if (ReferenceEquals(addedEntity.Comp!.AssociatedStack, stack) || stack.Contains(addedEntity!))
        {
            Log.Error($"Tried to add z-level {ToPrettyString(addedEntity.Owner)} to a stack it is already part of, via {ToPrettyString(targetEntity.Owner)}.");
            return false;
        }

        // Migrate out first, so the removal can never touch the node that is about to be inserted.
        var departedStack = addedEntity.Comp!.AssociatedStack;
        RemoveFromOwnStack(addedEntity!);

        var node = insertion switch
        {
            // The stack is ascending, so First is the bottom-most z-level and AddAfter means "above".
            KsZLevelInsertion.DirectlyUnder => stack.AddBefore(stack.Find(targetEntity!)!, addedEntity!),
            KsZLevelInsertion.DirectlyAbove => stack.AddAfter(stack.Find(targetEntity!)!, addedEntity!),
            KsZLevelInsertion.UnderStack => stack.AddFirst(addedEntity!),
            KsZLevelInsertion.AboveStack => stack.AddLast(addedEntity!),
            _ => throw new ArgumentOutOfRangeException(nameof(insertion), insertion, null),
        };

        addedEntity.Comp!.AssociatedStack = stack;
        addedEntity.Comp!.Node = node;

        // Every z-level replicates the whole stack, so every member of both stacks now has a stale state.
        foreach (var memberEntity in stack)
            Dirty(memberEntity);

        foreach (var departedEntity in departedStack)
            Dirty(departedEntity);

        return true;
    }
}
