using System.Collections.ObjectModel;
using System.Collections.Frozen;
using System.Linq;
using System.Numerics;
using System.Threading;
using Content.Shared._KS14.Procedural;
using Content.Shared.GameTicking;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Server._KS14.Procedural;

public enum KsProcgenAssemblyStageStatus : byte
{
    PositionedPreview,
    InitializedPreview,
    InvalidInput,
    BudgetExceeded,
    UnsupportedContent,
    Rejected,
    EngineFailure,
    Cancelled,
}

public enum KsProcgenAssemblyStagePhase : byte
{
    Uninitialized,
    Initialized,
}

/// <summary>Owned transforms and container contents; no surface mounting or usable-room proof.</summary>
public sealed class KsProcgenAssemblyStage : KsProcgenStageHandle
{
    internal KsProcgenAssemblyStage(MapId mapId, EntityUid mapUid, KsProcgenAssemblyPosePlan poses,
        Dictionary<string, EntityUid> members, List<EntityUid> spawnedEntities, int maxSpawnedEntities,
        KsProcgenSupportedAccessPlan? access)
        : base(mapId, mapUid, KsProcgenStageKind.Assembly, spawnedEntities, maxSpawnedEntities)
    {
        Poses = poses;
        Members = new ReadOnlyDictionary<string, EntityUid>(members);
        Access = access;
    }

    public EntityUid? GridUid { get; internal set; }
    public EntityUid CoordinateFrame => GridUid ?? MapUid;
    public KsProcgenRoomTerrain? Terrain { get; internal set; }
    public FrozenSet<Vector2i> DeclaredBlockingCells { get; internal set; } = FrozenSet<Vector2i>.Empty;
    internal Dictionary<Vector2i, EntityUid> Walls { get; } = new();
    public KsProcgenAssemblyPosePlan Poses { get; }
    /// <summary>Optional declared room geometry, not live engine reach/collision validation.</summary>
    public KsProcgenSupportedAccessPlan? Access { get; }
    public IReadOnlyDictionary<string, EntityUid> Members { get; }
    public KsProcgenAssemblyStagePhase Phase => InitializationPhase == KsProcgenStageInitializationPhase.Initialized
        ? KsProcgenAssemblyStagePhase.Initialized : KsProcgenAssemblyStagePhase.Uninitialized;
    public bool PlacementVerified => false;
}

public sealed record KsProcgenAssemblyStageResult(
    KsProcgenAssemblyStageStatus Status, KsProcgenIssue? Issue, KsProcgenAssemblyStage? Stage);

/// <summary>
/// Disposable support-only assembly preview using fresh declarations and exact proposed transforms.
/// Owns all members, generated entities and its paused map. Real insertion never swaps contents.
/// Surface drops only set coordinates: contacts, mounting, traversal and publication remain unverified.
/// </summary>
public sealed partial class KsProcgenAssemblyStageSystem : EntitySystem
{
    public const int MaximumRetainedStages = KsProcgenStageOwnershipSystem.MaximumRetainedPerKind;
    public const int MaximumSpawnedEntitiesPerStage = KsProcgenPreviewOperationSystem.MaximumAllocationsPerStage;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private KsProcgenPreviewOperationSystem _operationSystem = default!;
    [Dependency] private KsProcgenStageOwnershipSystem _ownership = default!;
    [Dependency] private KsProcgenStageLifecycleSystem _lifecycle = default!;
    [Dependency] private KsProcgenAssemblyPlacementSystem _assemblyPlacementSystem = default!;

    public int RetainedStageCount => _ownership.Count(KsProcgenStageKind.Assembly);

    public override void Shutdown()
    {
        DiscardAll();
        base.Shutdown();
    }

    [SubscribeLocalEvent]
    private void OnRoundRestartCleanup(RoundRestartCleanupEvent args) => DiscardAll();


