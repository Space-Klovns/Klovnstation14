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
public sealed class KsProcgenSurfaceStage : KsProcgenStageHandle
{
    internal KsProcgenSurfaceStage(MapId mapId, EntityUid mapUid, EntityUid surfaceUid, EntityUid subjectUid,
        string surfacePrototypeId, string subjectPrototypeId, Vector2 dropPosition, int quarterTurns,
        List<EntityUid> spawnedEntities, int maxSpawnedEntities)
        : base(mapId, mapUid, KsProcgenStageKind.Surface, spawnedEntities, maxSpawnedEntities)
    {
        SurfaceUid = surfaceUid;
        SubjectUid = subjectUid;
        SurfacePrototypeId = surfacePrototypeId;
        SubjectPrototypeId = subjectPrototypeId;
        DropPosition = dropPosition;
        QuarterTurns = quarterTurns;
    }

    public EntityUid SurfaceUid { get; }
    public EntityUid SubjectUid { get; }
    public Vector2 DropPosition { get; }
    public int QuarterTurns { get; }
    public KsProcgenSurfaceStagePhase Phase => InitializationPhase == KsProcgenStageInitializationPhase.Initialized
        ? KsProcgenSurfaceStagePhase.Initialized : KsProcgenSurfaceStagePhase.Uninitialized;
    public bool PlacementVerified => false;
    internal string SurfacePrototypeId { get; }
    internal string SubjectPrototypeId { get; }
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
    public const int MaximumRetainedStages = KsProcgenStageOwnershipSystem.MaximumRetainedPerKind;
    public const int MaximumSpawnedEntitiesPerStage = KsProcgenPreviewOperationSystem.MaximumAllocationsPerStage;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private KsProcgenSurfacePreflightSystem _preflightSystem = default!;
    [Dependency] private KsProcgenPreviewOperationSystem _operationSystem = default!;
    [Dependency] private KsProcgenStageOwnershipSystem _ownership = default!;
    [Dependency] private KsProcgenStageLifecycleSystem _lifecycle = default!;

