using System.Numerics;
using Content.Shared.Database;
using Content.Shared.Physics;
using Robust.Shared.Collections;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Utility;

namespace Content.Shared._KS14.FieldGenerator;

public sealed partial class KsFieldGeneratorSystem : EntitySystem
{
    [Dependency] private RayCastSystem _rayCastSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;

    /// <summary>
    ///     Cache of every grid, and a dictionary of anchored generators on that grid
    ///         as well as their tile coordinates. Server-side only: anchoring is not tracked on the client.
    /// </summary>
    private readonly Dictionary<EntityUid, Dictionary<Vector2i, HashSet<EntityUid>>> _tileCache = [];

    private void InitialiseLinking()
    {
        // Conditional, so not attribute subscriptions: the tile cache is the server's
        if (_netManager.IsServer)
        {
            SubscribeLocalEvent<KsFieldGeneratorComponent, AnchorStateChangedEvent>(OnAnchorStateChanged);
            SubscribeLocalEvent<KsFieldGeneratorComponent, ReAnchorEvent>(OnReAnchor);
        }
    }

    private void ClearFields(Entity<KsFieldGeneratorComponent> entity)
    {
        var fieldUids = entity.Comp.FieldUids;
        if (fieldUids.Count == 0)
            return;

        foreach (var fieldUid in fieldUids)
        {
            // Fields can already be deleted when this runs during entity flushes.
            if (!_fieldQuery.TryGetComponent(fieldUid, out var fieldComponent))
                continue;

            fieldComponent.GeneratorUids.Clear();

            PredictedQueueDel(fieldUid);
        }

        fieldUids.Clear();
    }

    private void UnlinkAndClearFields(Entity<KsFieldGeneratorComponent> entity, EntityUid? linkedUid = null)
    {
        ClearFields(entity);

        // The linked generator may already be deleted (notably while a map full of
        //     generators is being flushed), so never assume the component still exists.
        linkedUid ??= entity.Comp.LinkedGeneratorUid;
        if (linkedUid is { } &&
            TryComp<KsFieldGeneratorComponent>(linkedUid.Value, out var linkedGeneratorComponent))
        {
            linkedGeneratorComponent.LinkedGeneratorUid = null;
            DirtyField(linkedUid.Value, linkedGeneratorComponent, nameof(linkedGeneratorComponent.LinkedGeneratorUid));

            linkedGeneratorComponent.FieldUids.Clear();
        }

        entity.Comp.LinkedGeneratorUid = null;
        DirtyField(entity.Owner, entity.Comp, nameof(entity.Comp.LinkedGeneratorUid));
    }

    private void GenerateFields(Entity<KsFieldGeneratorComponent> entity)
    {
        var transformComponent = Transform(entity);

        var fallbackParentUid = transformComponent.GridUid ?? transformComponent.ParentUid;
        if (!TryComp<MapGridComponent>(fallbackParentUid, out var mapGridComponent) ||
            !_tileCache.TryGetValue(fallbackParentUid, out var gridEntTileCache))
            return;

        var originTileCoordinates = (Vector2)_mapSystem.CoordinatesToTile(fallbackParentUid, mapGridComponent, transformComponent.Coordinates);

        var directionAngle = transformComponent.LocalRotation.RoundToCardinalAngle();
        var directionDir = directionAngle.GetDir();
        var direction = directionAngle.ToWorldVec();

        var rayResult = new RayResult();
        _rayCastSystem.CastRayClosest(
            fallbackParentUid,
            ref rayResult,
            originTileCoordinates,
            direction * entity.Comp.Range,
            new QueryFilter() { LayerBits = 0L, Flags = QueryFlags.Static, MaskBits = (long)CollisionGroup.Impassable }
        );

        var distance = entity.Comp.Range;
        if (rayResult.Results.Count != 0)
            distance *= rayResult.Results[0].Fraction;

        var tiles = new ValueList<Vector2i>();

        // Distance in tiles from the hit thing
        var tileDistance = (int)Math.Round(distance / mapGridComponent.TileSize);
        for (var iteratedDistance = 0; iteratedDistance <= tileDistance; ++iteratedDistance)
        {
            var tileCoordinates = (Vector2i)(originTileCoordinates + iteratedDistance * direction);

            tiles.Add(tileCoordinates);
            if (!gridEntTileCache.TryGetValue(tileCoordinates, out var generatorsOnTile) ||
                generatorsOnTile.Count == 0)
                continue;

            foreach (var otherGeneratorUid in generatorsOnTile)
            {
                if (otherGeneratorUid == entity.Owner)
                    continue;

                // Make sure the other generator is facing us
                var otherTransformComponent = Transform(otherGeneratorUid);
                if (directionDir.GetOpposite() != otherTransformComponent.LocalRotation.GetDir())
                    continue;

                if (TryLinkGenerators((entity, entity.Comp, transformComponent), (otherGeneratorUid, null, otherTransformComponent), tiles, (fallbackParentUid, mapGridComponent)))
                    return;
            }
        }
    }

