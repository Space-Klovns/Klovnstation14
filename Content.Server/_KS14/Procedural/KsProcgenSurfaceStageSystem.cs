using System.Linq;
using System.Numerics;
using System.Threading;
using Content.Shared._KS14.Procedural;
using Content.Shared.GameTicking;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Procedural;

public enum KsProcgenSurfaceStageStatus : byte
{
    PositionedPreview,
    InvalidInput,
    BudgetExceeded,
    Rejected,
    EngineFailure,
    Cancelled,
    InitializedPreview,
}

public enum KsProcgenSurfaceStagePhase : byte
{
    Uninitialized,
    Initialized,
}

/// <summary>Owned drop-coordinate preview, not a mount, physical-contact or usable-front guarantee.</summary>
public sealed class KsProcgenSurfaceStage
{
    internal KsProcgenSurfaceStage(MapId mapId, EntityUid mapUid, EntityUid surfaceUid, EntityUid subjectUid,
        string surfacePrototypeId, string subjectPrototypeId, Vector2 dropPosition, int quarterTurns,
        List<EntityUid> spawnedEntities, int maxSpawnedEntities)
    {
        MapId = mapId;
        MapUid = mapUid;
        SurfaceUid = surfaceUid;
        SubjectUid = subjectUid;
        SurfacePrototypeId = surfacePrototypeId;
        SubjectPrototypeId = subjectPrototypeId;
        DropPosition = dropPosition;
        QuarterTurns = quarterTurns;
        SpawnedEntities = spawnedEntities;
        MaxSpawnedEntities = maxSpawnedEntities;
    }

    public MapId MapId { get; }
    public EntityUid MapUid { get; }
    public EntityUid SurfaceUid { get; }
    public EntityUid SubjectUid { get; }
    public Vector2 DropPosition { get; }
    public int QuarterTurns { get; }
    public bool Active { get; internal set; } = true;
    public KsProcgenSurfaceStagePhase Phase { get; internal set; }
    public bool PlacementVerified => false;
    public int SpawnedEntityCount => SpawnedEntities.Count;
    public int MaxSpawnedEntities { get; }
    internal string SurfacePrototypeId { get; }
    internal string SubjectPrototypeId { get; }
    internal List<EntityUid> SpawnedEntities { get; }
}

public sealed record KsProcgenSurfaceStageResult(
    KsProcgenSurfaceStageStatus Status, KsProcgenIssue? Issue, KsProcgenSurfaceStage? Stage);

/// <summary>
/// Spawns a disposable surface/item pair and applies the surface's centered drop offset (otherwise
/// a deterministic center click). No existing entities are borrowed; maps stay paused. Initialization
/// is explicit and verifies lifecycle/pose/filter state without simulating physical contacts.
/// No contact events are fabricated and no shared-XY floor placement permission is implied.
/// </summary>
public sealed partial class KsProcgenSurfaceStageSystem : EntitySystem
{
    public const int MaximumRetainedStages = 16;
    public const int MaximumSpawnedEntitiesPerStage = 256;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private KsProcgenSurfacePreflightSystem _preflightSystem = default!;
    [Dependency] private KsProcgenPreviewOperationSystem _operationSystem = default!;
    private readonly Dictionary<MapId, KsProcgenSurfaceStage> _stages = new();
    private List<EntityUid>? _capturedEntities;

