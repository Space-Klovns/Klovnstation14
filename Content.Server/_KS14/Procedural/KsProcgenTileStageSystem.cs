using System.Linq;
using Content.Shared._KS14.Procedural;
using Content.Shared.GameTicking;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.Server._KS14.Procedural;

public enum KsProcgenTileStageStatus : byte
{
    Staged,
    InvalidPlan,
    UnsupportedContent,
    BudgetExceeded,
    EngineFailure,
}

/// <summary>
/// An uninitialized, paused map containing only generated floor tiles. Publication and
/// visibility isolation are not established by this preview stage.
/// Only the owning stage system may discard it.
/// </summary>
public sealed class KsProcgenTileStage
{
    internal KsProcgenTileStage(MapId mapId, EntityUid mapUid, EntityUid gridUid,
        ulong semanticHash, IReadOnlyList<(Vector2i Cell, ushort TileId)> tiles)
    {
        MapId = mapId;
        MapUid = mapUid;
        GridUid = gridUid;
        SemanticHash = semanticHash;
        Tiles = tiles;
    }

    public MapId MapId { get; }
    public EntityUid MapUid { get; }
    public EntityUid GridUid { get; }
    public ulong SemanticHash { get; }
    public int StagedCells => Tiles.Count;
    public bool Active { get; internal set; } = true;
    internal IReadOnlyList<(Vector2i Cell, ushort TileId)> Tiles { get; }
}

public sealed class KsProcgenTileStageResult
{
    public KsProcgenTileStageStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public KsProcgenTileStage? Stage { get; init; }
}

/// <summary>
/// First materialization slice. Creates a disposable tile preview and rejects every plan
/// feature that would need authored copying, spawned entities, operational doors, or publication.
/// </summary>
public sealed partial class KsProcgenTileStageSystem : EntitySystem
{
    [Dependency] private ITileDefinitionManager _tileDefinitionManager = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;