    public bool TryLinkGenerators(Entity<KsFieldGeneratorComponent, TransformComponent> firstGeneratorEntity, Entity<KsFieldGeneratorComponent?, TransformComponent?> secondGeneratorEntity, in ValueList<Vector2i> tiles, Entity<MapGridComponent> gridEntity)
    {
        // Only two currently unlinked generators may link. LinkGenerators overwrites
        //     LinkedGeneratorUid on both sides, so linking an already-linked generator
        //     would orphan its old partner with a stale one-way reference.
        if (!Resolve(secondGeneratorEntity.Owner, ref secondGeneratorEntity.Comp1) ||
            firstGeneratorEntity.Comp1.LinkedGeneratorUid is { } ||
            secondGeneratorEntity.Comp1.LinkedGeneratorUid is { } ||
            !secondGeneratorEntity.Comp1.Enabled ||
            !CanGeneratorWork(secondGeneratorEntity!, transformComponent: secondGeneratorEntity.Comp2))
            return false;

        LinkGenerators(firstGeneratorEntity, secondGeneratorEntity!, tiles, gridEntity);
        return true;
    }

    private void LinkGenerators(Entity<KsFieldGeneratorComponent, TransformComponent> firstGeneratorEntity, Entity<KsFieldGeneratorComponent, TransformComponent?> secondGeneratorEntity, in ValueList<Vector2i> tiles, Entity<MapGridComponent> gridEntity)
    {
        foreach (var tilePos in tiles)
        {
            var fieldUid = SpawnAttachedTo(firstGeneratorEntity.Comp1.FieldProto, _mapSystem.GridTileToLocal(gridEntity, gridEntity, tilePos), rotation: firstGeneratorEntity.Comp2.LocalRotation);
            var fieldComponent = AddComp<KsGeneratedFieldComponent>(fieldUid);

            fieldComponent.GeneratorUids.Add(firstGeneratorEntity);
            fieldComponent.GeneratorUids.Add(secondGeneratorEntity);

            firstGeneratorEntity.Comp1.FieldUids.Add(fieldUid);
            secondGeneratorEntity.Comp1.FieldUids.Add(fieldUid);
        }

        firstGeneratorEntity.Comp1.LinkedGeneratorUid = secondGeneratorEntity.Owner;
        DirtyField(firstGeneratorEntity.Owner, firstGeneratorEntity.Comp1, nameof(firstGeneratorEntity.Comp1.LinkedGeneratorUid));

        secondGeneratorEntity.Comp1.LinkedGeneratorUid = firstGeneratorEntity.Owner;
        DirtyField(secondGeneratorEntity.Owner, secondGeneratorEntity.Comp1, nameof(secondGeneratorEntity.Comp1.LinkedGeneratorUid));

        _adminLogManager.Add(LogType.FieldGeneration, LogImpact.Low, $"{ToPrettyString(firstGeneratorEntity.Owner):generator} linked to {ToPrettyString(secondGeneratorEntity.Owner):generator} with a field of {tiles.Count} tiles");
    }