    public int RetainedStageCount => _stages.Count;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
        EntityManager.EntityAdded += OnEntityAdded;
    }

    public override void Shutdown()
    {
        EntityManager.EntityAdded -= OnEntityAdded;
        DiscardAll();
        base.Shutdown();
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent args) => DiscardAll();
    private void OnEntityAdded(Entity<MetaDataComponent> entity) => _capturedEntities?.Add(entity.Owner);

    public KsProcgenSurfaceStageResult TryStage(string surfacePrototypeId, string subjectPrototypeId,
        int subjectQuarterTurns = 0, CancellationToken cancellationToken = default,
        int maxSpawnedEntities = MaximumSpawnedEntitiesPerStage)
    {
        if (_operationSystem.Active)
            return Failure(KsProcgenSurfaceStageStatus.InvalidInput, "SurfaceStageOperationActive");
        if (subjectQuarterTurns is < 0 or > 3 || maxSpawnedEntities is < 1 or > MaximumSpawnedEntitiesPerStage ||
            string.IsNullOrWhiteSpace(surfacePrototypeId) || surfacePrototypeId.Length > 256 ||
            string.IsNullOrWhiteSpace(subjectPrototypeId) || subjectPrototypeId.Length > 256 ||
            !ProtoMan.TryIndex<EntityPrototype>(surfacePrototypeId, out _) ||
            !ProtoMan.TryIndex<EntityPrototype>(subjectPrototypeId, out _))
            return Failure(KsProcgenSurfaceStageStatus.InvalidInput, "InvalidSurfaceStageInput");
        if (cancellationToken.IsCancellationRequested)
            return Failure(KsProcgenSurfaceStageStatus.Cancelled, "SurfaceStageCancelled");
        if (_stages.Count >= MaximumRetainedStages)
            return Failure(KsProcgenSurfaceStageStatus.BudgetExceeded, "SurfaceStageRetentionBudget");
        if (maxSpawnedEntities < 3)
            return Failure(KsProcgenSurfaceStageStatus.BudgetExceeded, "SurfaceStageSpawnBudget");
        if (!_operationSystem.TryEnter(this))
            return Failure(KsProcgenSurfaceStageStatus.InvalidInput, "SurfaceStageOperationActive");

        var mapId = MapId.Nullspace;
        var mapUid = EntityUid.Invalid;
        var spawnedEntities = new List<EntityUid>();
        var retained = false;
        try
        {
            _capturedEntities = spawnedEntities;
            cancellationToken.ThrowIfCancellationRequested();
            mapUid = _mapSystem.CreateMap(out mapId, runMapInit: false);
            _mapSystem.SetPaused(mapId, true);
            var surfaceUid = Spawn(surfacePrototypeId, new EntityCoordinates(mapUid, Vector2.Zero));
            cancellationToken.ThrowIfCancellationRequested();
            if (spawnedEntities.Count >= maxSpawnedEntities)
                return Failure(KsProcgenSurfaceStageStatus.BudgetExceeded, "SurfaceStageSpawnBudget");
            var subjectUid = Spawn(subjectPrototypeId, new EntityCoordinates(mapUid, 4f, 0f));
            cancellationToken.ThrowIfCancellationRequested();
            if (spawnedEntities.Count > maxSpawnedEntities)
                return Failure(KsProcgenSurfaceStageStatus.BudgetExceeded, "SurfaceStageSpawnBudget");
            var preflight = _preflightSystem.Check(subjectUid, surfaceUid);
            if (preflight.Status != KsProcgenSurfacePreflightStatus.Candidate)
                return Failure(preflight.Status == KsProcgenSurfacePreflightStatus.BudgetExceeded ?
                    KsProcgenSurfaceStageStatus.BudgetExceeded : KsProcgenSurfaceStageStatus.Rejected, preflight.Reason);
            var dropPosition = preflight.Centered ? preflight.PositionOffset : Vector2.Zero;
            // This primitive only accepts bounded drop offsets; it does not infer a surface footprint.
            if (Math.Abs(dropPosition.X) > 16f || Math.Abs(dropPosition.Y) > 16f)
                return Failure(KsProcgenSurfaceStageStatus.Rejected, "SurfaceStageOffsetOutOfRange");
            if (Transform(subjectUid).NoLocalRotation && subjectQuarterTurns != 0)
                return Failure(KsProcgenSurfaceStageStatus.Rejected, "SurfaceStageRotationUnsupported");
            _transformSystem.SetCoordinates(subjectUid, new EntityCoordinates(mapUid, dropPosition));
            _transformSystem.SetLocalRotation(subjectUid, Angle.FromDegrees((double) subjectQuarterTurns * 90.0));
            cancellationToken.ThrowIfCancellationRequested();
            if (spawnedEntities.Count > maxSpawnedEntities)
                return Failure(KsProcgenSurfaceStageStatus.BudgetExceeded, "SurfaceStageSpawnBudget");
            var stage = new KsProcgenSurfaceStage(mapId, mapUid, surfaceUid, subjectUid,
                surfacePrototypeId, subjectPrototypeId, dropPosition, subjectQuarterTurns, spawnedEntities, maxSpawnedEntities);
            var verificationIssue = ContentsIssue(stage);
            if (verificationIssue != null)
                return Failure(KsProcgenSurfaceStageStatus.Rejected, verificationIssue);
            cancellationToken.ThrowIfCancellationRequested();
            _stages.Add(mapId, stage);
            retained = true;
            return new(KsProcgenSurfaceStageStatus.PositionedPreview, null, stage);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failure(KsProcgenSurfaceStageStatus.Cancelled, "SurfaceStageCancelled");
        }
        catch (Exception)
        {
            return Failure(KsProcgenSurfaceStageStatus.EngineFailure, "SurfaceStageEngineFailure");
        }
        finally
        {
            _capturedEntities = null;
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

    private bool Owns(KsProcgenSurfaceStage stage) => stage != null && stage.Active &&
        _stages.TryGetValue(stage.MapId, out var owned) && ReferenceEquals(owned, stage);

    public bool Verify(KsProcgenSurfaceStage stage) => Owns(stage) && ContentsIssue(stage) == null;

    /// <summary>Run map initialization once, keeping the preview paused and discarding any invalid result.</summary>
    public KsProcgenSurfaceStageResult TryInitialize(KsProcgenSurfaceStage stage,
        CancellationToken cancellationToken = default)
    {
        if (!Owns(stage))
            return Failure(KsProcgenSurfaceStageStatus.InvalidInput, "SurfaceStageNotOwned");
        if (!_operationSystem.TryEnter(this))
            return Failure(KsProcgenSurfaceStageStatus.InvalidInput, "SurfaceStageOperationActive");
        try
        {
            _capturedEntities = stage.SpawnedEntities;
            cancellationToken.ThrowIfCancellationRequested();
            var preconditionIssue = ContentsIssue(stage);
            if (preconditionIssue != null)
            {
                DiscardAfterOperation(stage);
                return Failure(KsProcgenSurfaceStageStatus.Rejected, preconditionIssue);
            }
            if (stage.Phase == KsProcgenSurfaceStagePhase.Initialized)
                return new(KsProcgenSurfaceStageStatus.InitializedPreview, null, stage);
            _mapSystem.SetPaused(stage.MapId, true);
            _mapSystem.InitializeMap(stage.MapId, unpause: false);
            stage.Phase = KsProcgenSurfaceStagePhase.Initialized;
            cancellationToken.ThrowIfCancellationRequested();
            if (stage.SpawnedEntityCount > stage.MaxSpawnedEntities)
            {
                DiscardAfterOperation(stage);
                return Failure(KsProcgenSurfaceStageStatus.BudgetExceeded, "SurfaceStageInitializationSpawnBudget");
            }
            var verificationIssue = ContentsIssue(stage);
            if (verificationIssue != null)
            {
                DiscardAfterOperation(stage);
                return Failure(KsProcgenSurfaceStageStatus.Rejected, verificationIssue);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new(KsProcgenSurfaceStageStatus.InitializedPreview, null, stage);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DiscardAfterOperation(stage);
            return Failure(KsProcgenSurfaceStageStatus.Cancelled, "SurfaceStageInitializationCancelled");
        }
        catch (Exception)
        {
            DiscardAfterOperation(stage);
            return Failure(KsProcgenSurfaceStageStatus.EngineFailure, "SurfaceStageInitializationEngineFailure");
        }
        finally
        {
            _capturedEntities = null;
            _operationSystem.Exit(this);
        }
    }

    private void DiscardAfterOperation(KsProcgenSurfaceStage stage)
    {
        _capturedEntities = null;
        DiscardOwned(stage);
    }

    private string? ContentsIssue(KsProcgenSurfaceStage stage)
    {
        if (stage.SpawnedEntityCount > stage.MaxSpawnedEntities ||
            !_mapSystem.TryGetMap(stage.MapId, out var mapUid) || mapUid != stage.MapUid ||
            !_mapSystem.IsPaused(stage.MapId) ||
            _mapSystem.IsInitialized(stage.MapId) != (stage.Phase == KsProcgenSurfaceStagePhase.Initialized))
            return "SurfaceStageMapDrift";
        foreach (var spawnedUid in stage.SpawnedEntities)
        {
            if (spawnedUid != stage.MapUid && Exists(spawnedUid) &&
                (!TryComp<TransformComponent>(spawnedUid, out var generatedTransformComponent) ||
                    generatedTransformComponent.MapUid != stage.MapUid))
                return "SurfaceStageGeneratedEntityOffMap";
        }
        if (!Member(stage.SurfaceUid, stage.SurfacePrototypeId, Vector2.Zero))
            return "SurfaceStageSurfaceDrift";
        if (!Member(stage.SubjectUid, stage.SubjectPrototypeId, stage.DropPosition))
            return "SurfaceStageSubjectDrift";
        if (Transform(stage.SubjectUid).LocalRotation != Angle.FromDegrees((double) stage.QuarterTurns * 90.0))
            return "SurfaceStageRotationDrift";
        var preflight = _preflightSystem.Check(stage.SubjectUid, stage.SurfaceUid);
        if (preflight.Status != KsProcgenSurfacePreflightStatus.Candidate)
            return preflight.Reason;
        return (preflight.Centered ? preflight.PositionOffset : Vector2.Zero) == stage.DropPosition ?
            null : "SurfaceStageDropOffsetDrift";

        bool Member(EntityUid uid, string prototypeId, Vector2 position) =>
            TryComp<MetaDataComponent>(uid, out var metadataComponent) &&
            metadataComponent.EntityLifeStage == (stage.Phase == KsProcgenSurfaceStagePhase.Initialized ?
                EntityLifeStage.MapInitialized : EntityLifeStage.Initialized) &&
            metadataComponent.EntityPrototype?.ID == prototypeId && !EntityManager.IsQueuedForDeletion(uid) &&
            TryComp<TransformComponent>(uid, out var transformComponent) &&
            transformComponent.ParentUid == stage.MapUid && transformComponent.LocalPosition == position;
    }

    public bool Discard(KsProcgenSurfaceStage stage)
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

    private bool DiscardOwned(KsProcgenSurfaceStage stage)
    {
        if (!Owns(stage))
            return false;
        _stages.Remove(stage.MapId);
        stage.Active = false;
        Cleanup(stage.MapId, stage.MapUid, stage.SpawnedEntities);
        return true;
    }

    public int DiscardAll()
    {
        if (_operationSystem.Active)
            return 0;
        var stages = _stages.Values.ToArray();
        foreach (var stage in stages)
            Discard(stage);
        return stages.Length;
    }

    private void Cleanup(MapId mapId, EntityUid mapUid, IEnumerable<EntityUid> spawnedEntities)
    {
        foreach (var spawnedUid in spawnedEntities.Reverse().ToArray())
        {
            if (Exists(spawnedUid))
                Del(spawnedUid);
        }
        if (_mapSystem.TryGetMap(mapId, out var actualMapUid) && actualMapUid == mapUid)
            _mapSystem.DeleteMap(mapId);
    }

    private static KsProcgenSurfaceStageResult Failure(KsProcgenSurfaceStageStatus status, string code) =>
        new(status, new KsProcgenIssue(code, "Surface drop-coordinate preview could not be retained."), null);
}
