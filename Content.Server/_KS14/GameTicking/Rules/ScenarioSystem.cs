using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Shared.GameTicking.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;
using Robust.Shared.EntitySerialization.Systems;
using System;
using System.Collections.Generic;
using System.Linq;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Random;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server._KS14.GameTicking.Rules;

[RegisterComponent]
public sealed partial class ScenarioRuleComponent : Component
{
    [DataField("mapShape")]
    public MapShape MapShape { get; set; } = MapShape.Circle;

    [DataField("mapSize")]
    public int MapSize { get; set; } = 300;

    [DataField("canyonWidth")]
    public int CanyonWidth { get; set; } = 100;

    [DataField("structures")]
    public List<ScenarioStructure> Structures { get; set; } = new();
}

public enum MapShape
{
    Circle,
    Canyon
}

[DataDefinition]
public sealed partial class ScenarioStructure
{
    [DataField("proto", required: true)]
    public string ProtoId { get; set; } = string.Empty;

    [DataField("randomLocation")]
    public bool RandomLocation { get; set; } = false;

    [DataField("coords")]
    public (int X, int Y) Coordinates { get; set; } = (0, 0);
}

public sealed class ScenarioSystem : GameRuleSystem<ScenarioRuleComponent>
{
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly MapSystem _mapSystem = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    protected override void Added(EntityUid uid, ScenarioRuleComponent component, GameRuleComponent gameRule, GameRuleAddedEvent args)
    {
        base.Added(uid, component, gameRule, args);

        try
        {
            // Create the map
            var mapId = _mapManager.CreateMap();
            _mapManager.SetMapPaused(mapId, true);

            // Create a grid for the map
            var grid = _mapManager.CreateGridEntity(mapId);
            var gridComp = _entityManager.GetComponent<MapGridComponent>(grid);

            // Generate base planetmap
            GeneratePlanetMap(grid, gridComp, component.MapSize);

            // Carve out the shape
            CarvemapShape(grid, gridComp, component.MapShape, component.MapSize, component.CanyonWidth);

            // Spawn perimeter walls
            SpawnPerimeterWalls(mapId, grid, component.MapShape, component.MapSize, component.CanyonWidth);

            // Spawn structures
            SpawnStructures(mapId, grid, component.Structures, component.MapSize);

            // Initialize atmosphere
            _mapSystem.InitializeMap(mapId);
            _mapManager.SetMapPaused(mapId, false);

            Log.Info("Scenario map generated successfully");
        }
        catch (Exception e)
        {
            Log.Warning($"Scenario: Exception during map generation: {e.Message}\n{e.StackTrace}");
        }
    }

    private void GeneratePlanetMap(EntityUid grid, MapGridComponent gridComp, int mapSize)
    {
        // Create a flat planetmap filled with floor
        var halfSize = mapSize / 2;

        for (int x = -halfSize; x <= halfSize; x++)
        {
            for (int y = -halfSize; y <= halfSize; y++)
            {
                var pos = new Vector2i(x, y);
                // Set to basalt floor tile (0x0A is basalt)
                _mapSystem.SetTile(grid, gridComp, pos, new Tile(10));
            }
        }

        Log.Info($"Scenario: Generated {mapSize}x{mapSize} base planetmap");
    }

    private void CarvemapShape(EntityUid grid, MapGridComponent gridComp, MapShape shape, int mapSize, int canyonWidth)
    {
        // Carve out the map based on shape - remove tiles to create shape
        if (shape == MapShape.Circle)
        {
            CarveCircle(grid, gridComp, mapSize);
        }
        else if (shape == MapShape.Canyon)
        {
            CarveCanyon(grid, gridComp, mapSize, canyonWidth);
        }
    }

