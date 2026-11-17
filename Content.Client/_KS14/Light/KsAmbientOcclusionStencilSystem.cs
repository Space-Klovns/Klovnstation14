using Content.Shared.Maps;
using Robust.Client.Graphics;
using Robust.Shared.Map.Components;

namespace Content.Client._KS14.Light;

/// <summary>
///     Draws a grid's floor into the stencil for the ambient occlusion, as a handful of cached rectangles per block of
///         tiles rather than one per tile per frame.
/// </summary>
/// <remarks>
///     Blocks are this system's own, not the engine's chunks: a grid's chunk size is internal to the engine, so a block
///         is found from a tile's indices instead.
/// </remarks>
public sealed partial class KsAmbientOcclusionStencilSystem : EntitySystem
{
    /// <summary>
    ///     The side of a block, in tiles. A tile change rebuilds only its block.
    /// </summary>
    public const int BlockSize = 16;

    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private TurfSystem _turfSystem = default!;

    [Dependency] private EntityQuery<KsAmbientOcclusionStencilCacheComponent> _stencilCacheQuery = default!;

    // Scratch for merging one block's rows into rectangles: the rectangles so far, and those still open on the last row,
    //      by their span.
    private readonly List<Box2i> _mergedRectangles = new();
    private readonly Dictionary<(int Left, int Right), int> _openRectangles = new();
    private readonly Dictionary<(int Left, int Right), int> _nextOpenRectangles = new();

    [SubscribeLocalEvent]
    private void OnTileChanged(Entity<KsAmbientOcclusionStencilCacheComponent> entity, ref TileChangedEvent args)
    {
        foreach (var change in args.Changes)
            entity.Comp.Blocks.Remove(GetBlockIndex(change.GridIndices));
    }

    /// <summary>
    ///     Draws the floor of every block of <paramref name="grid"/> that <paramref name="localBounds"/> touches, with the
    ///         handle's current shader. The handle's transform must already be the grid's world matrix.
    /// </summary>
    public void DrawFloor(DrawingHandleWorld worldHandle, Entity<MapGridComponent> grid, Box2 localBounds)
    {
        if (!_stencilCacheQuery.TryComp(grid.Owner, out var cacheComponent))
            cacheComponent = AddComp<KsAmbientOcclusionStencilCacheComponent>(grid.Owner);

        var blockWorldSize = (float)(BlockSize * grid.Comp.TileSize);
        var minimumBlock = new Vector2i(
            (int)MathF.Floor(localBounds.Left / blockWorldSize),
            (int)MathF.Floor(localBounds.Bottom / blockWorldSize));
        var maximumBlock = new Vector2i(
            (int)MathF.Floor(localBounds.Right / blockWorldSize),
            (int)MathF.Floor(localBounds.Top / blockWorldSize));

        for (var blockX = minimumBlock.X; blockX <= maximumBlock.X; blockX++)
        {
            for (var blockY = minimumBlock.Y; blockY <= maximumBlock.Y; blockY++)
            {
                var blockIndex = new Vector2i(blockX, blockY);
                if (!cacheComponent.Blocks.TryGetValue(blockIndex, out var rectangles))
                {
                    rectangles = BuildBlock(grid, blockIndex);
                    cacheComponent.Blocks[blockIndex] = rectangles;
                }

                foreach (var rectangle in rectangles)
                    worldHandle.DrawRect(rectangle, Color.White);
            }
        }
    }

    /// <returns>
    ///     Grid-local rectangles covering the block's non-space tiles: runs along each row, merged with the run of the
    ///         same span on the row below.
    /// </returns>
    private List<Box2> BuildBlock(Entity<MapGridComponent> grid, Vector2i blockIndex)
    {
        var origin = blockIndex * BlockSize;

        _mergedRectangles.Clear();
        _openRectangles.Clear();

        for (var y = 0; y < BlockSize; y++)
        {
            _nextOpenRectangles.Clear();

            var runStart = -1;
            for (var x = 0; x <= BlockSize; x++)
            {
                var isFloor = x < BlockSize && IsFloor(grid, origin + new Vector2i(x, y));
                if (isFloor)
                {
                    if (runStart < 0)
                        runStart = x;

                    continue;
                }

                if (runStart < 0)
                    continue;

                var span = (runStart, x);
                if (_openRectangles.TryGetValue(span, out var rectangleIndex))
                {
                    var rectangle = _mergedRectangles[rectangleIndex];
                    _mergedRectangles[rectangleIndex] = rectangle with { Top = rectangle.Top + 1 };
                }
                else
                {
                    rectangleIndex = _mergedRectangles.Count;
                    _mergedRectangles.Add(new Box2i(runStart, y, x, y + 1));
                }

                _nextOpenRectangles[span] = rectangleIndex;
                runStart = -1;
            }

            _openRectangles.Clear();
            foreach (var (span, rectangleIndex) in _nextOpenRectangles)
                _openRectangles[span] = rectangleIndex;
        }

        var tileSize = (float)grid.Comp.TileSize;
        var rectangles = new List<Box2>(_mergedRectangles.Count);
        foreach (var rectangle in _mergedRectangles)
        {
            rectangles.Add(new Box2(
                (origin.X + rectangle.Left) * tileSize,
                (origin.Y + rectangle.Bottom) * tileSize,
                (origin.X + rectangle.Right) * tileSize,
                (origin.Y + rectangle.Top) * tileSize));
        }

        return rectangles;
    }

    private static Vector2i GetBlockIndex(Vector2i tileIndices)
    {
        return new Vector2i(
            (int)MathF.Floor((float)tileIndices.X / BlockSize),
            (int)MathF.Floor((float)tileIndices.Y / BlockSize));
    }

    private bool IsFloor(Entity<MapGridComponent> grid, Vector2i tileIndices)
    {
        return _mapSystem.TryGetTileRef(grid.Owner, grid.Comp, tileIndices, out var tileRef)
            && !_turfSystem.IsSpace(tileRef);
    }
}
