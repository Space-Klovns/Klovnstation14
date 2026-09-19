using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared.Maps;
using Robust.Shared.Map;
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
    ///     How much of the eye's scale one z-level of render depth takes away.
    /// </summary>
    public const float DepthScaleStep = 0.075f;

    /// <summary>
    ///     The shallowest a z-level may be.
    /// </summary>
    /// <remarks>
    ///     Transit integration divides by Depth, and the render passes both multiply and accumulate it without
    ///         clamping, so zero would divide by zero and a negative would quietly invert the whole effect.
    /// </remarks>
    public const float MinimumDepth = 0.01f;

    /// <summary>
    ///     The z-level directly above this one, if it has one.
    /// </summary>
    public bool TryGetZLevelAbove(Entity<KsZLevelComponent?> entity, [NotNullWhen(true)] out Entity<KsZLevelComponent>? aboveEntity)
    {
        aboveEntity = null;
        if (!_zLevelQuery.Resolve(ref entity, logMissing: false))
            return false;

        if (entity.Comp!.Node?.Next is not { } aboveNode)
            return false;

        aboveEntity = aboveNode.Value;
        return true;
    }

    /// <summary>
    ///     Whether the z-level floor plane on this map is solid at this world position.
    /// </summary>
    /// <remarks>
    ///     Deliberately spatial rather than reading <see cref="TransformComponent.GridUid"/>: a crossing
    ///         re-parents the entity, so the cached grid is only correct once the crossing is already done.
    ///     This is the one definition of "is there floor here" - what an entity falls through, and what a
    ///         sound has to find a gap in.
    /// </remarks>
    public bool IsFloorSolidAt(MapId mapId, Vector2 worldPosition)
    {
        // Open space: nothing to stand on, and nothing to bump into.
        if (!_mapSystem.TryFindGridAt(mapId, worldPosition, out var gridUid, out var mapGridComponent))
            return false;

        return !_mapSystem.GetTileRef((gridUid, mapGridComponent), new MapCoordinates(worldPosition, mapId)).Tile.IsEmpty;
    }

    /// <summary>
    ///     Whether light and sound from a neighbouring z-level carry through the floor plane on this map at
    ///         this world position.
    /// </summary>
    /// <remarks>
    ///     Not the inverse of <see cref="IsFloorSolidAt"/>: a grating is solid enough to stand on and still
    ///         lets both through. Falling asks the other question.
    /// </remarks>
    public bool IsFloorTransparentAt(MapId mapId, Vector2 worldPosition)
    {
        if (!_mapSystem.TryFindGridAt(mapId, worldPosition, out var gridUid, out var mapGridComponent))
            return true;

        var tileRef = _mapSystem.GetTileRef((gridUid, mapGridComponent), new MapCoordinates(worldPosition, mapId));
        return IsTileTransparent(tileRef.Tile);
    }

    /// <summary>
    ///     Whether light and sound carry through this tile, for a caller that already has one in hand.
    /// </summary>
    public bool IsTileTransparent(Tile tile)
    {
        if (tile.IsEmpty)
            return true;

        return _tileDefinitionManager[tile.TypeId] is ContentTileDefinition { KsZLevelTransparent: true };
    }

    /// <summary>
    ///     Sets how far a z-level sits below the one above it, at runtime.
    /// </summary>
    /// <remarks>
    ///     Depth is networked as part of the z-level's own state, so only the one being changed needs dirtying -
    ///         unlike stack membership, which every member replicates.
    /// </remarks>
    /// <returns>Whether the depth actually changed.</returns>
    public bool SetDepth(Entity<KsZLevelComponent?> entity, float depth)
    {
        if (!_zLevelQuery.Resolve(ref entity))
            return false;

        depth = MathF.Max(depth, MinimumDepth);

        var previousDepth = entity.Comp!.Depth;
        if (MathHelper.CloseTo(previousDepth, depth))
            return false;

        entity.Comp.Depth = depth;
        Dirty(entity!);

        var depthChangedEvent = new KsZLevelDepthChangedEvent(previousDepth, depth);
        RaiseLocalEvent(entity.Owner, ref depthChangedEvent);

        return true;
    }

    /// <summary>
    ///     The eye scale a z-level sitting <paramref name="depth"/> z-levels below the viewer is rendered at.
    /// </summary>
    /// <remarks>
    ///     Shared so that anything compensating for the per-z-level camera scale - a transiting entity's sprite,
    ///         say - cannot drift out of step with what actually rendered it.
    /// </remarks>
    public static Vector2 GetDepthScale(Vector2 eyeScale, float depth)
    {
        var shrink = DepthScaleStep * depth;
        return eyeScale - new Vector2(shrink, shrink);
    }

    /// <summary>
    ///     How far below <paramref name="fromEntity"/>'s floor plane <paramref name="toUid"/>'s floor plane
    ///         sits, in z-levels, summing the Depth of every z-level stepped down through.
    /// </summary>
    /// <returns>
    ///     False if the two are not in the same stack, or if <paramref name="toUid"/> is above
    ///         <paramref name="fromEntity"/>. Zero and true if they are the same z-level.
    /// </returns>
    public bool TryGetDepthBelow(Entity<KsZLevelComponent?> fromEntity, EntityUid toUid, out float depth)
    {
        return TryGetDepthBelow(fromEntity, toUid, out depth, out _);
    }

    /// <summary>
    ///     How far below <paramref name="fromEntity"/>'s floor plane <paramref name="toUid"/>'s floor plane
    ///         sits, in z-levels, and how many floor planes lie between the two.
    /// </summary>
    /// <param name="crossings">
    ///     How many floor planes separate them - one for the z-level directly below, and one more for each
    ///         step past that. Zero when they are the same z-level.
    /// </param>
    /// <returns>
    ///     False if the two are not in the same stack, or if <paramref name="toUid"/> is above
    ///         <paramref name="fromEntity"/>. Zero and true if they are the same z-level.
    /// </returns>
    /// <remarks>
    ///     Allocates nothing and touches no shared state, so it is safe to call from the parallel jobs the
    ///         audio system runs its streams on.
    /// </remarks>
    public bool TryGetDepthBelow(Entity<KsZLevelComponent?> fromEntity, EntityUid toUid, out float depth, out int crossings)
    {
        depth = 0f;
        crossings = 0;

        if (!_zLevelQuery.Resolve(ref fromEntity, logMissing: false))
            return false;

        if (fromEntity.Owner == toUid)
            return true;

        // Stepping down from a z-level to the one below crosses the lower one's own Depth.
        for (var node = fromEntity.Comp!.Node?.Previous; node != null; node = node.Previous)
        {
            depth += node.Value.Comp.Depth;
            crossings++;

            if (node.Value.Owner == toUid)
                return true;
        }

        depth = 0f;
        crossings = 0;
        return false;
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