    public KsProcgenAssemblyStageResult TryStage(KsProcgenResolvedAssembly assembly,
        IReadOnlyCollection<string> selectedMemberIds, IReadOnlyList<KsProcgenFloorRootPose> floorRoots,
        IReadOnlyList<KsProcgenMemberOrientation> orientations, int assemblyQuarterTurns,
        CancellationToken cancellationToken = default, int maxMembers = 64,
        int maxSpawnedEntities = MaximumSpawnedEntitiesPerStage,
        KsProcgenRoomAccessMask? accessMask = null, int maximumAccessExpandedCells = 4096,
        int maximumLandingProbes = 4096)
    {
        if (_operationSystem.Active)
            return Failure(KsProcgenAssemblyStageStatus.InvalidInput, "AssemblyStageOperationActive");
        if (maxMembers is < 1 or > 64 || maxSpawnedEntities is < 1 or > MaximumSpawnedEntitiesPerStage ||
            maximumAccessExpandedCells is < 0 or > 65_536 || maximumLandingProbes is < 0 or > 65_536)
            return Failure(KsProcgenAssemblyStageStatus.InvalidInput, "InvalidAssemblyStageBudget");
        if (selectedMemberIds.Count > maxMembers)
            return Failure(KsProcgenAssemblyStageStatus.BudgetExceeded, "AssemblyStageMemberBudget");
        if (cancellationToken.IsCancellationRequested)
            return Failure(KsProcgenAssemblyStageStatus.Cancelled, "AssemblyStageCancelled");
        if (!_ownership.CanRetain(KsProcgenStageKind.Assembly))
            return Failure(KsProcgenAssemblyStageStatus.BudgetExceeded, "AssemblyStageRetentionBudget");
        if (selectedMemberIds.Count + 1 > maxSpawnedEntities)
            return Failure(KsProcgenAssemblyStageStatus.BudgetExceeded, "AssemblyStageSpawnBudget");
        if (!KsProcgenPrototypeCapabilityInspector.TryInspectAssembly(ProtoMan, Factory,
                assembly, out var capabilities, out var issue))
            return Failure(KsProcgenAssemblyStageStatus.InvalidInput, issue!.Code);
        KsProcgenSupportedAccessPlan? access = null;
        KsProcgenAssemblyPosePlan poses;
        if (accessMask != null)
        {
            access = KsProcgenSupportedAccessPlanner.Plan(assembly, selectedMemberIds, capabilities!,
                floorRoots, orientations, assemblyQuarterTurns, accessMask,
                maximumExpandedCells: maximumAccessExpandedCells, maximumLandingProbes: maximumLandingProbes);
            if (access.Status != KsProcgenSupportedAccessStatus.Candidate)
                return Failure(access.Status switch
                {
                    KsProcgenSupportedAccessStatus.InvalidInput => KsProcgenAssemblyStageStatus.InvalidInput,
                    KsProcgenSupportedAccessStatus.UnsupportedContent => KsProcgenAssemblyStageStatus.UnsupportedContent,
                    KsProcgenSupportedAccessStatus.BudgetExceeded => KsProcgenAssemblyStageStatus.BudgetExceeded,
                    _ => KsProcgenAssemblyStageStatus.Rejected,
                }, access.Issue!.Code);
            poses = access.Poses!;
        }
        else
            poses = KsProcgenAssemblyPosePlanner.Plan(assembly, selectedMemberIds, capabilities!,
                floorRoots, orientations, assemblyQuarterTurns);
        if (poses.Status != KsProcgenAssemblySupportStatus.NeedsEngineValidation)
            return Failure(poses.Status == KsProcgenAssemblySupportStatus.InvalidInput ?
                KsProcgenAssemblyStageStatus.InvalidInput : KsProcgenAssemblyStageStatus.Rejected, poses.Issue!.Code);
        return StagePrepared(assembly, selectedMemberIds, poses, access, cancellationToken, maxSpawnedEntities);
    }

