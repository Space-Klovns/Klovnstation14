using System.Diagnostics.CodeAnalysis;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.Shared._KS14.ZLevel;

public sealed partial class KsZLevelSystem : EntitySystem
{
    /// <summary>
    ///     Gets the z-level stack a map is part of, if any.
    /// </summary>
    public bool TryGetStackFromMap(Entity<KsZLevelComponent?> entity, [MaybeNullWhen(false)] out KsZLevelStack stack)
    {
        DebugTools.Assert(!HasComp<MapGridComponent>(entity), "`TryGetStackFromMap` was run on a non-map entity, did you mean to use TryGetStackFromDescendant instead?");

        if (!_zLevelQuery.Resolve(ref entity, logMissing: false))
        {
            stack = null;
            return false;
        }

        stack = entity.Comp!.AssociatedStack;
        return true;
    }

    /// <summary>
    ///     Gets the z-level stack of the map that the entity is on, if any.
    /// </summary>
    /// <param name="zLevelEntity">Only valid if <see langword="true"/> is returned.</param>
    public bool TryGetStackFromDescendant(Entity<TransformComponent?> entity, [MaybeNullWhen(false)] out Entity<KsZLevelComponent> zLevelEntity, [MaybeNullWhen(false)] out KsZLevelStack stack)
    {
        DebugTools.Assert(!HasComp<MapGridComponent>(entity), "`TryGetStackFromDescendant` was run on a map entity, did you mean to use TryGetStackFromMap instead?");

        if (!EntityManager.TransformQuery.Resolve(ref entity, logMissing: true) ||
            entity.Comp!.MapUid is not { } mapUid ||
            !_zLevelQuery.TryGetComponent(mapUid, out var zLevelComponent))
        {
            zLevelEntity = default;
            stack = null;
            return false;
        }

        zLevelEntity = (mapUid, zLevelComponent);
        stack = zLevelComponent.AssociatedStack;
        return true;
    }

    /// <summary>
    ///     Sets a z-level to be directly under another.
    ///         Any z-levels adjacent to the added one before it is added
    ///         will not be moved.
    /// </summary>
    public void AddZLevelDirectlyUnder(Entity<KsZLevelComponent?> targetEntity, Entity<KsZLevelComponent?> addedEntity)
    {
        if (!_zLevelQuery.Resolve(ref targetEntity) ||
            !_zLevelQuery.Resolve(ref addedEntity))
            return;

        var stack = targetEntity.Comp!.AssociatedStack;
        var underNode = stack.AddAfter(stack.Find(targetEntity!)!, addedEntity!);

        // Migrate addedEntity from its stack to the new stack
        RemoveFromOwnStack(addedEntity!);
        addedEntity.Comp!.AssociatedStack = stack;
        addedEntity.Comp!.Node = underNode;

        Dirty(targetEntity);
        Dirty(addedEntity);
    }

    /// <summary>
    ///     Sets a z-level to be directly above another.
    ///         Any z-levels adjacent to the added one before it is added
    ///         will not be moved.
    /// </summary>
    public void AddZLevelDirectlyAbove(Entity<KsZLevelComponent?> targetEntity, Entity<KsZLevelComponent?> addedEntity)
    {
        if (!_zLevelQuery.Resolve(ref targetEntity) ||
            !_zLevelQuery.Resolve(ref addedEntity))
            return;

        var stack = targetEntity.Comp!.AssociatedStack;
        var afterNode = stack.AddAfter(stack.Find(targetEntity!)!, addedEntity!);

        // Migrate addedEntity from its stack to the new stack
        RemoveFromOwnStack(addedEntity!);
        addedEntity.Comp!.AssociatedStack = stack;
        addedEntity.Comp!.Node = afterNode;

        Dirty(targetEntity);
        Dirty(addedEntity);
    }

    /// <summary>
    ///     Sets a z-level to be under an entire z-level stack.
    ///         Any z-levels adjacent to the added one before it is added
    ///         will not be moved.
    /// </summary>
    public void AddZLevelUnderStack(Entity<KsZLevelComponent?> targetEntity, Entity<KsZLevelComponent?> addedEntity)
    {
        if (!_zLevelQuery.Resolve(ref targetEntity) ||
            !_zLevelQuery.Resolve(ref addedEntity))
            return;

        var stack = targetEntity.Comp!.AssociatedStack;
        var firstNode = stack.AddFirst(addedEntity!);

        // Migrate addedEntity from its stack to the new stack
        RemoveFromOwnStack(addedEntity!);
        addedEntity.Comp!.AssociatedStack = stack;
        addedEntity.Comp!.Node = firstNode;

        Dirty(targetEntity);
        Dirty(addedEntity);
    }

    /// <summary>
    ///     Sets a z-level to be above an entire z-level stack.
    ///         Any z-levels adjacent to the added one before it is added
    ///         will not be moved.
    /// </summary>
    public void AddZLevelAboveStack(Entity<KsZLevelComponent?> targetEntity, Entity<KsZLevelComponent?> addedEntity)
    {
        if (!_zLevelQuery.Resolve(ref targetEntity) ||
            !_zLevelQuery.Resolve(ref addedEntity))
            return;

        var stack = targetEntity.Comp!.AssociatedStack;
        var lastNode = stack.AddLast(addedEntity!);

        // Migrate addedEntity from its stack to the new stack
        RemoveFromOwnStack(addedEntity!);
        addedEntity.Comp!.AssociatedStack = stack;
        addedEntity.Comp!.Node = lastNode;

        Dirty(targetEntity);
        Dirty(addedEntity);
    }
}