    private void CarveCircle(EntityUid grid, MapGridComponent gridComp, int radius)
    {
        var centerX = 0;
        var centerY = 0;
        var radiusFloat = radius / 2f;

        // Remove tiles outside the circle
        for (int x = -radius; x <= radius; x++)
        {
            for (int y = -radius; y <= radius; y++)
            {
                var distSq = x * x + y * y;
                if (distSq > radiusFloat * radiusFloat)
                {
                    var pos = new Vector2i(centerX + x, centerY + y);
                    // Set to space (empty)
                    _mapSystem.SetTile(grid, gridComp, pos, Tile.Empty);
                }
            }
        }

        Log.Info("Scenario: Carved circular map shape");
    }

    private void CarveCanyon(EntityUid grid, MapGridComponent gridComp, int width, int canyonWidth)
    {
        var centerX = 0;
        var centerY = 0;
        var halfWidth = width / 2;
        var halfCanyonWidth = canyonWidth / 2;

        // Remove tiles outside the canyon
        for (int x = -halfWidth; x <= halfWidth; x++)
        {
            for (int y = -halfWidth; y <= halfWidth; y++)
            {
                if (y > halfCanyonWidth || y < -halfCanyonWidth)
                {
                    var pos = new Vector2i(centerX + x, centerY + y);
                    // Set to space (empty)
                    _mapSystem.SetTile(grid, gridComp, pos, Tile.Empty);
                }
            }
        }

        Log.Info("Scenario: Carved canyon map shape");
    }

    private void SpawnPerimeterWalls(MapId mapId, EntityUid gridUid, MapShape shape, int mapSize, int canyonWidth)
    {
        var centerX = 0;
        var centerY = 0;

        if (shape == MapShape.Circle)
        {
            // Spawn walls in a circle around the perimeter
            var wallRadius = mapSize / 2;
            var circumference = (int)(2 * MathF.PI * wallRadius);

            for (int i = 0; i < circumference; i++)
            {
                var angle = (i / (float)circumference) * MathF.PI * 2;
                var x = centerX + (int)(MathF.Cos(angle) * wallRadius);
                var y = centerY + (int)(MathF.Sin(angle) * wallRadius);

                SpawnWall(mapId, gridUid, x, y);
            }

            Log.Info($"Scenario: Spawned {circumference} walls in circular perimeter");
        }
        else if (shape == MapShape.Canyon)
        {
            // Spawn walls at canyon edges
            var halfWidth = mapSize / 2;
            var halfCanyonWidth = canyonWidth / 2;

            // Top and bottom walls
            for (int x = -halfWidth; x <= halfWidth; x++)
            {
                SpawnWall(mapId, gridUid, x, centerY + halfCanyonWidth + 1);
                SpawnWall(mapId, gridUid, x, centerY - halfCanyonWidth - 1);
            }

            Log.Info("Scenario: Spawned canyon edge walls");
        }
    }

    private void SpawnWall(MapId mapId, EntityUid gridUid, int x, int y)
    {
        try
        {
            var coords = new EntityCoordinates(gridUid, x, y);
            _entityManager.SpawnEntity("RockWallInvincible", coords);
        }
        catch (Exception e)
        {
            // Silent fail - wall spawning is not critical
        }
    }

    private void SpawnStructures(MapId mapId, EntityUid gridUid, List<ScenarioStructure> structures, int mapSize)
    {
        foreach (var structure in structures)
        {
            try
            {
                int x, y;

                if (structure.RandomLocation)
                {
                    // Spawn at random location within inner map area
                    x = _random.Next(-mapSize / 4, mapSize / 4);
                    y = _random.Next(-mapSize / 4, mapSize / 4);
                }
                else
                {
                    // Use specified coordinates
                    x = structure.Coordinates.X;
                    y = structure.Coordinates.Y;
                }

                var coords = new EntityCoordinates(gridUid, x, y);
                _entityManager.SpawnEntity(structure.ProtoId, coords);
                Log.Info($"Scenario: Spawned {structure.ProtoId} at ({x}, {y})");
            }
            catch (Exception e)
            {
                Log.Warning($"Scenario: Failed to spawn {structure.ProtoId}: {e.Message}");
            }
        }
    }
}
