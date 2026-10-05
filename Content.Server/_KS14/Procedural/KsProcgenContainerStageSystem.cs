using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using System.Threading;
using Content.Shared._KS14.Procedural;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.GameTicking;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Procedural;

public enum KsProcgenContainerStageStatus : byte
{
    InsertedPreview,
    InitializedPreview,
    InvalidInput,
    BudgetExceeded,
    UnsupportedContent,
    Rejected,
    EngineFailure,
    Cancelled,
}

public enum KsProcgenContainerStagePhase : byte
{
    Uninitialized,
    Initialized,
}

/// <summary>Owned, paused preview. Container membership is not full placement or publication proof.</summary>
public sealed class KsProcgenContainerStage
{
    internal KsProcgenContainerStage(MapId mapId, EntityUid mapUid, KsProcgenAssemblySupportPlan structure,
        Dictionary<string, EntityUid> members, List<EntityUid> spawnedEntities, int maxSpawnedEntities)
    {
        MapId = mapId;
        MapUid = mapUid;
        Structure = structure;
        Members = new ReadOnlyDictionary<string, EntityUid>(members);
        SpawnedEntities = spawnedEntities;
        MaxSpawnedEntities = maxSpawnedEntities;
    }

    public MapId MapId { get; }
    public EntityUid MapUid { get; }
    public IReadOnlyDictionary<string, EntityUid> Members { get; }
    public bool Active { get; internal set; } = true;
    public KsProcgenContainerStagePhase Phase { get; internal set; }
    /// <summary>Cumulative allocations, including the map and subsequently deleted initializer entities.</summary>
    public int SpawnedEntityCount => SpawnedEntities.Count;
    public int MaxSpawnedEntities { get; }
    internal List<EntityUid> SpawnedEntities { get; }
    internal KsProcgenAssemblySupportPlan Structure { get; }
}

public sealed record KsProcgenContainerStageResult(
    KsProcgenContainerStageStatus Status, KsProcgenIssue? Issue, KsProcgenContainerStage? Stage);

/// <summary>
/// Disposable container-only assembly preview. Every member is newly spawned and owned here.
/// No surface, spatial-layout, traversal, operational-use, visibility-isolation or publication guarantee.
/// </summary>
public sealed partial class KsProcgenContainerStageSystem : EntitySystem
{
    public const int MaximumRetainedStages = 16;
    public const int MaximumSpawnedEntitiesPerStage = 256;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IComponentFactory _componentFactory = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private ItemSlotsSystem _itemSlotsSystem = default!;
    [Dependency] private EntityWhitelistSystem _whitelistSystem = default!;
    [Dependency] private KsProcgenContainerPreflightSystem _preflightSystem = default!;
    [Dependency] private KsProcgenPreviewOperationSystem _operationSystem = default!;

    private readonly Dictionary<MapId, KsProcgenContainerStage> _stages = new();
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

    // Allocation occurs before components/transforms exist. Only record here; never throw, inspect
    // parents or delete an entity from this callback. Synchronous operation scope establishes ownership.
    private void OnEntityAdded(Entity<MetaDataComponent> entity) => _capturedEntities?.Add(entity.Owner);

