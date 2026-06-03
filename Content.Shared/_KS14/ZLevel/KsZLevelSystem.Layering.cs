using System.Diagnostics.CodeAnalysis;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.Shared._KS14.ZLevel;

public sealed partial class KsZLevelSystem : EntitySystem
{
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