    public int RetainedStageCount => _ownership.Count(KsProcgenStageKind.Surface);

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

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent args) => DiscardAll();

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
        if (!_ownership.CanRetain(KsProcgenStageKind.Surface))
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
            _operationSystem.SetCapture(this, spawnedEntities);
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
            _transformSystem.SetLocalRotation(subjectUid, KsProcgenRotation.ToEngineAngle(subjectQuarterTurns));
            cancellationToken.ThrowIfCancellationRequested();
            if (spawnedEntities.Count > maxSpawnedEntities)
                return Failure(KsProcgenSurfaceStageStatus.BudgetExceeded, "SurfaceStageSpawnBudget");
            var stage = new KsProcgenSurfaceStage(mapId, mapUid, surfaceUid, subjectUid,
                surfacePrototypeId, subjectPrototypeId, dropPosition, subjectQuarterTurns, spawnedEntities, maxSpawnedEntities);
            var verificationIssue = ContentsIssue(stage);
            if (verificationIssue != null)
                return Failure(KsProcgenSurfaceStageStatus.Rejected, verificationIssue);
            cancellationToken.ThrowIfCancellationRequested();
            _ownership.Attach(stage);
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

    private bool Owns(KsProcgenSurfaceStage stage) => _ownership.Owns(stage);

    public bool Verify(KsProcgenSurfaceStage stage) => Owns(stage) && ContentsIssue(stage) == null;

    /// <summary>Run map initialization once, keeping the preview paused and discarding any invalid result.</summary>
    public KsProcgenSurfaceStageResult TryInitialize(KsProcgenSurfaceStage stage,
        CancellationToken cancellationToken = default)
    {
        var result = _lifecycle.TryInitialize(stage, "SurfaceStage", _ => ContentsIssue(stage), cancellationToken);
        if (result.Status == KsProcgenStageInitializationStatus.Initialized)
            return new(KsProcgenSurfaceStageStatus.InitializedPreview, null, stage);
        return Failure(result.Status switch
        {
            KsProcgenStageInitializationStatus.InvalidInput => KsProcgenSurfaceStageStatus.InvalidInput,
            KsProcgenStageInitializationStatus.Rejected => KsProcgenSurfaceStageStatus.Rejected,
            KsProcgenStageInitializationStatus.BudgetExceeded => KsProcgenSurfaceStageStatus.BudgetExceeded,
            KsProcgenStageInitializationStatus.Cancelled => KsProcgenSurfaceStageStatus.Cancelled,
            _ => KsProcgenSurfaceStageStatus.EngineFailure,
        }, result.IssueCode!);
    }

    private string? ContentsIssue(KsProcgenSurfaceStage stage)
    {
        if (stage.WasRetained && !Owns(stage)) return "SurfaceStageNotOwned";
        if (EntityManager.IsQueuedForDeletion(stage.MapUid)) return "SurfaceStageMapDrift";
        if (stage.SpawnedEntityCount > stage.MaxSpawnedEntities ||
            !_mapSystem.TryGetMap(stage.MapId, out var mapUid) || mapUid != stage.MapUid ||
            !_mapSystem.IsPaused(stage.MapId) ||
            _mapSystem.IsInitialized(stage.MapId) != (stage.Phase == KsProcgenSurfaceStagePhase.Initialized))
            return "SurfaceStageMapDrift";
        foreach (var spawnedUid in stage.SpawnedEntities)
        {
            if (spawnedUid != stage.MapUid && Exists(spawnedUid) &&
                (!TryComp(spawnedUid, out TransformComponent? generatedTransformComponent) ||
                    generatedTransformComponent.MapUid != stage.MapUid))
                return "SurfaceStageGeneratedEntityOffMap";
        }
        if (!Member(stage.SurfaceUid, stage.SurfacePrototypeId, Vector2.Zero))
            return "SurfaceStageSurfaceDrift";
        if (!Member(stage.SubjectUid, stage.SubjectPrototypeId, stage.DropPosition))
            return "SurfaceStageSubjectDrift";
        if (Transform(stage.SubjectUid).LocalRotation != KsProcgenRotation.ToEngineAngle(stage.QuarterTurns))
            return "SurfaceStageRotationDrift";
        var preflight = _preflightSystem.Check(stage.SubjectUid, stage.SurfaceUid);
        if (preflight.Status != KsProcgenSurfacePreflightStatus.Candidate)
            return preflight.Reason;
        return (preflight.Centered ? preflight.PositionOffset : Vector2.Zero) == stage.DropPosition ?
            null : "SurfaceStageDropOffsetDrift";

        bool Member(EntityUid uid, string prototypeId, Vector2 position) =>
            TryComp(uid, out MetaDataComponent? metadataComponent) &&
            metadataComponent.EntityLifeStage == (stage.Phase == KsProcgenSurfaceStagePhase.Initialized ?
                EntityLifeStage.MapInitialized : EntityLifeStage.Initialized) &&
            metadataComponent.EntityPrototype?.ID == prototypeId && !EntityManager.IsQueuedForDeletion(uid) &&
            TryComp(uid, out TransformComponent? transformComponent) &&
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

    private bool DiscardOwned(KsProcgenSurfaceStage stage) => _ownership.Release(stage);

    public int DiscardAll()
    {
        if (_operationSystem.Active)
            return 0;
        var stages = _ownership.Snapshot(KsProcgenStageKind.Surface).Cast<KsProcgenSurfaceStage>().ToArray();
        foreach (var stage in stages)
            Discard(stage);
        return stages.Length;
    }

    private void Cleanup(MapId mapId, EntityUid mapUid, IEnumerable<EntityUid> spawnedEntities)
        => _ownership.Cleanup(mapId, mapUid, spawnedEntities);

    private static KsProcgenSurfaceStageResult Failure(KsProcgenSurfaceStageStatus status, string code) =>
        new(status, new KsProcgenIssue(code, "Surface drop-coordinate preview could not be retained."), null);
}
