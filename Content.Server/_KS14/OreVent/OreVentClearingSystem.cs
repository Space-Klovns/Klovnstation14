using Content.Server.Gatherable.Components;
using Content.Shared._KS14.OreVent;
using Content.Shared.Destructible;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;

namespace Content.Server._KS14.OreVent;

/// <summary>
///     Destroys the minable rock around an ore vent as it clears. Nothing is dropped:
///         the vent is what's worth mining. Leaves rubble about, as tgstation's vents do.
/// </summary>
public sealed partial class OreVentClearingSystem : EntitySystem
{
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private SharedDestructibleSystem _destructibleSystem = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private TurfSystem _turfSystem = default!;

    private readonly HashSet<Entity<GatherableComponent>> _rocks = [];
    private readonly HashSet<Vector2i> _rockTiles = [];

    [SubscribeLocalEvent]
    private void OnClearing(Entity<OreVentComponent> entity, ref OreVentClearingEvent args)
    {
        var ventTransformComponent = Transform(entity);

        _rocks.Clear();
        _rockTiles.Clear();
        _entityLookupSystem.GetEntitiesInRange(ventTransformComponent.Coordinates, args.Radius, _rocks, LookupFlags.Static);

        TryComp<MapGridComponent>(ventTransformComponent.GridUid, out var gridComponent);

        foreach (var rock in _rocks)
        {
            var rockCoordinates = Transform(rock).Coordinates;
            if (gridComponent != null)
                _rockTiles.Add(_mapSystem.TileIndicesFor(ventTransformComponent.GridUid!.Value, gridComponent, rockCoordinates));

            if (_robustRandom.Prob(entity.Comp.RockRubbleChance))
                Spawn(entity.Comp.RubbleProto, rockCoordinates);

            _destructibleSystem.DestroyEntity(rock.Owner);
        }

        if (_rocks.Count > 0)
            _audioSystem.PlayPvs(entity.Comp.ClearingSound, entity.Owner);

        if (gridComponent != null)
            ScatterFloorRubble(entity, (ventTransformComponent.GridUid!.Value, gridComponent), args.Radius);
    }

    /// <summary>
    ///     Rubble on some of the open floor around the vent, as on tgstation, where each clearing iteration rolls for
    ///         every floor tile in range again.
    /// </summary>
    private void ScatterFloorRubble(Entity<OreVentComponent> entity, Entity<MapGridComponent> grid, float radius)
    {
        var ventTile = _mapSystem.TileIndicesFor(grid, grid.Comp, Transform(entity).Coordinates);
        var circle = new Circle(_transformSystem.GetWorldPosition(entity), radius);

        foreach (var tileRef in _mapSystem.GetTilesIntersecting(grid, grid.Comp, circle))
        {
            if (tileRef.GridIndices == ventTile ||
                _rockTiles.Contains(tileRef.GridIndices) ||
                !_robustRandom.Prob(entity.Comp.FloorRubbleChance) ||
                _turfSystem.IsTileBlocked(tileRef, CollisionGroup.Impassable))
                continue;

            Spawn(entity.Comp.RubbleProto, _mapSystem.GridTileToLocal(grid, grid.Comp, tileRef.GridIndices));
        }
    }
}