    private void OnAnchorStateChanged(Entity<KsFieldGeneratorComponent> entity, ref AnchorStateChangedEvent args)
    {
        if (args.Anchored)
        {
            TryUpdateAnchoredPosition(entity, args.Transform);
            return;
        }
        else
            TryRemoveFromAnchoredPositions(entity, args.Transform);

        UnlinkAndClearFields(entity);
    }

    private void OnReAnchor(Entity<KsFieldGeneratorComponent> entity, ref ReAnchorEvent args)
    {
        // Moved to another grid (a grid split, say): out of the old grid's cache, and into the new one's
        RemoveFromTileCache(entity, args.OldGrid, args.TilePos);
        TryUpdateAnchoredPosition(entity, args.Xform);
    }

    private void UpdateAnchoredPosition(Entity<KsFieldGeneratorComponent> entity, TransformComponent transformComponent, Entity<MapGridComponent> gridEntity)
    {
        var gridEntTileCache = _tileCache.GetOrNew(gridEntity);
        var generatorsOnTile = gridEntTileCache.GetOrNew(_mapSystem.CoordinatesToTile(gridEntity, gridEntity, transformComponent.Coordinates));

        generatorsOnTile.Add(entity);
    }

    private void RemoveFromAnchoredPositions(Entity<KsFieldGeneratorComponent> entity, TransformComponent transformComponent, Entity<MapGridComponent> oldGridEntity)
    {
        RemoveFromTileCache(entity, oldGridEntity, _mapSystem.CoordinatesToTile(oldGridEntity, oldGridEntity, transformComponent.Coordinates));
    }

    private void RemoveFromTileCache(EntityUid generatorUid, EntityUid gridUid, Vector2i tileCoordinates)
    {
        if (!_tileCache.TryGetValue(gridUid, out var gridEntTileCache) ||
            !gridEntTileCache.TryGetValue(tileCoordinates, out var localTileCache))
            return;

        localTileCache.Remove(generatorUid);

        // Clear unused caches
        if (localTileCache.Count == 0)
            gridEntTileCache.Remove(tileCoordinates);

        if (gridEntTileCache.Count == 0)
            _tileCache.Remove(gridUid);
    }

    private bool TryUpdateAnchoredPosition(Entity<KsFieldGeneratorComponent> entity, TransformComponent transformComponent)
    {
        var gridUid = transformComponent.GridUid ?? transformComponent.ParentUid;
        if (!TryComp<MapGridComponent>(gridUid, out var mapGridComponent))
            return false;

        UpdateAnchoredPosition(entity, transformComponent, (gridUid, mapGridComponent));
        return true;
    }

    private bool TryRemoveFromAnchoredPositions(Entity<KsFieldGeneratorComponent> entity, TransformComponent transformComponent)
    {
        var gridUid = transformComponent.GridUid ?? transformComponent.ParentUid;
        if (!TryComp<MapGridComponent>(gridUid, out var mapGridComponent))
            return false;

        RemoveFromAnchoredPositions(entity, transformComponent, (gridUid, mapGridComponent));
        return true;
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<KsFieldGeneratorComponent> entity, ref ComponentShutdown args)
    {
        var transformComponent = Transform(entity);
        TryRemoveFromAnchoredPositions(entity, transformComponent);

        ClearFields(entity);

        if (entity.Comp.LinkedGeneratorUid is not { } linkedUid ||
            !TryComp<KsFieldGeneratorComponent>(linkedUid, out var linkedGeneratorComponent))
            return;

        linkedGeneratorComponent.LinkedGeneratorUid = null;
        DirtyField(linkedUid, linkedGeneratorComponent, nameof(linkedGeneratorComponent.LinkedGeneratorUid));

        ClearFields((linkedUid, linkedGeneratorComponent));
    }
}
