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
public sealed class KsProcgenContainerStage : KsProcgenStageHandle
{
    internal KsProcgenContainerStage(MapId mapId, EntityUid mapUid, KsProcgenAssemblySupportPlan structure,
        Dictionary<string, EntityUid> members, List<EntityUid> spawnedEntities, int maxSpawnedEntities)
        : base(mapId, mapUid, KsProcgenStageKind.Container, spawnedEntities, maxSpawnedEntities)
    {
        Structure = structure;
        Members = new ReadOnlyDictionary<string, EntityUid>(members);
    }

    public IReadOnlyDictionary<string, EntityUid> Members { get; }
    public KsProcgenContainerStagePhase Phase => InitializationPhase == KsProcgenStageInitializationPhase.Initialized
        ? KsProcgenContainerStagePhase.Initialized : KsProcgenContainerStagePhase.Uninitialized;
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
    public const int MaximumRetainedStages = KsProcgenStageOwnershipSystem.MaximumRetainedPerKind;
    public const int MaximumSpawnedEntitiesPerStage = KsProcgenPreviewOperationSystem.MaximumAllocationsPerStage;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private ItemSlotsSystem _itemSlotsSystem = default!;
    [Dependency] private EntityWhitelistSystem _whitelistSystem = default!;
    [Dependency] private KsProcgenContainerPreflightSystem _preflightSystem = default!;
    [Dependency] private KsProcgenPreviewOperationSystem _operationSystem = default!;

    [Dependency] private KsProcgenStageOwnershipSystem _ownership = default!;
    [Dependency] private KsProcgenStageLifecycleSystem _lifecycle = default!;

    public int RetainedStageCount => _ownership.Count(KsProcgenStageKind.Container);

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
        if (!_ownership.CanRetain(KsProcgenStageKind.Container))
            return Failure(KsProcgenContainerStageStatus.BudgetExceeded, "ContainerStageRetentionBudget");
        if (selectedMemberIds.Count + 1 > maxSpawnedEntities)
            return Failure(KsProcgenContainerStageStatus.BudgetExceeded, "ContainerStageSpawnBudget");
        if (!KsProcgenPrototypeCapabilityInspector.TryInspectAssembly(ProtoMan, Factory,
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
            _operationSystem.SetCapture(this, spawnedEntities);
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
            _ownership.Attach(stage);
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

    private bool Owns(KsProcgenContainerStage stage) => _ownership.Owns(stage);

    public bool Verify(KsProcgenContainerStage stage) => Owns(stage) && VerifyContents(stage);

    /// <summary>Initialize an owned preview once while paused; discard it if any selected relation loses membership.</summary>
    public KsProcgenContainerStageResult TryInitialize(KsProcgenContainerStage stage,
        CancellationToken cancellationToken = default)
    {
        var result = _lifecycle.TryInitialize(stage, "ContainerStage", initialized => VerifyContents(stage) ? null :
            initialized ? "ContainerStageInitializationFailed" : "ContainerStageInitializationPrecondition", cancellationToken);
        if (result.Status == KsProcgenStageInitializationStatus.Initialized)
            return new(KsProcgenContainerStageStatus.InitializedPreview, null, stage);
        return Failure(result.Status switch
        {
            KsProcgenStageInitializationStatus.InvalidInput => KsProcgenContainerStageStatus.InvalidInput,
            KsProcgenStageInitializationStatus.Rejected => KsProcgenContainerStageStatus.Rejected,
            KsProcgenStageInitializationStatus.BudgetExceeded => KsProcgenContainerStageStatus.BudgetExceeded,
            KsProcgenStageInitializationStatus.Cancelled => KsProcgenContainerStageStatus.Cancelled,
            _ => KsProcgenContainerStageStatus.EngineFailure,
        }, result.IssueCode!);
    }

    private bool VerifyContents(KsProcgenContainerStage stage)
    {
        if (stage.WasRetained && !Owns(stage)) return false;
        if (EntityManager.IsQueuedForDeletion(stage.MapUid)) return false;
        if (stage.SpawnedEntityCount > stage.MaxSpawnedEntities)
            return false;
        foreach (var spawnedUid in stage.SpawnedEntities)
        {
            if (spawnedUid != stage.MapUid && Exists(spawnedUid) &&
                (!TryComp(spawnedUid, out TransformComponent? spawnedTransformComponent) ||
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
            if (!TryComp(memberUid, out MetaDataComponent? metadataComponent) ||
                metadataComponent.EntityPrototype?.ID != member.EntityPrototypeId ||
                metadataComponent.EntityLifeStage != (stage.Phase == KsProcgenContainerStagePhase.Initialized ?
                    EntityLifeStage.MapInitialized : EntityLifeStage.Initialized) ||
                EntityManager.IsQueuedForDeletion(memberUid) || !TryComp(memberUid, out TransformComponent? transformComponent))
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
        return TryComp(subjectUid, out TransformComponent? transformComponent) && transformComponent.ParentUid == parentUid &&
            transformComponent.LocalPosition == Vector2.Zero && !transformComponent.Anchored;
    }

    public bool Discard(KsProcgenContainerStage stage)
    {
        if (!Owns(stage) || _operationSystem.Active)
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

    private bool DiscardOwned(KsProcgenContainerStage stage) => _ownership.Release(stage);

    public int DiscardAll()
    {
        if (_operationSystem.Active)
            return 0;
        var stages = _ownership.Snapshot(KsProcgenStageKind.Container).Cast<KsProcgenContainerStage>().ToArray();
        foreach (var stage in stages)
            Discard(stage);
        return stages.Length;
    }

    private void Cleanup(MapId mapId, EntityUid mapUid, IEnumerable<EntityUid> members)
        => _ownership.Cleanup(mapId, mapUid, members);

    private static KsProcgenContainerStageResult Failure(KsProcgenContainerStageStatus status, string code) =>
        new(status, new KsProcgenIssue(code, "Container assembly preview could not be retained."), null);
}
