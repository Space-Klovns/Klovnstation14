using System.Numerics;
using System.Threading.Tasks;
using Content.Server.Procedural;
using Content.Shared.Procedural;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._KS14.Procedural;

/// <summary>
///     Generates standalone dungeon grids on an isolated staging map, then places them without overlapping any
///     existing grid on the target map.
/// </summary>
public sealed partial class KsCollisionFreeDungeonGridSystem : EntitySystem
{
    [Dependency] private DungeonSystem _dungeonSystem = default!;
    [Dependency] private MapSystem _mapSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    public async Task<Entity<MapGridComponent>?> GenerateAsync(
        DungeonConfig dungeonConfig,
        MapId targetMapId,
        Vector2 preferredCenter,
        Vector2 searchDirection,
        float separationPadding,
        float minimumPlacementStep,
        int seed)
    {
        if (searchDirection.LengthSquared() <= 0f)
            return null;

        _mapSystem.CreateMap(out var stagingMapId);

        try
        {
            var generatedGrid = _mapSystem.CreateGridEntity(stagingMapId);
            await _dungeonSystem.GenerateDungeonAsync(
                dungeonConfig,
                generatedGrid.Owner,
                generatedGrid.Comp,
                Vector2i.Zero,
                seed);

            if (TerminatingOrDeleted(generatedGrid.Owner) || !_mapSystem.MapExists(targetMapId))
                return null;

            if (!TryFindCollisionFreePosition(
                    generatedGrid.Comp,
                    targetMapId,
                    preferredCenter,
                    searchDirection,
                    separationPadding,
                    minimumPlacementStep,
                    out var gridPosition))
            {
                return null;
            }

            _transformSystem.SetMapCoordinates(
                generatedGrid,
                new MapCoordinates(gridPosition, targetMapId));
            return generatedGrid;
        }
        finally
        {
            if (_mapSystem.MapExists(stagingMapId))
                _mapSystem.DeleteMap(stagingMapId);
        }
    }

    /// <summary>
    ///     Searches outward until it finds a free placement. Maps are spatially unbounded, so a finite retry limit
    ///     can only discard valid generated grids when the intended area happens to be crowded.
    /// </summary>
    public bool TryFindCollisionFreePosition(
        MapGridComponent generatedGrid,
        MapId targetMapId,
        Vector2 preferredCenter,
        Vector2 searchDirection,
        float separationPadding,
        float minimumPlacementStep,
        out Vector2 gridPosition)
    {
        gridPosition = default;
        if (searchDirection.LengthSquared() <= 0f)
            return false;

        var direction = Vector2.Normalize(searchDirection);
        var placementStep = MathF.Max(
            generatedGrid.LocalAABB.MaxDimension + separationPadding * 2f,
            minimumPlacementStep);
        var overlappingGrids = new List<Entity<MapGridComponent>>();

        for (var attempt = 0L; _mapSystem.MapExists(targetMapId); attempt++)
        {
            var center = preferredCenter + direction * placementStep * attempt;
            gridPosition = center - generatedGrid.LocalAABB.Center;
            var candidateBounds = generatedGrid.LocalAABB
                .Translated(gridPosition)
                .Enlarged(separationPadding);

            overlappingGrids.Clear();
            _mapSystem.FindGridsIntersecting(targetMapId, candidateBounds, ref overlappingGrids);
            if (overlappingGrids.Count == 0)
                return true;
        }

        return false;
    }
}