    public KsProcgenContainerStageResult TryStage(KsProcgenResolvedAssembly assembly,
        IReadOnlyCollection<string> selectedMemberIds, int maxMembers = 64,
        CancellationToken cancellationToken = default, int maxSpawnedEntities = MaximumSpawnedEntitiesPerStage)
    {
        if (_operationSystem.Active)
            return Failure(KsProcgenContainerStageStatus.InvalidInput, "ContainerStageOperationActive");
        if (maxMembers is < 1 or > 64)
            return Failure(KsProcgenContainerStageStatus.InvalidInput, "InvalidContainerStageBudget");
        if (selectedMemberIds.Count > maxMembers)
            return Failure(KsProcgenContainerStageStatus.BudgetExceeded, "ContainerStageMemberBudget");
        if (cancellationToken.IsCancellationRequested)
            return Failure(KsProcgenContainerStageStatus.Cancelled, "ContainerStageCancelled");
        if (maxSpawnedEntities is < 1 or > MaximumSpawnedEntitiesPerStage)
            return Failure(KsProcgenContainerStageStatus.InvalidInput, "InvalidContainerStageSpawnBudget");
        if (_stages.Count >= MaximumRetainedStages)
            return Failure(KsProcgenContainerStageStatus.BudgetExceeded, "ContainerStageRetentionBudget");
        if (selectedMemberIds.Count + 1 > maxSpawnedEntities)
            return Failure(KsProcgenContainerStageStatus.BudgetExceeded, "ContainerStageSpawnBudget");
        if (!KsProcgenPrototypeCapabilityInspector.TryInspectAssembly(_prototypeManager, _componentFactory,
                assembly, out var capabilities, out var issue))
            return Failure(KsProcgenContainerStageStatus.InvalidInput, issue!.Code);
        var structure = KsProcgenAssemblySupportPlanner.Plan(assembly, selectedMemberIds, capabilities!);
        if (structure.Status != KsProcgenAssemblySupportStatus.NeedsEngineValidation)
            return Failure(structure.Status == KsProcgenAssemblySupportStatus.InvalidInput ?
                KsProcgenContainerStageStatus.InvalidInput : KsProcgenContainerStageStatus.Rejected, structure.Issue!.Code);
        if (structure.Members.Any(member => member.Layer == KsProcgenPlacementLayer.Surface) ||
            assembly.Relations.Any(relation => selectedMemberIds.Contains(relation.Subject) &&
                relation.Kind != KsProcgenRelationKind.InContainer))
            return Failure(KsProcgenContainerStageStatus.UnsupportedContent, "UnsupportedContainerStageRelations");

        var mapId = MapId.Nullspace;
        var mapUid = EntityUid.Invalid;
        var members = new Dictionary<string, EntityUid>(StringComparer.Ordinal);
        var spawnedEntities = new List<EntityUid>();
        var retained = false;
        if (!_operationSystem.TryEnter(this))
            return Failure(KsProcgenContainerStageStatus.InvalidInput, "ContainerStageOperationActive");
        try
        {
            _capturedEntities = spawnedEntities;
            cancellationToken.ThrowIfCancellationRequested();
            mapUid = _mapSystem.CreateMap(out mapId, runMapInit: false);
            _mapSystem.SetPaused(mapId, true);
            foreach (var member in structure.Members)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Temporary preview positions only; no floor footprint/interaction validation is implied.
                var memberUid = Spawn(member.EntityPrototypeId, new EntityCoordinates(mapUid, (float) members.Count * 2f, 0f));
                members.Add(member.MemberId, memberUid);
                cancellationToken.ThrowIfCancellationRequested();
                if (spawnedEntities.Count > maxSpawnedEntities)
                    return Failure(KsProcgenContainerStageStatus.BudgetExceeded, "ContainerStageSpawnBudget");
                if (member.Layer != KsProcgenPlacementLayer.Container)
                    continue;
                var parentUid = members[member.ParentMemberId!];
                var preflight = _preflightSystem.Check(memberUid, parentUid, member.SlotId!);
                cancellationToken.ThrowIfCancellationRequested();
                if (spawnedEntities.Count > maxSpawnedEntities)
                    return Failure(KsProcgenContainerStageStatus.BudgetExceeded, "ContainerStageSpawnBudget");
                if (preflight.Status != KsProcgenContainerPreflightStatus.Eligible)
                    return Failure(KsProcgenContainerStageStatus.Rejected, preflight.Reason);
                var inserted = preflight.UsesItemSlot ?
                    _itemSlotsSystem.TryInsert(parentUid, member.SlotId!, memberUid, user: null) :
                    _containerSystem.TryGetContainer(parentUid, member.SlotId!, out var container) &&
                    _containerSystem.Insert(memberUid, container, force: false);
                cancellationToken.ThrowIfCancellationRequested();
                if (spawnedEntities.Count > maxSpawnedEntities)
                    return Failure(KsProcgenContainerStageStatus.BudgetExceeded, "ContainerStageSpawnBudget");
                // Item-slot success alone is insufficient: its internal container insertion can fail.
                if (!inserted || !Membership(memberUid, parentUid, member.SlotId!))
                    return Failure(KsProcgenContainerStageStatus.Rejected, "ContainerStageInsertionFailed");
            }
            var stage = new KsProcgenContainerStage(mapId, mapUid, structure, members, spawnedEntities, maxSpawnedEntities);
            if (!VerifyContents(stage))
                return Failure(KsProcgenContainerStageStatus.Rejected, "ContainerStageVerificationFailed");
            cancellationToken.ThrowIfCancellationRequested();
            _stages.Add(mapId, stage);
            retained = true;
            return new(KsProcgenContainerStageStatus.InsertedPreview, null, stage);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failure(KsProcgenContainerStageStatus.Cancelled, "ContainerStageCancelled");
        }
        catch (Exception)
        {
            return Failure(KsProcgenContainerStageStatus.EngineFailure, "ContainerStageEngineFailure");
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

    private bool Owns(KsProcgenContainerStage stage) => stage != null && stage.Active &&
        _stages.TryGetValue(stage.MapId, out var owned) && ReferenceEquals(stage, owned);

    public bool Verify(KsProcgenContainerStage stage) => Owns(stage) && VerifyContents(stage);

    /// <summary>Initialize an owned preview once while paused; discard it if any selected relation loses membership.</summary>
    public KsProcgenContainerStageResult TryInitialize(KsProcgenContainerStage stage,
        CancellationToken cancellationToken = default)
    {
        if (!Owns(stage))
            return Failure(KsProcgenContainerStageStatus.InvalidInput, "ContainerStageNotOwned");
        if (!_operationSystem.TryEnter(this))
            return Failure(KsProcgenContainerStageStatus.InvalidInput, "ContainerStageOperationActive");
        try
        {
            _capturedEntities = stage.SpawnedEntities;
            cancellationToken.ThrowIfCancellationRequested();
            if (!VerifyContents(stage))
            {
                DiscardAfterOperation(stage);
                return Failure(KsProcgenContainerStageStatus.Rejected, "ContainerStageInitializationPrecondition");
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (stage.Phase == KsProcgenContainerStagePhase.Initialized)
                return new(KsProcgenContainerStageStatus.InitializedPreview, null, stage);
            // An uninitialized map is implicitly paused; retain an explicit pause across initialization.
            _mapSystem.SetPaused(stage.MapId, true);
            _mapSystem.InitializeMap(stage.MapId, unpause: false);
            stage.Phase = KsProcgenContainerStagePhase.Initialized;
            cancellationToken.ThrowIfCancellationRequested();
            if (stage.SpawnedEntityCount > stage.MaxSpawnedEntities)
            {
                DiscardAfterOperation(stage);
                return Failure(KsProcgenContainerStageStatus.BudgetExceeded, "ContainerStageInitializationSpawnBudget");
            }
            if (!VerifyContents(stage))
            {
                DiscardAfterOperation(stage);
                return Failure(KsProcgenContainerStageStatus.Rejected, "ContainerStageInitializationFailed");
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new(KsProcgenContainerStageStatus.InitializedPreview, null, stage);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DiscardAfterOperation(stage);
            return Failure(KsProcgenContainerStageStatus.Cancelled, "ContainerStageInitializationCancelled");
        }
        catch (Exception)
        {
            DiscardAfterOperation(stage);
            return Failure(KsProcgenContainerStageStatus.EngineFailure, "ContainerStageInitializationEngineFailure");
        }
        finally
        {
            _capturedEntities = null;
            _operationSystem.Exit(this);
        }
    }

    private void DiscardAfterOperation(KsProcgenContainerStage stage)
    {
        _capturedEntities = null;
        DiscardOwned(stage);
    }

    private bool VerifyContents(KsProcgenContainerStage stage)
    {
        if (stage.SpawnedEntityCount > stage.MaxSpawnedEntities)
            return false;
        foreach (var spawnedUid in stage.SpawnedEntities)
        {
            if (spawnedUid != stage.MapUid && Exists(spawnedUid) &&
                (!TryComp<TransformComponent>(spawnedUid, out var spawnedTransformComponent) ||
                    spawnedTransformComponent.MapUid != stage.MapUid))
                return false;
        }
        if (!_mapSystem.TryGetMap(stage.MapId, out var mapUid) || mapUid != stage.MapUid ||
            !_mapSystem.IsPaused(stage.MapId) ||
            _mapSystem.IsInitialized(stage.MapId) != (stage.Phase == KsProcgenContainerStagePhase.Initialized))
            return false;
        foreach (var member in stage.Structure.Members)
        {
            var memberUid = stage.Members[member.MemberId];
            if (!TryComp<MetaDataComponent>(memberUid, out var metadataComponent) ||
                metadataComponent.EntityPrototype?.ID != member.EntityPrototypeId ||
                metadataComponent.EntityLifeStage != (stage.Phase == KsProcgenContainerStagePhase.Initialized ?
                    EntityLifeStage.MapInitialized : EntityLifeStage.Initialized) ||
                EntityManager.IsQueuedForDeletion(memberUid) || !TryComp<TransformComponent>(memberUid, out var transformComponent))
                return false;
            if (member.Layer == KsProcgenPlacementLayer.Container)
            {
                if (!Membership(memberUid, stage.Members[member.ParentMemberId!], member.SlotId!))
                    return false;
            }
            else if (transformComponent.ParentUid != stage.MapUid)
                return false;
        }
        return true;
    }

    private bool Membership(EntityUid subjectUid, EntityUid parentUid, string containerId)
    {
        if (!_containerSystem.TryGetContainer(parentUid, containerId, out var container) || !container.Contains(subjectUid))
            return false;
        // Retained contents must still match filters. Do not replay insertion attempts against an
        // occupied slot; locking controls insertion/ejection, not compatibility of stored contents.
        if (TryComp<ItemSlotsComponent>(parentUid, out var slotsComponent) &&
            _itemSlotsSystem.TryGetSlot(parentUid, containerId, out var slot, component: slotsComponent) &&
            (!ReferenceEquals(slot.ContainerSlot, container) ||
                !_whitelistSystem.CheckBoth(subjectUid, blacklist: slot.Blacklist, whitelist: slot.Whitelist)))
            return false;
        return TryComp<TransformComponent>(subjectUid, out var transformComponent) && transformComponent.ParentUid == parentUid &&
            transformComponent.LocalPosition == Vector2.Zero && !transformComponent.Anchored;
    }

    public bool Discard(KsProcgenContainerStage stage)
    {
        if (_operationSystem.Active)
            return false;
        if (!_operationSystem.TryEnter(this))
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

    private bool DiscardOwned(KsProcgenContainerStage stage)
    {
        if (stage == null || !stage.Active || !_stages.TryGetValue(stage.MapId, out var owned) || !ReferenceEquals(stage, owned))
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

    private void Cleanup(MapId mapId, EntityUid mapUid, IEnumerable<EntityUid> members)
    {
        // Delete recorded members even if an event moved them off the owned map.
        foreach (var memberUid in members.Reverse().ToArray())
        {
            if (Exists(memberUid))
                Del(memberUid);
        }
        if (_mapSystem.TryGetMap(mapId, out var actualMapUid) && actualMapUid == mapUid)
            _mapSystem.DeleteMap(mapId);
    }

    private static KsProcgenContainerStageResult Failure(KsProcgenContainerStageStatus status, string code) =>
        new(status, new KsProcgenIssue(code, "Container assembly preview could not be retained."), null);
}