    /// <summary>Searches fresh declarations before allocating a preview. No engine-driven retry or publication.</summary>
    public KsProcgenAssemblyStageResult TrySearchStage(KsProcgenResolvedAssembly assembly,
        IReadOnlyCollection<string> selectedMemberIds, KsProcgenRoomAccessMask accessMask, int seed,
        IReadOnlyList<KsProcgenFloorRootPose>? fixedFloorRoots = null,
        CancellationToken cancellationToken = default, int maxMembers = 64,
        int maxSpawnedEntities = MaximumSpawnedEntitiesPerStage,
        int maximumPlacementProbes = 4096, int maximumAccessExpandedCells = 4096,
        int maximumLandingProbes = 4096)
    {
        if (_operationSystem.Active)
            return Failure(KsProcgenAssemblyStageStatus.InvalidInput, "AssemblyStageOperationActive");
        if (maxMembers is < 1 or > 64 || maxSpawnedEntities is < 1 or > MaximumSpawnedEntitiesPerStage ||
            maximumPlacementProbes is < 0 or > 65_536 || maximumAccessExpandedCells is < 0 or > 65_536 ||
            maximumLandingProbes is < 0 or > 65_536)
            return Failure(KsProcgenAssemblyStageStatus.InvalidInput, "InvalidAssemblyStageBudget");
        if (selectedMemberIds.Count > maxMembers)
            return Failure(KsProcgenAssemblyStageStatus.BudgetExceeded, "AssemblyStageMemberBudget");
        if (cancellationToken.IsCancellationRequested)
            return Failure(KsProcgenAssemblyStageStatus.Cancelled, "AssemblyStageCancelled");
        if (!_ownership.CanRetain(KsProcgenStageKind.Assembly))
            return Failure(KsProcgenAssemblyStageStatus.BudgetExceeded, "AssemblyStageRetentionBudget");
        if (selectedMemberIds.Count + 1 > maxSpawnedEntities)
            return Failure(KsProcgenAssemblyStageStatus.BudgetExceeded, "AssemblyStageSpawnBudget");
        if (!KsProcgenPrototypeCapabilityInspector.TryInspectAssembly(ProtoMan, Factory,
                assembly, out var capabilities, out var issue))
            return Failure(KsProcgenAssemblyStageStatus.InvalidInput, issue!.Code);
        var search = KsProcgenSupportedAssemblySearch.Search(assembly, selectedMemberIds, capabilities!, accessMask, seed,
            fixedFloorRoots: fixedFloorRoots, maximumPlacementProbes: maximumPlacementProbes,
            maximumExpandedCells: maximumAccessExpandedCells, maximumLandingProbes: maximumLandingProbes);
        if (search.Status != KsProcgenSupportedAccessStatus.Candidate)
            return Failure(search.Status switch
            {
                KsProcgenSupportedAccessStatus.InvalidInput => KsProcgenAssemblyStageStatus.InvalidInput,
                KsProcgenSupportedAccessStatus.UnsupportedContent => KsProcgenAssemblyStageStatus.UnsupportedContent,
                KsProcgenSupportedAccessStatus.BudgetExceeded => KsProcgenAssemblyStageStatus.BudgetExceeded,
                _ => KsProcgenAssemblyStageStatus.Rejected,
            }, search.Issue!.Code);
        // Consume the internally produced witness directly; do not reset and spend its path budget again.
        return StagePrepared(assembly, selectedMemberIds, search.Access!.Poses!, search.Access,
            cancellationToken, maxSpawnedEntities);
    }

