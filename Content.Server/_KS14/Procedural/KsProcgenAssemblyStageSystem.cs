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
public sealed class KsProcgenAssemblyStage
{
    internal KsProcgenAssemblyStage(MapId mapId, EntityUid mapUid, KsProcgenAssemblyPosePlan poses,
        Dictionary<string, EntityUid> members, List<EntityUid> spawnedEntities, int maxSpawnedEntities,
        KsProcgenSupportedAccessPlan? access)
    {
        MapId = mapId;
        MapUid = mapUid;
        Poses = poses;
        Members = new ReadOnlyDictionary<string, EntityUid>(members);
        SpawnedEntities = spawnedEntities;
        MaxSpawnedEntities = maxSpawnedEntities;
        Access = access;
    }

    public MapId MapId { get; }
    public EntityUid MapUid { get; }
    public KsProcgenAssemblyPosePlan Poses { get; }
    /// <summary>Optional declared room geometry, not live engine reach/collision validation.</summary>
    public KsProcgenSupportedAccessPlan? Access { get; }
    public IReadOnlyDictionary<string, EntityUid> Members { get; }
    public bool Active { get; internal set; } = true;
    public KsProcgenAssemblyStagePhase Phase { get; internal set; }
    public bool PlacementVerified => false;
    public int SpawnedEntityCount => SpawnedEntities.Count;
    public int MaxSpawnedEntities { get; }
    internal List<EntityUid> SpawnedEntities { get; }
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
    public const int MaximumRetainedStages = 16;
    public const int MaximumSpawnedEntitiesPerStage = 256;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private ItemSlotsSystem _itemSlotsSystem = default!;
    [Dependency] private EntityWhitelistSystem _whitelistSystem = default!;
    [Dependency] private KsProcgenContainerPreflightSystem _containerPreflightSystem = default!;
    [Dependency] private KsProcgenSurfacePreflightSystem _surfacePreflightSystem = default!;
    [Dependency] private KsProcgenPreviewOperationSystem _operationSystem = default!;
    private readonly Dictionary<MapId, KsProcgenAssemblyStage> _stages = new();
    private List<EntityUid>? _capturedEntities;

    public int RetainedStageCount => _stages.Count;

    public override void Initialize()
    {
        base.Initialize();
        EntityManager.EntityAdded += OnEntityAdded;
    }

    public override void Shutdown()
    {
        EntityManager.EntityAdded -= OnEntityAdded;
        DiscardAll();
        base.Shutdown();
    }

    [SubscribeLocalEvent]
    private void OnRoundRestartCleanup(RoundRestartCleanupEvent args) => DiscardAll();

