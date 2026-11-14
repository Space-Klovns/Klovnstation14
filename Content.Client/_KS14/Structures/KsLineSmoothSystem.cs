using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client._KS14.Structures;

/// <summary>
///     Joins <see cref="KsLineSmoothComponent"/> structures into lines; see the component for the rules.
/// </summary>
/// <remarks>
///     Redrawn when a structure is set up, torn down, anchored, unanchored, moved or turned, and then for its
///         neighbours. Never per frame.
/// </remarks>
public sealed partial class KsLineSmoothSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;
    [Dependency] private EntityQuery<KsLineSmoothComponent> _lineSmoothQuery = default!;

    private static readonly Direction[] Cardinals = [Direction.North, Direction.East, Direction.South, Direction.West];

    [SubscribeLocalEvent]
    private void OnStartup(Entity<KsLineSmoothComponent> entity, ref ComponentStartup args)
    {
        RefreshWithNeighbours(entity);
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<KsLineSmoothComponent> entity, ref ComponentShutdown args)
    {
        // Still in the tile while it goes, so the neighbours must be told to look past it.
        RefreshNeighbours(entity);
    }

    [SubscribeLocalEvent]
    private void OnAnchorStateChanged(Entity<KsLineSmoothComponent> entity, ref AnchorStateChangedEvent args)
    {
        RefreshWithNeighbours(entity);
    }

    [SubscribeLocalEvent]
    private void OnMove(Entity<KsLineSmoothComponent> entity, ref MoveEvent args)
    {
        RefreshWithNeighbours(entity);
    }

    private void RefreshWithNeighbours(Entity<KsLineSmoothComponent> entity)
    {
        Refresh(entity);
        RefreshNeighbours(entity);
    }

    private void RefreshNeighbours(Entity<KsLineSmoothComponent> entity)
    {
        if (!TryGetGridTile(Transform(entity), out var grid, out var tile))
            return;

        foreach (var direction in Cardinals)
        {
            foreach (var neighbourUid in _mapSystem.GetAnchoredEntities(grid, tile + direction.ToIntVec()))
            {
                if (neighbourUid != entity.Owner && _lineSmoothQuery.TryComp(neighbourUid, out var neighbourComponent))
                    Refresh((neighbourUid, neighbourComponent));
            }
        }
    }

    private void Refresh(Entity<KsLineSmoothComponent> entity)
    {
        if (!TryComp<SpriteComponent>(entity, out var spriteComponent))
            return;

        var sprite = (entity.Owner, spriteComponent);
        var transformComponent = Transform(entity);
        var facing = transformComponent.LocalRotation.GetCardinalDir();

        // An unanchored structure isn't part of any line.
        var north = false;
        var east = false;
        var south = false;
        var west = false;
        if (transformComponent.Anchored && TryGetGridTile(transformComponent, out var grid, out var tile))
        {
            north = HasNeighbour(entity, grid, tile, Direction.North);
            east = HasNeighbour(entity, grid, tile, Direction.East);
            south = HasNeighbour(entity, grid, tile, Direction.South);
            west = HasNeighbour(entity, grid, tile, Direction.West);
        }

        var showNorthPost = north && !south;
        var showSouthPost = south && !north;
        var showEastPost = east && !west;
        var showWestPost = west && !east;

        // Alone, it's a short run of its own: a post at each end of the way it faces.
        if (!north && !east && !south && !west)
        {
            var vertical = facing is Direction.North or Direction.South;
            showNorthPost = showSouthPost = vertical;
            showEastPost = showWestPost = !vertical;
        }

        var rotation = transformComponent.LocalRotation;
        SetLayer(sprite, entity.Comp.VerticalWire, north && south, Direction.South, facing, rotation);
        SetLayer(sprite, entity.Comp.HorizontalWire, east && west, Direction.East, facing, rotation);
        SetLayer(sprite, entity.Comp.NorthPost, showNorthPost, Direction.North, facing, rotation);
        SetLayer(sprite, entity.Comp.SouthPost, showSouthPost, Direction.South, facing, rotation);
        SetLayer(sprite, entity.Comp.EastPost, showEastPost, Direction.East, facing, rotation);
        SetLayer(sprite, entity.Comp.WestPost, showWestPost, Direction.West, facing, rotation);
    }

    /// <summary>
    ///     Shows or hides a layer, turned to draw its <paramref name="direction"/> state and placed at its offset along
    ///         the grid, whichever way the structure faces.
    /// </summary>
    private void SetLayer(Entity<SpriteComponent> sprite, KsLineSmoothLayer layer, bool visible, Direction direction, Direction facing, Angle rotation)
    {
        if (!_spriteSystem.LayerMapTryGet(sprite.AsNullable(), layer.Key, out var layerIndex, logMissing: true))
            return;

        _spriteSystem.LayerSetVisible(sprite.AsNullable(), layerIndex, visible);
        if (!visible)
            return;

        _spriteSystem.LayerSetDirOffset(sprite.AsNullable(), layerIndex, GetDirOffset(facing, direction));
        _spriteSystem.LayerSetOffset(sprite.AsNullable(), layerIndex, GetLocalOffset(rotation, layer.Offset));
    }

    /// <summary>
    ///     The layer offset that puts a layer <paramref name="gridPixels"/> along the grid on a structure turned by
    ///         <paramref name="rotation"/>. A directional layer's texture is drawn upright whichever way the entity
    ///         faces, but its offset still turns with the entity, so it is turned back here.
    /// </summary>
    public static Vector2 GetLocalOffset(Angle rotation, Vector2 gridPixels)
    {
        return (-rotation).RotateVec(gridPixels / EyeManager.PixelsPerMeter);
    }

    /// <summary>
    ///     The offset that turns a layer drawn facing <paramref name="facing"/> into one drawn facing
    ///         <paramref name="direction"/>.
    /// </summary>
    public static SpriteComponent.DirectionOffset GetDirOffset(Direction facing, Direction direction)
    {
        var quarterTurns = (ClockwiseIndex(direction) - ClockwiseIndex(facing) + 4) % 4;
        return quarterTurns switch
        {
            1 => SpriteComponent.DirectionOffset.Clockwise,
            2 => SpriteComponent.DirectionOffset.Flip,
            3 => SpriteComponent.DirectionOffset.CounterClockwise,
            _ => SpriteComponent.DirectionOffset.None,
        };
    }

    private static int ClockwiseIndex(Direction direction)
    {
        return direction switch
        {
            Direction.East => 1,
            Direction.South => 2,
            Direction.West => 3,
            _ => 0,
        };
    }

    private bool HasNeighbour(Entity<KsLineSmoothComponent> entity, Entity<MapGridComponent> grid, Vector2i tile, Direction direction)
    {
        foreach (var neighbourUid in _mapSystem.GetAnchoredEntities(grid, tile + direction.ToIntVec()))
        {
            if (neighbourUid != entity.Owner &&
                !TerminatingOrDeleted(neighbourUid) &&
                _lineSmoothQuery.TryComp(neighbourUid, out var neighbourComponent) &&
                neighbourComponent.Key == entity.Comp.Key)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryGetGridTile(TransformComponent transformComponent, out Entity<MapGridComponent> grid, out Vector2i tile)
    {
        grid = default;
        tile = default;

        if (transformComponent.GridUid is not { } gridUid || !TryComp(gridUid, out MapGridComponent? gridComponent))
            return false;

        grid = (gridUid, gridComponent);
        tile = _mapSystem.TileIndicesFor(grid, transformComponent.Coordinates);
        return true;
    }
}