    private KsProcgenAssemblyStageResult StagePrepared(KsProcgenResolvedAssembly assembly,
        IReadOnlyCollection<string> selectedMemberIds, KsProcgenAssemblyPosePlan poses,
        KsProcgenSupportedAccessPlan? access, CancellationToken cancellationToken, int maxSpawnedEntities,
        KsProcgenRoomTerrain? terrain = null)
    {
        if (assembly.Relations.Any(relation => selectedMemberIds.Contains(relation.Subject) &&
                relation.Kind is not (KsProcgenRelationKind.OnSurface or KsProcgenRelationKind.InContainer)))
            return Failure(KsProcgenAssemblyStageStatus.UnsupportedContent, "UnsupportedAssemblyStageSpatialRelations");
        if (!_operationSystem.TryEnter(this))
            return Failure(KsProcgenAssemblyStageStatus.InvalidInput, "AssemblyStageOperationActive");

        var mapId = MapId.Nullspace;
        var mapUid = EntityUid.Invalid;
        var members = new Dictionary<string, EntityUid>(StringComparer.Ordinal);
        var spawnedEntities = new List<EntityUid>();
        var retained = false;
        try
        {
            _operationSystem.SetCapture(this, spawnedEntities);
            cancellationToken.ThrowIfCancellationRequested();
            mapUid = _mapSystem.CreateMap(out mapId, runMapInit: false);
            _mapSystem.SetPaused(mapId, true);
            var frameUid = mapUid;
            EntityUid? gridUid = null;
            var walls = new Dictionary<Vector2i, EntityUid>();
            if (terrain != null)
            {
                var grid = _mapSystem.CreateGridEntity(mapId);
                gridUid = frameUid = grid.Owner;
                if (spawnedEntities.Count > maxSpawnedEntities)
                    return Failure(KsProcgenAssemblyStageStatus.BudgetExceeded, "AssemblyStageSpawnBudget");
                foreach (var cell in terrain.Floor.Concat(terrain.Walls).OrderBy(cell => cell.Y).ThenBy(cell => cell.X))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _mapSystem.SetTile(grid.Owner, grid.Comp, cell, new Tile(terrain.TileId));
                    // Tile callbacks can allocate entities; stop before invoking another callback.
                    if (spawnedEntities.Count > maxSpawnedEntities)
                        return Failure(KsProcgenAssemblyStageStatus.BudgetExceeded, "AssemblyStageSpawnBudget");
                }
                foreach (var cell in terrain.Walls.OrderBy(cell => cell.Y).ThenBy(cell => cell.X))
                {
                    if (spawnedEntities.Count >= maxSpawnedEntities)
                        return Failure(KsProcgenAssemblyStageStatus.BudgetExceeded, "AssemblyStageSpawnBudget");
                    cancellationToken.ThrowIfCancellationRequested();
                    walls.Add(cell, Spawn(terrain.WallPrototypeId,
                        new EntityCoordinates(frameUid, new Vector2(cell.X + 0.5f, cell.Y + 0.5f))));
                    if (spawnedEntities.Count > maxSpawnedEntities)
                        return Failure(KsProcgenAssemblyStageStatus.BudgetExceeded, "AssemblyStageSpawnBudget");
                }
            }
            var placementIssue = _assemblyPlacementSystem.Place(poses, frameUid, members, spawnedEntities,
                maxSpawnedEntities, cancellationToken);
            if (placementIssue != null)
                return Failure(placementIssue.Status switch
                {
                    KsProcgenAssemblyPlacementStatus.BudgetExceeded => KsProcgenAssemblyStageStatus.BudgetExceeded,
                    KsProcgenAssemblyPlacementStatus.InvalidInput => KsProcgenAssemblyStageStatus.InvalidInput,
                    _ => KsProcgenAssemblyStageStatus.Rejected,
                }, placementIssue.Code);
            cancellationToken.ThrowIfCancellationRequested();
            if (spawnedEntities.Count > maxSpawnedEntities)
                return Failure(KsProcgenAssemblyStageStatus.BudgetExceeded, "AssemblyStageSpawnBudget");
            var stage = new KsProcgenAssemblyStage(mapId, mapUid, poses, members, spawnedEntities, maxSpawnedEntities, access);
            stage.GridUid = gridUid;
            stage.Terrain = terrain;
            if (access != null)
            {
                var definitions = assembly.Members.ToDictionary(member => member.Id, StringComparer.Ordinal);
                stage.DeclaredBlockingCells = access.Footprints.Where(footprint =>
                    definitions[footprint.MemberId].Entry.Movement != KsProcgenMovementClass.Clear)
                    .SelectMany(footprint => footprint.Cells).ToFrozenSet();
            }
            foreach (var (cell, wallUid) in walls)
                stage.Walls.Add(cell, wallUid);
            var verificationIssue = ContentsIssue(stage);
            if (verificationIssue != null)
                return VerificationFailure(verificationIssue);
            _ownership.Attach(stage);
            retained = true;
            return new(KsProcgenAssemblyStageStatus.PositionedPreview, null, stage);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failure(KsProcgenAssemblyStageStatus.Cancelled, "AssemblyStageCancelled");
        }
        catch (Exception)
        {
            return Failure(KsProcgenAssemblyStageStatus.EngineFailure, "AssemblyStageEngineFailure");
        }
        finally
        {
            _operationSystem.SetCapture(this, null);
            try
            {
                if (!retained)
                    Cleanup(mapId, mapUid, spawnedEntities);
            }
            finally
            {
                _operationSystem.Exit(this);
            }
        }
    }

    private bool Owns(KsProcgenAssemblyStage stage) => _ownership.Owns(stage);

    public bool Verify(KsProcgenAssemblyStage stage) => Owns(stage) && ContentsIssue(stage) == null;

    public KsProcgenAssemblyStageResult TryInitialize(KsProcgenAssemblyStage stage,
        CancellationToken cancellationToken = default)
    {
        var result = _lifecycle.TryInitialize(stage, "AssemblyStage", _ => ContentsIssue(stage), cancellationToken);
        if (result.Status == KsProcgenStageInitializationStatus.Initialized)
            return new(KsProcgenAssemblyStageStatus.InitializedPreview, null, stage);
        if (result.Status == KsProcgenStageInitializationStatus.Rejected)
            return VerificationFailure(result.IssueCode!);
        return Failure(result.Status switch
        {
            KsProcgenStageInitializationStatus.InvalidInput => KsProcgenAssemblyStageStatus.InvalidInput,
            KsProcgenStageInitializationStatus.BudgetExceeded => KsProcgenAssemblyStageStatus.BudgetExceeded,
            KsProcgenStageInitializationStatus.Cancelled => KsProcgenAssemblyStageStatus.Cancelled,
            _ => KsProcgenAssemblyStageStatus.EngineFailure,
        }, result.IssueCode!);
    }

    private string? ContentsIssue(KsProcgenAssemblyStage stage)
    {
        if (stage.WasRetained && !Owns(stage)) return "AssemblyStageNotOwned";
        if (stage.SpawnedEntityCount > stage.MaxSpawnedEntities)
            return "AssemblyStageSpawnBudget";
        if (!_mapSystem.TryGetMap(stage.MapId, out var mapUid) || mapUid != stage.MapUid ||
            EntityManager.IsQueuedForDeletion(stage.MapUid) || !_mapSystem.IsPaused(stage.MapId) ||
            _mapSystem.IsInitialized(stage.MapId) != (stage.Phase == KsProcgenAssemblyStagePhase.Initialized))
            return "AssemblyStageMapDrift";
        var terrainIssue = TerrainIssue(stage);
        if (terrainIssue != null)
            return terrainIssue;
        foreach (var spawnedUid in stage.SpawnedEntities)
        {
            if (spawnedUid != mapUid && Exists(spawnedUid) &&
                (!TryComp(spawnedUid, out TransformComponent? generatedTransformComponent) || generatedTransformComponent.MapUid != mapUid))
                return "AssemblyStageGeneratedEntityOffMap";
        }
        return _assemblyPlacementSystem.MembersIssue(stage.Poses, stage.Members, stage.CoordinateFrame,
            initialized: stage.Phase == KsProcgenAssemblyStagePhase.Initialized);
    }

    public bool Discard(KsProcgenAssemblyStage stage)
    {
        if (!Owns(stage) || !_operationSystem.TryEnter(this))
            return false;
        try
        {
            return DiscardOwned(stage);
        }
        finally
        {
            _operationSystem.Exit(this);
        }
    }

    private bool DiscardOwned(KsProcgenAssemblyStage stage) => _ownership.Release(stage);

    public int DiscardAll()
    {
        if (_operationSystem.Active)
            return 0;
        var stages = _ownership.Snapshot(KsProcgenStageKind.Assembly).Cast<KsProcgenAssemblyStage>().ToArray();
        foreach (var stage in stages)
            Discard(stage);
        return stages.Length;
    }

    private void Cleanup(MapId mapId, EntityUid mapUid, IEnumerable<EntityUid> spawnedEntities)
        => _ownership.Cleanup(mapId, mapUid, spawnedEntities);

    private static KsProcgenAssemblyStageResult Failure(KsProcgenAssemblyStageStatus status, string code) =>
        new(status, new KsProcgenIssue(code, "Supported assembly transform preview could not be retained."), null);

    private static KsProcgenAssemblyStageResult VerificationFailure(string code) =>
        Failure(code is "AssemblyStageSpawnBudget" or "SurfaceTrackerBudget" ?
            KsProcgenAssemblyStageStatus.BudgetExceeded : KsProcgenAssemblyStageStatus.Rejected, code);
}
