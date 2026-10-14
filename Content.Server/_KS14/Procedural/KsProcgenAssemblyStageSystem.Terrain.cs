using System.Collections.Frozen;
using System.Linq;
using System.Numerics;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Procedural;

/// <summary>Detached terrain allocated exclusively on the assembly stage's disposable grid.</summary>
public sealed class KsProcgenRoomTerrain
{
    internal KsProcgenRoomTerrain(FrozenSet<Vector2i> floor, FrozenSet<Vector2i> walls,
        FrozenSet<Vector2i> blocked, ushort tileId, string wallPrototypeId)
    {
        Floor = floor;
        Walls = walls;
        Blocked = blocked;
        TileId = tileId;
        WallPrototypeId = wallPrototypeId;
    }

    public FrozenSet<Vector2i> Floor { get; }
    public FrozenSet<Vector2i> Walls { get; }
    public FrozenSet<Vector2i> Blocked { get; }
    public ushort TileId { get; }
    public string WallPrototypeId { get; }
}

public sealed partial class KsProcgenAssemblyStageSystem
{
    [Dependency] private ITileDefinitionManager _tileDefinitions = default!;

    private string? PrepareTerrain(KsProcgenRoomAccessMask mask, string floorTileId, string wallPrototypeId,
        int maxTerrainCells, out KsProcgenRoomTerrain? terrain)
    {
        terrain = null;
        if (mask == null || maxTerrainCells is < 1 or > 65_536 || mask.CellCenterOrigin != new Vector2(0.5f, 0.5f) ||
            mask.Floor.Count is < 1 or > 4096 || mask.Walls.Count > 8192 ||
            mask.Blocking.Count > 8192 || mask.Occupied.Count > 8192 || mask.Floor.Overlaps(mask.Walls) ||
            !_tileDefinitions.TryGetDefinition(floorTileId, out var tile) || tile.TileId == 0 ||
            !ProtoMan.TryIndex<EntityPrototype>(wallPrototypeId, out _))
            return "InvalidRoomTerrain";
        if ((long) mask.Floor.Count + mask.Walls.Count > maxTerrainCells)
            return "RoomTerrainCellBudget";
        foreach (var cell in mask.Floor)
        {
            if (Math.Abs((long) cell.X) > 1_000_000 || Math.Abs((long) cell.Y) > 1_000_000)
                return "InvalidRoomTerrain";
        }
        foreach (var cell in mask.Walls)
        {
            if (Math.Abs((long) cell.X) > 1_000_000 || Math.Abs((long) cell.Y) > 1_000_000)
                return "InvalidRoomTerrain";
        }
        terrain = new(mask.Floor.ToFrozenSet(), mask.Walls.ToFrozenSet(),
            mask.Blocking.Concat(mask.Occupied).ToFrozenSet(), tile.TileId, wallPrototypeId);
        return null;
    }

    private string? TerrainIssue(KsProcgenAssemblyStage stage)
    {
        if (stage.Terrain == null)
            return stage.GridUid == null ? null : "AssemblyStageTerrainDrift";
        if (stage.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid) ||
            EntityManager.IsQueuedForDeletion(gridUid) || grid.TileSize != 1 ||
            !TryComp(gridUid, out TransformComponent? gridTransform) || gridTransform.ParentUid != stage.MapUid ||
            gridTransform.LocalPosition != Vector2.Zero || gridTransform.LocalRotation != Angle.Zero ||
            Transform(stage.MapUid).LocalPosition != Vector2.Zero || Transform(stage.MapUid).LocalRotation != Angle.Zero)
            return "AssemblyStageTerrainDrift";
        var terrain = stage.Terrain;
        if (_mapSystem.GetFilledTileCount((gridUid, grid)) != terrain.Floor.Count + terrain.Walls.Count ||
            stage.Walls.Count != terrain.Walls.Count)
            return "AssemblyStageTerrainDrift";
        foreach (var cell in terrain.Floor)
        {
            if (_mapSystem.GetTileRef(gridUid, grid, cell).Tile.TypeId != terrain.TileId)
                return "AssemblyStageTerrainDrift";
        }
        foreach (var cell in terrain.Walls)
        {
            if (_mapSystem.GetTileRef(gridUid, grid, cell).Tile.TypeId != terrain.TileId ||
                !stage.Walls.TryGetValue(cell, out var wallUid) ||
                !TryComp(wallUid, out MetaDataComponent? metadata) ||
                metadata.EntityPrototype?.ID != terrain.WallPrototypeId ||
                metadata.EntityLifeStage != (stage.Phase == KsProcgenAssemblyStagePhase.Initialized ?
                    EntityLifeStage.MapInitialized : EntityLifeStage.Initialized) ||
                EntityManager.IsQueuedForDeletion(wallUid) ||
                !TryComp(wallUid, out TransformComponent? transform) || transform.ParentUid != gridUid ||
                transform.LocalPosition != new Vector2(cell.X + 0.5f, cell.Y + 0.5f) || transform.LocalRotation != Angle.Zero)
                return "AssemblyStageTerrainDrift";
        }
        return null;
    }
}