    private void OnEntityAdded(Entity<MetaDataComponent> entity) => _capturedEntities?.Add(entity.Owner);

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
        if (_stages.Count >= MaximumRetainedStages)
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
        if (_stages.Count >= MaximumRetainedStages)
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
        KsProcgenSupportedAccessPlan? access, CancellationToken cancellationToken, int maxSpawnedEntities)
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
            _capturedEntities = spawnedEntities;
            cancellationToken.ThrowIfCancellationRequested();
            mapUid = _mapSystem.CreateMap(out mapId, runMapInit: false);
            _mapSystem.SetPaused(mapId, true);
            foreach (var pose in poses.Members)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (spawnedEntities.Count >= maxSpawnedEntities)
                    return Failure(KsProcgenAssemblyStageStatus.BudgetExceeded, "AssemblyStageSpawnBudget");
                var member = pose.Support;
                // All members start as owned map children; insertion establishes container parenting.
                var memberUid = Spawn(member.EntityPrototypeId, new EntityCoordinates(mapUid, pose.Position));
                members.Add(member.MemberId, memberUid);
                cancellationToken.ThrowIfCancellationRequested();
                if (spawnedEntities.Count > maxSpawnedEntities)
                    return Failure(KsProcgenAssemblyStageStatus.BudgetExceeded, "AssemblyStageSpawnBudget");
                if (member.Layer == KsProcgenPlacementLayer.Container)
                {
                    var parentUid = members[member.ParentMemberId!];
                    var preflight = _containerPreflightSystem.Check(memberUid, parentUid, member.SlotId!);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (spawnedEntities.Count > maxSpawnedEntities)
                        return Failure(KsProcgenAssemblyStageStatus.BudgetExceeded, "AssemblyStageSpawnBudget");
                    if (preflight.Status != KsProcgenContainerPreflightStatus.Eligible)
                        return Failure(KsProcgenAssemblyStageStatus.Rejected, preflight.Reason);
                    var inserted = preflight.UsesItemSlot ?
                        _itemSlotsSystem.TryInsert(parentUid, member.SlotId!, memberUid, user: null) :
                        _containerSystem.TryGetContainer(parentUid, member.SlotId!, out var container) &&
                        _containerSystem.Insert(memberUid, container, force: false);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (spawnedEntities.Count > maxSpawnedEntities)
                        return Failure(KsProcgenAssemblyStageStatus.BudgetExceeded, "AssemblyStageSpawnBudget");
                    if (!inserted || !Membership(memberUid, parentUid, member.SlotId!))
                        return Failure(KsProcgenAssemblyStageStatus.Rejected, "AssemblyStageInsertionFailed");
                }
                else
                {
                    if (Transform(memberUid).NoLocalRotation && pose.LocalQuarterTurns != 0)
                        return Failure(KsProcgenAssemblyStageStatus.Rejected, "AssemblyStageRotationUnsupported");
                    if (member.Layer == KsProcgenPlacementLayer.Surface)
                    {
                        var preflight = _surfacePreflightSystem.Check(memberUid, members[member.ParentMemberId!]);
                        if (preflight.Status != KsProcgenSurfacePreflightStatus.Candidate)
                            return Failure(preflight.Status == KsProcgenSurfacePreflightStatus.BudgetExceeded ?
                                KsProcgenAssemblyStageStatus.BudgetExceeded : KsProcgenAssemblyStageStatus.Rejected, preflight.Reason);
                    }
                    _transformSystem.SetCoordinates(memberUid, new EntityCoordinates(mapUid, pose.LocalPosition));
                    _transformSystem.SetLocalRotation(memberUid, Angle.FromDegrees((double) pose.LocalQuarterTurns * 90.0));
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (spawnedEntities.Count > maxSpawnedEntities)
                return Failure(KsProcgenAssemblyStageStatus.BudgetExceeded, "AssemblyStageSpawnBudget");
            var stage = new KsProcgenAssemblyStage(mapId, mapUid, poses, members, spawnedEntities, maxSpawnedEntities, access);
            var verificationIssue = ContentsIssue(stage);
            if (verificationIssue != null)
                return VerificationFailure(verificationIssue);
            _stages.Add(mapId, stage);
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

    private bool Owns(KsProcgenAssemblyStage stage) => stage != null && stage.Active &&
        _stages.TryGetValue(stage.MapId, out var owned) && ReferenceEquals(owned, stage);

    public bool Verify(KsProcgenAssemblyStage stage) => Owns(stage) && ContentsIssue(stage) == null;

    public KsProcgenAssemblyStageResult TryInitialize(KsProcgenAssemblyStage stage,
        CancellationToken cancellationToken = default)
    {
        if (!Owns(stage))
            return Failure(KsProcgenAssemblyStageStatus.InvalidInput, "AssemblyStageNotOwned");
        if (!_operationSystem.TryEnter(this))
            return Failure(KsProcgenAssemblyStageStatus.InvalidInput, "AssemblyStageOperationActive");
        try
        {
            _capturedEntities = stage.SpawnedEntities;
            cancellationToken.ThrowIfCancellationRequested();
            var preconditionIssue = ContentsIssue(stage);
            if (preconditionIssue != null)
            {
                DiscardAfterOperation(stage);
                return VerificationFailure(preconditionIssue);
            }
            if (stage.Phase == KsProcgenAssemblyStagePhase.Initialized)
                return new(KsProcgenAssemblyStageStatus.InitializedPreview, null, stage);
            _mapSystem.InitializeMap(stage.MapId, unpause: false);
            stage.Phase = KsProcgenAssemblyStagePhase.Initialized;
            cancellationToken.ThrowIfCancellationRequested();
            if (stage.SpawnedEntityCount > stage.MaxSpawnedEntities)
            {
                DiscardAfterOperation(stage);
                return Failure(KsProcgenAssemblyStageStatus.BudgetExceeded, "AssemblyStageInitializationSpawnBudget");
            }
            var verificationIssue = ContentsIssue(stage);
            if (verificationIssue != null)
            {
                DiscardAfterOperation(stage);
                return VerificationFailure(verificationIssue);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new(KsProcgenAssemblyStageStatus.InitializedPreview, null, stage);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DiscardAfterOperation(stage);
            return Failure(KsProcgenAssemblyStageStatus.Cancelled, "AssemblyStageInitializationCancelled");
        }
        catch (Exception)
        {
            DiscardAfterOperation(stage);
            return Failure(KsProcgenAssemblyStageStatus.EngineFailure, "AssemblyStageInitializationEngineFailure");
        }
        finally
        {
            _capturedEntities = null;
            _operationSystem.Exit(this);
        }
    }

    private string? ContentsIssue(KsProcgenAssemblyStage stage)
    {
        if (stage.SpawnedEntityCount > stage.MaxSpawnedEntities)
            return "AssemblyStageSpawnBudget";
        if (!_mapSystem.TryGetMap(stage.MapId, out var mapUid) || mapUid != stage.MapUid ||
            EntityManager.IsQueuedForDeletion(stage.MapUid) || !_mapSystem.IsPaused(stage.MapId) ||
            _mapSystem.IsInitialized(stage.MapId) != (stage.Phase == KsProcgenAssemblyStagePhase.Initialized))
            return "AssemblyStageMapDrift";
        foreach (var spawnedUid in stage.SpawnedEntities)
        {
            if (spawnedUid != mapUid && Exists(spawnedUid) &&
                (!TryComp<TransformComponent>(spawnedUid, out var generatedTransformComponent) || generatedTransformComponent.MapUid != mapUid))
                return "AssemblyStageGeneratedEntityOffMap";
        }
        foreach (var pose in stage.Poses.Members)
        {
            var member = pose.Support;
            var memberUid = stage.Members[member.MemberId];
            if (!TryComp<MetaDataComponent>(memberUid, out var metadataComponent) ||
                metadataComponent.EntityPrototype?.ID != member.EntityPrototypeId ||
                metadataComponent.EntityLifeStage != (stage.Phase == KsProcgenAssemblyStagePhase.Initialized ?
                    EntityLifeStage.MapInitialized : EntityLifeStage.Initialized) ||
                EntityManager.IsQueuedForDeletion(memberUid) || !TryComp<TransformComponent>(memberUid, out var transformComponent))
                return "AssemblyStageMemberDrift";
            var parentUid = pose.CoordinateParentMemberId == null ? stage.MapUid : stage.Members[pose.CoordinateParentMemberId];
            if (transformComponent.ParentUid != parentUid || transformComponent.LocalPosition != pose.LocalPosition ||
                transformComponent.LocalRotation != Angle.FromDegrees((double) pose.LocalQuarterTurns * 90.0))
                return "AssemblyStagePoseDrift";
            if (member.Layer == KsProcgenPlacementLayer.Container)
            {
                if (!Membership(memberUid, parentUid, member.SlotId!))
                    return "AssemblyStageMembershipDrift";
            }
            else if (_containerSystem.TryGetContainingContainer(memberUid, out _))
                return "AssemblyStageUnexpectedContainment";
            if (member.Layer != KsProcgenPlacementLayer.Surface)
                continue;
            var supportUid = stage.Members[member.ParentMemberId!];
            var preflight = _surfacePreflightSystem.Check(memberUid, supportUid);
            if (preflight.Status != KsProcgenSurfacePreflightStatus.Candidate)
                return preflight.Reason;
            var offset = preflight.Centered ? preflight.PositionOffset : Vector2.Zero;
            if (Transform(supportUid).LocalPosition + offset != pose.LocalPosition)
                return "AssemblyStageDropOffsetDrift";
        }
        foreach (var group in stage.Poses.Members.Where(pose => pose.Support.Layer == KsProcgenPlacementLayer.Surface)
                     .GroupBy(pose => pose.Support.ParentMemberId))
        {
            var preflight = _surfacePreflightSystem.Check(stage.Members[group.First().Support.MemberId], stage.Members[group.Key!]);
            if (preflight.MaximumTrackedEntities is > 0 &&
                (ulong) preflight.TrackedEntities + (ulong) group.Count() > (ulong) preflight.MaximumTrackedEntities.Value)
                return "AssemblyStageSurfaceCapacityConflict";
        }
        return null;
    }

    private bool Membership(EntityUid subjectUid, EntityUid parentUid, string containerId)
    {
        if (!_containerSystem.TryGetContainer(parentUid, containerId, out var container) || !container.Contains(subjectUid))
            return false;
        // Check retained filters without retrying insertion into a now occupied slot.
        if (TryComp<ItemSlotsComponent>(parentUid, out var slotsComponent) &&
            _itemSlotsSystem.TryGetSlot(parentUid, containerId, out var slot, component: slotsComponent) &&
            (!ReferenceEquals(slot.ContainerSlot, container) ||
                !_whitelistSystem.CheckBoth(subjectUid, blacklist: slot.Blacklist, whitelist: slot.Whitelist)))
            return false;
        return !Transform(subjectUid).Anchored;
    }

    private void DiscardAfterOperation(KsProcgenAssemblyStage stage)
    {
        _capturedEntities = null;
        DiscardOwned(stage);
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

    private bool DiscardOwned(KsProcgenAssemblyStage stage)
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

    private static KsProcgenAssemblyStageResult Failure(KsProcgenAssemblyStageStatus status, string code) =>
        new(status, new KsProcgenIssue(code, "Supported assembly transform preview could not be retained."), null);

    private static KsProcgenAssemblyStageResult VerificationFailure(string code) =>
        Failure(code is "AssemblyStageSpawnBudget" or "SurfaceTrackerBudget" ?
            KsProcgenAssemblyStageStatus.BudgetExceeded : KsProcgenAssemblyStageStatus.Rejected, code);
}
