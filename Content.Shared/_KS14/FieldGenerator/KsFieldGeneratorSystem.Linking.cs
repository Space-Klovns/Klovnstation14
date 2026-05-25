using System.Numerics;
using Content.Shared.Physics;
using Robust.Shared.Collections;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Utility;

namespace Content.Shared._KS14.FieldGenerator;

// HOLY COPYPASTA
// TODO: Clean this up, FUCK

public sealed partial class KsFieldGeneratorSystem : EntitySystem
{
    [Dependency] private readonly RayCastSystem _rayCastSystem = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;

    // Could be a component on the grid but whatever
    // Server-side only IG
    /// <summary>
    ///     Cache of every grid, and a dictionary of anchored generators on that grid
    ///         as well as their tile coordinates.
    /// </summary>
    private readonly Dictionary<EntityUid, Dictionary<Vector2i, HashSet<EntityUid>>> _tileCache = [];

    private void InitialiseLinking()
    {
        base.Initialize();

        if (_netManager.IsServer) // FUCK, i dont wanna deal with this on client
        {
            SubscribeLocalEvent<KsFieldGeneratorComponent, AnchorStateChangedEvent>(OnAnchorStateChanged);
            SubscribeLocalEvent<KsFieldGeneratorComponent, ReAnchorEvent>(OnReAnchor);
        }

        SubscribeLocalEvent<KsFieldGeneratorComponent, ComponentShutdown>(OnShutdown);
    }

    private void ClearFields(Entity<KsFieldGeneratorComponent> entity)
    {
        var fieldUids = entity.Comp.FieldUids;
        if (fieldUids.Count == 0)
            return;

        foreach (var fieldUid in fieldUids)
        {
            var fieldComponent = _fieldQuery.GetComponent(fieldUid);
            fieldComponent.GeneratorUids.Clear();

            PredictedQueueDel(fieldUid);
        }

        fieldUids.Clear();
    }

    private void UnlinkAndClearFields(Entity<KsFieldGeneratorComponent> entity, EntityUid? linkedUid = null)
    {
        ClearFields(entity);

        linkedUid ??= entity.Comp.LinkedGeneratorUid;
        if (linkedUid is { })
        {
            var linkedGeneratorComponent = Comp<KsFieldGeneratorComponent>(linkedUid.Value);
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
        if (!Resolve(secondGeneratorEntity.Owner, ref secondGeneratorEntity.Comp1) ||
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
        if (_tileCache.TryGetValue(args.OldGrid, out var oldGridEntTileCache) &&
            oldGridEntTileCache.TryGetValue(args.TilePos, out var oldLocalTileCache))
        {
            oldLocalTileCache.Remove(entity);

            // Clear unused caches
            if (oldLocalTileCache.Count == 0)
                oldGridEntTileCache.Remove(args.TilePos);

            if (oldGridEntTileCache.Count == 0)
                _tileCache.Remove(args.OldGrid);
        }

    }

    private void UpdateAnchoredPosition(Entity<KsFieldGeneratorComponent> entity, TransformComponent transformComponent, Entity<MapGridComponent> gridEntity)
    {
        var gridEntTileCache = _tileCache.GetOrNew(gridEntity);
        var generatorsOnTile = gridEntTileCache.GetOrNew(_mapSystem.CoordinatesToTile(gridEntity, gridEntity, transformComponent.Coordinates));

        generatorsOnTile.Add(entity);
    }

    private void RemoveFromAnchoredPositions(Entity<KsFieldGeneratorComponent> entity, TransformComponent transformComponent, Entity<MapGridComponent> oldGridEntity)
    {
        if (!_tileCache.TryGetValue(oldGridEntity, out var oldGridEntTileCache))
            return;

        var tileCoordinates = _mapSystem.CoordinatesToTile(oldGridEntity, oldGridEntity, transformComponent.Coordinates);
        if (!oldGridEntTileCache.TryGetValue(tileCoordinates, out var oldLocalTileCache))
            return;

        oldLocalTileCache.Remove(entity);

        // Clear unused caches
        if (oldLocalTileCache.Count == 0)
            oldGridEntTileCache.Remove(tileCoordinates);

        if (oldGridEntTileCache.Count == 0)
            _tileCache.Remove(oldGridEntity);
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