    private readonly Dictionary<MapId, KsProcgenTileStage> _activeStages = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
    }

    public override void Shutdown()
    {
        DiscardAll();
        base.Shutdown();
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent args)
    {
        DiscardAll();
    }

    public KsProcgenTileStageResult TryStage(
        KsProcgenGeometryPipelineResult plan,
        int maxTiles = 4_096)
    {
        if (plan == null || plan.Status != KsProcgenGeometryPipelineStatus.GeometryPlanned ||
            plan.SemanticHash == 0 || plan.Packing == null || plan.Partition == null ||
            plan.Materials?.Status != KsProcgenMaterialStatus.Planned)
            return Failure(KsProcgenTileStageStatus.InvalidPlan, "InvalidTileStagePlan");
        if (maxTiles <= 0 || maxTiles > 65_536)
            return Failure(KsProcgenTileStageStatus.InvalidPlan, "InvalidTileStageBudget");

        if (plan.Packing.Placements.Count != 0 || plan.HasUnverifiedConstantRegions ||
            plan.Packing.CellClaims.Any(entry => entry.Claim.Disposition !=
                KsProcgenCellDisposition.ProceduralFloor) ||
            plan.Partition.WallCells.Count != 0 || plan.Partition.DoorOpenings.Count != 0 ||
            plan.Materials.InteriorWalls.Count != 0 || plan.Materials.InteriorDoors.Count != 0 ||
            plan.Furnishings.Any(item => item.Proposal.Entities.Count != 0) ||
            plan.Lighting.Any(item => item.Proposal.Lights.Count != 0) ||
            plan.Windows?.ChosenWindowCells.Count > 0)
            return Failure(KsProcgenTileStageStatus.UnsupportedContent, "UnsupportedTileStageContent");

        var expected = plan.Partition.FloorCells.ToHashSet();
        if (expected.Count == 0 || expected.Count != plan.Partition.FloorCells.Count ||
            expected.Count != plan.Materials.Tiles.Count ||
            expected.Count != plan.Packing.CellClaims.Count ||
            !expected.SetEquals(plan.Packing.CellClaims.Select(entry => entry.Cell)))
            return Failure(KsProcgenTileStageStatus.InvalidPlan, "InvalidTileStageCoverage");
        if (expected.Count > maxTiles)
            return Failure(KsProcgenTileStageStatus.BudgetExceeded, "TileStageCellBudget");

        var validated = new List<(Vector2i Cell, ushort TileId)>();
        var seen = new HashSet<Vector2i>();
        foreach (var choice in plan.Materials.Tiles.OrderBy(item => item.Cell.Y)
                     .ThenBy(item => item.Cell.X))
        {
            if (!expected.Contains(choice.Cell) || !seen.Add(choice.Cell))
                return Failure(KsProcgenTileStageStatus.InvalidPlan, "InvalidTileStageCoverage");
            if (string.IsNullOrWhiteSpace(choice.TileId) ||
                !_tileDefinitionManager.TryGetDefinition(choice.TileId, out var definition) ||
                definition.TileId == 0)
                return Failure(KsProcgenTileStageStatus.InvalidPlan, "UnknownTileStageTile");
            validated.Add((choice.Cell, definition.TileId));
        }

        var mapId = MapId.Nullspace;
        try
        {
            var mapUid = _mapSystem.CreateMap(out mapId, runMapInit: false);
            _mapSystem.SetPaused(mapId, true);
            var grid = _mapSystem.CreateGridEntity(mapId);
            foreach (var (cell, tileId) in validated)
                _mapSystem.SetTile(grid.Owner, grid.Comp, cell, new Tile(tileId));
            var stage = new KsProcgenTileStage(mapId, mapUid, grid.Owner,
                plan.SemanticHash, validated);
            _activeStages.Add(mapId, stage);
            return new KsProcgenTileStageResult
            {
                Status = KsProcgenTileStageStatus.Staged,
                Stage = stage,
            };
        }
        catch (Exception)
        {
            if (mapId != MapId.Nullspace && _mapSystem.MapExists(mapId))
                _mapSystem.DeleteMap(mapId);
            return Failure(KsProcgenTileStageStatus.EngineFailure, "TileStageEngineFailure");
        }
    }

    public bool Verify(KsProcgenTileStage stage)
    {
        if (stage == null || !stage.Active || !_activeStages.TryGetValue(stage.MapId, out var tracked) ||
            !ReferenceEquals(stage, tracked) ||
            !_mapSystem.TryGetMap(stage.MapId, out var mapUid) || mapUid != stage.MapUid ||
            !EntityManager.TryGetComponent<MapGridComponent>(stage.GridUid, out var grid))
            return false;
        foreach (var (cell, tileId) in stage.Tiles)
        {
            if (_mapSystem.GetTileRef(stage.GridUid, grid, cell).Tile.TypeId != tileId)
                return false;
        }
        return _mapSystem.IsPaused(stage.MapId);
    }

    public bool Discard(KsProcgenTileStage stage)
    {
        if (stage == null || !stage.Active || !_activeStages.TryGetValue(stage.MapId, out var tracked) ||
            !ReferenceEquals(stage, tracked))
            return false;
        _activeStages.Remove(stage.MapId);
        stage.Active = false;
        if (_mapSystem.TryGetMap(stage.MapId, out var mapUid) && mapUid == stage.MapUid)
            _mapSystem.DeleteMap(stage.MapId);
        return true;
    }

    /// <summary>
    /// Cancels all tile previews owned by this system. Round reset and system shutdown call this.
    /// </summary>
    public int DiscardAll()
    {
        var stages = _activeStages.Values.ToArray();
        foreach (var stage in stages)
            Discard(stage);
        return stages.Length;
    }

    private static KsProcgenTileStageResult Failure(KsProcgenTileStageStatus status, string code) => new()
    {
        Status = status,
        Issue = new KsProcgenIssue(code, "The plan cannot be placed in an isolated tile-only stage."),
    };
}
