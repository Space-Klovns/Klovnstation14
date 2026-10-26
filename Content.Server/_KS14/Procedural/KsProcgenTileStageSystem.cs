using System.Linq;
using System.Threading;
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
    Cancelled,
}

/// <summary>
/// An uninitialized, paused map containing only generated floor tiles. Publication and
/// visibility isolation are not established by this preview stage.
/// Only the owning stage system may discard it.
/// </summary>
public sealed class KsProcgenTileStage : KsProcgenStageHandle
{
    internal KsProcgenTileStage(MapId mapId, EntityUid mapUid, EntityUid gridUid,
        ulong semanticHash, IReadOnlyList<(Vector2i Cell, ushort TileId)> tiles,
        List<EntityUid> allocations, int maxSpawnedEntities)
        : base(mapId, mapUid, KsProcgenStageKind.Tiles, allocations, maxSpawnedEntities)
    {
        GridUid = gridUid;
        SemanticHash = semanticHash;
        Tiles = tiles;
    }

    public EntityUid GridUid { get; }
    public ulong SemanticHash { get; }
    public int StagedCells => Tiles.Count;
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
    public const int MaximumRetainedStages = KsProcgenStageOwnershipSystem.MaximumRetainedPerKind;
    public const int MaximumSpawnedEntitiesPerStage = KsProcgenPreviewOperationSystem.MaximumAllocationsPerStage;
    [Dependency] private ITileDefinitionManager _tileDefinitionManager = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;

    [Dependency] private KsProcgenStageOwnershipSystem _ownership = default!;
    [Dependency] private KsProcgenPreviewOperationSystem _operationSystem = default!;
    public int RetainedStageCount => _ownership.Count(KsProcgenStageKind.Tiles);

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
        int maxTiles = 4_096, int maxSpawnedEntities = MaximumSpawnedEntitiesPerStage,
        CancellationToken cancellationToken = default)
    {
        if (_operationSystem.Active)
            return Failure(KsProcgenTileStageStatus.InvalidPlan, "TileStageOperationActive");
        if (cancellationToken.IsCancellationRequested)
            return Failure(KsProcgenTileStageStatus.Cancelled, "TileStageCancelled");
        if (!_ownership.CanRetain(KsProcgenStageKind.Tiles))
            return Failure(KsProcgenTileStageStatus.BudgetExceeded, "TileStageRetentionBudget");
        if (maxSpawnedEntities is < 1 or > MaximumSpawnedEntitiesPerStage)
            return Failure(KsProcgenTileStageStatus.InvalidPlan, "InvalidTileStageSpawnBudget");
        if (maxSpawnedEntities < 2)
            return Failure(KsProcgenTileStageStatus.BudgetExceeded, "TileStageSpawnBudget");
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
            plan.Furnishings.Any(item => item.Proposal.Entities.Count != 0 || item.Proposal.Layered != null) ||
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
        var mapUid = EntityUid.Invalid;
        var allocations = new List<EntityUid>();
        var retained = false;
        if (!_operationSystem.TryEnter(this))
            return Failure(KsProcgenTileStageStatus.InvalidPlan, "TileStageOperationActive");
        try
        {
            _operationSystem.SetCapture(this, allocations);
            cancellationToken.ThrowIfCancellationRequested();
            mapUid = _mapSystem.CreateMap(out mapId, runMapInit: false);
            _mapSystem.SetPaused(mapId, true);
            if (allocations.Count >= maxSpawnedEntities)
                return Failure(KsProcgenTileStageStatus.BudgetExceeded, "TileStageSpawnBudget");
            var grid = _mapSystem.CreateGridEntity(mapId);
            foreach (var (cell, tileId) in validated)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (allocations.Count > maxSpawnedEntities)
                    return Failure(KsProcgenTileStageStatus.BudgetExceeded, "TileStageSpawnBudget");
                _mapSystem.SetTile(grid.Owner, grid.Comp, cell, new Tile(tileId));
                cancellationToken.ThrowIfCancellationRequested();
                if (allocations.Count > maxSpawnedEntities)
                    return Failure(KsProcgenTileStageStatus.BudgetExceeded, "TileStageSpawnBudget");
            }
            if (allocations.Any(uid => uid != mapUid && uid != grid.Owner && Exists(uid)))
                return Failure(KsProcgenTileStageStatus.UnsupportedContent, "TileStageUnexpectedAllocation");
            var stage = new KsProcgenTileStage(mapId, mapUid, grid.Owner,
                plan.SemanticHash, validated, allocations, maxSpawnedEntities);
            _ownership.Attach(stage);
            retained = true;
            return new KsProcgenTileStageResult
            {
                Status = KsProcgenTileStageStatus.Staged,
                Stage = stage,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failure(KsProcgenTileStageStatus.Cancelled, "TileStageCancelled");
        }
        catch (Exception)
        {
            return Failure(KsProcgenTileStageStatus.EngineFailure, "TileStageEngineFailure");
        }
        finally
        {
            _operationSystem.SetCapture(this, null);
            try { if (!retained) _ownership.Cleanup(mapId, mapUid, allocations); }
            finally { _operationSystem.Exit(this); }
        }
    }

    public bool Verify(KsProcgenTileStage stage)
    {
        if (!_ownership.Owns(stage) || EntityManager.IsQueuedForDeletion(stage.MapUid) ||
            stage.SpawnedEntityCount > stage.MaxSpawnedEntities ||
            stage.SpawnedEntities.Any(uid => uid != stage.MapUid && uid != stage.GridUid && Exists(uid)) ||
            !_mapSystem.TryGetMap(stage.MapId, out var mapUid) || mapUid != stage.MapUid ||
            !TryComp<MapGridComponent>(stage.GridUid, out var grid))
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
        if (!_ownership.Owns(stage) || !_operationSystem.TryEnter(this)) return false;
        try { return _ownership.Release(stage); }
        finally { _operationSystem.Exit(this); }
    }

    /// <summary>
    /// Cancels all tile previews owned by this system. Round reset and system shutdown call this.
    /// </summary>
    public int DiscardAll()
    {
        if (_operationSystem.Active) return 0;
        var stages = _ownership.Snapshot(KsProcgenStageKind.Tiles).Cast<KsProcgenTileStage>().ToArray();
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
