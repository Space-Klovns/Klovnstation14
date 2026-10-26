using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using System.Threading;
using Content.Shared._KS14.Procedural;
using Content.Shared.GameTicking;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.Server._KS14.Procedural;

/// <summary>
/// Request-owned complete scene preview. Plans and freezes before allocating, then uses the common
/// allocation capture, assembly placement, ownership and paused initialization transaction.
/// No public mutable proposal is accepted as write authority. Simulation/publication are separate.
/// </summary>
public sealed partial class KsProcgenOwnedStageSystem : EntitySystem
{
    [Dependency] private KsProcgenPlanningSystem _planning = default!;
    [Dependency] private KsProcgenStageOwnershipSystem _ownership = default!;
    [Dependency] private KsProcgenPreviewOperationSystem _operations = default!;
    [Dependency] private KsProcgenAssemblyPlacementSystem _placement = default!;
    [Dependency] private KsProcgenStageLifecycleSystem _lifecycle = default!;
    [Dependency] private KsProcgenOwnedAtmosphereSystem _ownedAtmosphere = default!;
    [Dependency] private KsProcgenOwnedElectricalSystem _ownedElectrical = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedTransformSystem _transforms = default!;
    [Dependency] private ITileDefinitionManager _tileDefinitions = default!;
    [Dependency] private KsProcgenPrototypeManifestSystem _prototypeManifestSystem = default!;

    public int RetainedStageCount => _ownership.Count(KsProcgenStageKind.Owned);

    /// <summary>Use both captured theme catalogs; a project preview must not recapture loaded packs.</summary>
    public KsProcgenOwnedStageResult TryGenerate(KsProcgenProjectArea area,
        KsProcgenOwnedPlanBudgets? planningBudgets = null, KsProcgenOwnedStageBudgets? stageBudgets = null,
        CancellationToken cancellationToken = default)
    {
        if (area == null) return Failure(KsProcgenOwnedStageStatus.InvalidInput, "InvalidOwnedProjectArea");
        if (area.Library != null) return Failure(KsProcgenOwnedStageStatus.UnsupportedContent, "OwnedProjectLibraryUnsupported");
        if ((area.Request.Electrical ?? area.Content.Theme.Electrical)?.Distribution != null && area.UtilityContent == null)
            return Failure(KsProcgenOwnedStageStatus.InvalidInput, "OwnedProjectUtilityCatalogRequired");
        return TryGenerate(area.Request.CopyRequest(), area.Content.Theme.Id, planningBudgets: planningBudgets,
            stageBudgets: stageBudgets, content: area.Content, cancellationToken: cancellationToken, utilityContent: area.UtilityContent);
    }

    public KsProcgenOwnedStageResult TryGenerate(KsProcgenRequest request, string themeId,
        KsProcgenOwnedPlanBudgets? planningBudgets = null, KsProcgenOwnedStageBudgets? stageBudgets = null,
        KsProcgenThemeCatalog? content = null, CancellationToken cancellationToken = default, KsProcgenThemeCatalog? utilityContent = null)
    {
        if (_operations.Active) return Failure(KsProcgenOwnedStageStatus.InvalidInput, "OwnedStageOperationActive");
        if (cancellationToken.IsCancellationRequested) return Failure(KsProcgenOwnedStageStatus.Cancelled, "OwnedStageCancelled");
        var budgets = stageBudgets ?? new();
        if (budgets.MaximumTerrainCells is < 1 or > 65_536 || budgets.MaximumSpawnedEntities is < 1 or >
                KsProcgenPreviewOperationSystem.MaximumAllocationsPerStage || budgets.MaximumPlacementWork is < 0 or > 131_072 ||
            budgets.PrototypeCapture == null)
            return Failure(KsProcgenOwnedStageStatus.InvalidInput, "InvalidOwnedStageBudget");
        if (budgets.MaximumPrototypeImageCharacters is < 0 or > 67_108_864 || budgets.MaximumElectricalWork is < 0 or > 1_000_000)
            return Failure(KsProcgenOwnedStageStatus.InvalidInput, "InvalidOwnedStageBudget");
        if (!_ownership.CanRetain(KsProcgenStageKind.Owned))
            return Failure(KsProcgenOwnedStageStatus.BudgetExceeded, "OwnedStageRetentionBudget");
        if (!KsProcgenRequestSnapshot.TryCapture(ProtoMan, request, out var original, out var issue))
            return Failure(KsProcgenOwnedStageStatus.InvalidInput, issue!.Code);
        var resolved = original!.CopyRequest();
        // Resolve the standalone contract on a detached request. Authored entrance contracts remain intact.
        resolved.EntranceDomain ??= new();
        if (!KsProcgenRequestSnapshot.TryCapture(resolved, out var snapshot, out issue) ||
            !KsProcgenGeometry.TryNormalize(resolved, out var shape, out issue))
            return Failure(KsProcgenOwnedStageStatus.InvalidInput, issue!.Code);
        // There is no authoritative host/constant capture in this entry yet. Never replace it with blank space.
        if (resolved.ConstantRegions.Count != 0 || resolved.Shape.PreservedCells.Count != 0 ||
            resolved.EntranceDomain.Entrances.Count != 0 || shape!.TargetCells.Any(cell => !shape.CanWrite(cell)))
            return Failure(KsProcgenOwnedStageStatus.UnsupportedContent, "OwnedStageContextCaptureRequired");
        var context = new KsProcgenGroupRouteContext(new HashSet<Vector2i>(), new HashSet<Vector2i>(),
            new HashSet<Vector2i>(), true, true) { ExteriorIsEmptySpace = true };
        if (content == null && !KsProcgenThemeCatalog.TryCapture(ProtoMan, themeId, out content, out issue))
            return Failure(KsProcgenOwnedStageStatus.InvalidInput, issue!.Code);
        if (content!.Theme.Id != themeId) return Failure(KsProcgenOwnedStageStatus.InvalidInput, "MismatchedThemeCatalog");
        if (!KsProcgenElectricalProfiles.TryResolveSelection(ProtoMan, resolved.ElectricalProfile, resolved.Electrical,
                content.Theme.Electrical, out var electricalProfile, out issue))
            return Failure(KsProcgenOwnedStageStatus.InvalidInput, issue!.Code);
        if (!_planning.TryResolveElectricalUtilityContent(electricalProfile, utilityContent, out utilityContent, out issue))
            return Failure(KsProcgenOwnedStageStatus.InvalidInput, issue!.Code);
        var catalogs = utilityContent == null ? new[] { content } : new[] { content, utilityContent };
        if (!_prototypeManifestSystem.TryCapture(catalogs.SelectMany(item => item.CandidateEntityIds())
                .Concat(electricalProfile == null ? [] : KsProcgenPlanningSystem.ElectricalPrototypeIds(electricalProfile)),
                catalogs.SelectMany(item => item.CandidateTileIds()), out var prototypes, out issue, limits: budgets.PrototypeCapture))
            return Failure(issue!.Code.EndsWith("Budget", StringComparison.Ordinal) ? KsProcgenOwnedStageStatus.BudgetExceeded :
                issue.Code.StartsWith("Unsupported", StringComparison.Ordinal) ? KsProcgenOwnedStageStatus.UnsupportedContent :
                KsProcgenOwnedStageStatus.InvalidInput, issue.Code);
        var proposal = _planning.Propose(resolved, themeId, context, [], budgets: planningBudgets, content: content, utilityContent: utilityContent);
        if (proposal.Status != KsProcgenOwnedPlanStatus.Candidate)
            return Failure(proposal.Status == KsProcgenOwnedPlanStatus.BudgetExceeded ? KsProcgenOwnedStageStatus.BudgetExceeded :
                proposal.Status == KsProcgenOwnedPlanStatus.InvalidInput ? KsProcgenOwnedStageStatus.InvalidInput :
                KsProcgenOwnedStageStatus.Rejected, proposal.Issue!.Code, proposal: proposal);
        if (proposal.Gas?.Domains.Any(domain => domain.Demands.Any(demand =>
                demand.Profile.StateRequirement == KsProcgenAtmosphereState.StableAtRest &&
                demand.Profile.Requirement == KsProcgenAtmosphereRequirement.Required)) == true ||
            resolved.Utilities?.GasRestrictions.MinimumObservationTicks != null)
            return Failure(KsProcgenOwnedStageStatus.UnsupportedContent, "OwnedGasStabilityUnsupported", proposal: proposal);
        var compileIssue = Compile(shape!, proposal, electricalProfile, out var scene);
        if (compileIssue != null) return Failure(KsProcgenOwnedStageStatus.InvalidInput, compileIssue, proposal: proposal);
        if (scene!.Tiles.Count > budgets.MaximumTerrainCells)
            return Failure(KsProcgenOwnedStageStatus.BudgetExceeded, "OwnedStageTerrainBudget", proposal: proposal);
        if (scene.EntityCount + 2 > budgets.MaximumSpawnedEntities)
            return Failure(KsProcgenOwnedStageStatus.BudgetExceeded, "OwnedStageSpawnBudget", proposal: proposal);
        if (scene.PlacementWork > budgets.MaximumPlacementWork)
            return Failure(KsProcgenOwnedStageStatus.BudgetExceeded, "OwnedStagePlacementBudget", proposal: proposal);
        var imageBodies = prototypes!.Bodies.Where(body => body.Kind == KsProcgenPrototypeBodyKind.Entity)
            .ToDictionary(body => body.PrototypeId, StringComparer.Ordinal);
        long imageCharacters = 0;
        foreach (var id in scene.Entities.Select(entity => entity.PrototypeId).Concat(scene.Assemblies.SelectMany(assembly =>
                     assembly.Poses.Members.Select(member => member.Support.EntityPrototypeId))))
        {
            if (!imageBodies.TryGetValue(id, out var body))
                return Failure(KsProcgenOwnedStageStatus.Rejected, "UncapturedPrototypeReference", proposal: proposal);
            imageCharacters += 4L * ((long) body.CanonicalBody.Length + body.CanonicalMetadata.Length);
            if (imageCharacters > budgets.MaximumPrototypeImageCharacters)
                return Failure(KsProcgenOwnedStageStatus.BudgetExceeded, "OwnedStagePrototypeImageBudget", proposal: proposal);
        }
        if (!_prototypeManifestSystem.Matches(prototypes!, out issue))
            return Failure(KsProcgenOwnedStageStatus.Rejected, issue!.Code, proposal: proposal);
        return Allocate(snapshot!, proposal, scene, prototypes!, budgets, cancellationToken);
    }

    private KsProcgenOwnedStageResult Allocate(KsProcgenRequestSnapshot request,
        KsProcgenOwnedInteriorResult proposal, KsProcgenOwnedScene scene, KsProcgenPrototypeManifest prototypes, KsProcgenOwnedStageBudgets budgets,
        CancellationToken cancellationToken)
    {
        if (!_operations.TryEnter(this)) return Failure(KsProcgenOwnedStageStatus.InvalidInput, "OwnedStageOperationActive");
        var mapId = MapId.Nullspace;
        var mapUid = EntityUid.Invalid;
        var allocations = new List<EntityUid>();
        var retained = false;
        try
        {
            _operations.SetCapture(this, allocations);
            cancellationToken.ThrowIfCancellationRequested();
            mapUid = _maps.CreateMap(out mapId, runMapInit: false);
            _maps.SetPaused(mapId, true);
            if (allocations.Count >= budgets.MaximumSpawnedEntities)
                return Failure(KsProcgenOwnedStageStatus.BudgetExceeded, "OwnedStageSpawnBudget", proposal: proposal);
            var grid = _maps.CreateGridEntity(mapId);
            // Disconnected requested islands are intentional in a paused preview. Incremental
            // tile writes must not split/reparent the coordinate frame during construction.
            grid.Comp.CanSplit = false;
            if (!_prototypeManifestSystem.Matches(prototypes, out var manifestIssue))
                return Failure(KsProcgenOwnedStageStatus.Rejected, manifestIssue!.Code, proposal: proposal);
            foreach (var tile in scene.Tiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (allocations.Count > budgets.MaximumSpawnedEntities)
                    return Failure(KsProcgenOwnedStageStatus.BudgetExceeded, "OwnedStageSpawnBudget", proposal: proposal);
                if (!_prototypeManifestSystem.MatchesBody(prototypes, KsProcgenPrototypeBodyKind.Tile, tile.PrototypeId, out manifestIssue))
                    return Failure(KsProcgenOwnedStageStatus.Rejected, manifestIssue!.Code, proposal: proposal);
                _maps.SetTile(grid.Owner, grid.Comp, tile.Cell, new Tile(tile.TileId));
                cancellationToken.ThrowIfCancellationRequested();
                if (allocations.Count > budgets.MaximumSpawnedEntities)
                    return Failure(KsProcgenOwnedStageStatus.BudgetExceeded, "OwnedStageSpawnBudget", proposal: proposal);
            }
            if (!_prototypeManifestSystem.Matches(prototypes, out manifestIssue))
                return Failure(KsProcgenOwnedStageStatus.Rejected, manifestIssue!.Code, proposal: proposal);
            var entities = new List<EntityUid>();
            foreach (var entity in scene.Entities)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (allocations.Count >= budgets.MaximumSpawnedEntities)
                    return Failure(KsProcgenOwnedStageStatus.BudgetExceeded, "OwnedStageSpawnBudget", proposal: proposal);
                if (!_prototypeManifestSystem.MatchesBody(prototypes, KsProcgenPrototypeBodyKind.Entity, entity.PrototypeId, out manifestIssue))
                    return Failure(KsProcgenOwnedStageStatus.Rejected, manifestIssue!.Code, proposal: proposal);
                var uid = _prototypeManifestSystem.SpawnCaptured(prototypes, entity.PrototypeId, new EntityCoordinates(grid.Owner, entity.Position));
                entities.Add(uid);
                if (!_prototypeManifestSystem.MatchesBody(prototypes, KsProcgenPrototypeBodyKind.Entity, entity.PrototypeId, out manifestIssue))
                    return Failure(KsProcgenOwnedStageStatus.Rejected, manifestIssue!.Code, proposal: proposal);
                if (Transform(uid).NoLocalRotation && entity.QuarterTurns != 0)
                    return Failure(KsProcgenOwnedStageStatus.Rejected, "OwnedStageRotationUnsupported", proposal: proposal);
                _transforms.SetLocalRotation(uid, KsProcgenRotation.ToEngineAngle(entity.QuarterTurns));
                cancellationToken.ThrowIfCancellationRequested();
                if (allocations.Count > budgets.MaximumSpawnedEntities)
                    return Failure(KsProcgenOwnedStageStatus.BudgetExceeded, "OwnedStageSpawnBudget", proposal: proposal);
            }
            var assemblies = new List<IReadOnlyDictionary<string, EntityUid>>();
            foreach (var assembly in scene.Assemblies)
            {
                var members = new Dictionary<string, EntityUid>(StringComparer.Ordinal);
                var placementIssue = _placement.Place(assembly.Poses, grid.Owner, members, allocations,
                    budgets.MaximumSpawnedEntities, cancellationToken, validatePrototype: prototypeId =>
                        _prototypeManifestSystem.MatchesBody(prototypes, KsProcgenPrototypeBodyKind.Entity, prototypeId, out var issue) ? null : issue!.Code,
                    spawnCaptured: (prototypeId, coordinates) => _prototypeManifestSystem.SpawnCaptured(prototypes, prototypeId, coordinates));
                if (placementIssue != null)
                    return Failure(placementIssue.Status == KsProcgenAssemblyPlacementStatus.BudgetExceeded ?
                        KsProcgenOwnedStageStatus.BudgetExceeded : KsProcgenOwnedStageStatus.Rejected, placementIssue.Code, proposal: proposal);
                assemblies.Add(new ReadOnlyDictionary<string, EntityUid>(members));
            }
            cancellationToken.ThrowIfCancellationRequested();
            var stage = new KsProcgenOwnedStage(mapId, mapUid, grid.Owner, request, proposal.SemanticHash, scene, proposal.Gas, proposal.Electrical, budgets.MaximumElectricalWork,
                prototypes, allocations, budgets.MaximumSpawnedEntities, entities.AsReadOnly(), assemblies.AsReadOnly());
            var contentsIssue = ContentsIssue(stage);
            if (contentsIssue != null) return Failure(KsProcgenOwnedStageStatus.Rejected, contentsIssue, proposal: proposal);
            _ownership.Attach(stage);
            retained = true;
            return new(KsProcgenOwnedStageStatus.PositionedPreview, null, stage, proposal);
        }
        catch (KsProcgenCapturedPrototypeException exception)
        { return Failure(KsProcgenOwnedStageStatus.Rejected, exception.Code, proposal: proposal); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { return Failure(KsProcgenOwnedStageStatus.Cancelled, "OwnedStageCancelled", proposal: proposal); }
        catch (Exception)
        { return Failure(KsProcgenOwnedStageStatus.EngineFailure, "OwnedStageEngineFailure", proposal: proposal); }
        finally
        {
            _operations.SetCapture(this, null);
            try { if (!retained) _ownership.Cleanup(mapId, mapUid, allocations); }
            finally { _operations.Exit(this); }
        }
    }

    public bool Verify(KsProcgenOwnedStage stage) => _ownership.Owns(stage) && ContentsIssue(stage) == null &&
        _ownedAtmosphere.InitializeOrVerify(stage, afterMapInitialization: false, out _) == null &&
        _ownedElectrical.Inspect(stage, stage.InitialElectrical, false, stage.MaximumElectricalWork, out _, out _) == null;

    private string? ContentsIssue(KsProcgenOwnedStage stage) =>
        InspectScene(stage, stage.MaxSpawnedEntities, out _, out _);

    /// <summary>Share the verified complete map walk with bounded physics observers.</summary>
    internal string? InspectScene(KsProcgenOwnedStage stage, int maximumScannedEntities,
        out List<EntityUid> members, out int scannedEntities)
    {
        members = new(); scannedEntities = 0;
        if (stage.WasRetained && !_ownership.Owns(stage)) return "OwnedStageNotOwned";
        var initialized = stage.InitializationPhase == KsProcgenStageInitializationPhase.Initialized;
        if (stage.SpawnedEntityCount > stage.MaxSpawnedEntities) return "OwnedStageSpawnBudget";
        if (!_prototypeManifestSystem.Matches(stage.Prototypes, out _)) return "OwnedStagePrototypeDrift";
        if (!_maps.TryGetMap(stage.MapId, out var mapUid) || mapUid != stage.MapUid ||
            EntityManager.IsQueuedForDeletion(stage.MapUid) || !_maps.IsPaused(stage.MapId) ||
            _maps.IsInitialized(stage.MapId) != initialized ||
            Transform(stage.MapUid).LocalPosition != Vector2.Zero || Transform(stage.MapUid).LocalRotation != Angle.Zero)
            return "OwnedStageMapDrift";
        if (!TryComp<MapGridComponent>(stage.GridUid, out var grid) || grid.TileSize != 1 || grid.CanSplit ||
            EntityManager.IsQueuedForDeletion(stage.GridUid) ||
            !TryComp(stage.GridUid, out TransformComponent? gridTransform) || gridTransform.ParentUid != mapUid ||
            gridTransform.LocalPosition != Vector2.Zero || gridTransform.LocalRotation != Angle.Zero ||
            _maps.GetFilledTileCount((stage.GridUid, grid)) != stage.Scene.Tiles.Count)
            return "OwnedStageTerrainDrift";
        foreach (var tile in stage.Scene.Tiles)
            if (_maps.GetTileRef(stage.GridUid, grid, tile.Cell).Tile.TypeId != tile.TileId) return "OwnedStageTerrainDrift";
        var treeIssue = _ownership.InspectMapMembers(stage, Math.Min(stage.MaxSpawnedEntities, maximumScannedEntities),
            "OwnedStage", out members, out scannedEntities);
        if (treeIssue != null) return treeIssue;
        var present = members.ToHashSet();
        foreach (var uid in stage.SpawnedEntities)
        {
            if (!Exists(uid)) continue;
            if (!present.Contains(uid) || EntityManager.IsQueuedForDeletion(uid) ||
                !TryComp(uid, out MetaDataComponent? metadata) || metadata.EntityLifeStage !=
                    (initialized ? EntityLifeStage.MapInitialized : EntityLifeStage.Initialized))
                return "OwnedStageGeneratedEntityDrift";
        }
        if (stage.Entities.Count != stage.Scene.Entities.Count || stage.Assemblies.Count != stage.Scene.Assemblies.Count)
            return "OwnedStageMemberCountDrift";
        for (var index = 0; index < stage.Entities.Count; index++)
        {
            var uid = stage.Entities[index]; var expected = stage.Scene.Entities[index];
            if (!TryComp(uid, out MetaDataComponent? metadata) || metadata.EntityPrototype?.ID != expected.PrototypeId ||
                !TryComp(uid, out TransformComponent? transform) || transform.ParentUid != stage.GridUid ||
                transform.LocalPosition != expected.Position || transform.LocalRotation != KsProcgenRotation.ToEngineAngle(expected.QuarterTurns))
                return "OwnedStageEntityDrift";
            if (!_prototypeManifestSystem.MatchesSnapshot(stage.Prototypes, metadata.EntityPrototype!, out _))
                return "OwnedStageInstancePrototypeDrift";
        }
        for (var index = 0; index < stage.Assemblies.Count; index++)
        {
            var issue = _placement.MembersIssue(stage.Scene.Assemblies[index].Poses, stage.Assemblies[index], stage.GridUid, initialized);
            if (issue != null) return issue;
            foreach (var uid in stage.Assemblies[index].Values)
                if (!TryComp(uid, out MetaDataComponent? metadata) || metadata.EntityPrototype == null ||
                    !_prototypeManifestSystem.MatchesSnapshot(stage.Prototypes, metadata.EntityPrototype, out _))
                    return "OwnedStageInstancePrototypeDrift";
        }
        return null;
    }

    public KsProcgenOwnedStageResult TryInitialize(KsProcgenOwnedStage stage, CancellationToken cancellationToken = default)
    {
        KsProcgenInitialAtmosphereReceipt? preparedReceipt = null;
        KsProcgenInitialElectricalReceipt? preparedElectrical = null;
        var electricalWork = 0;
        string? InspectElectrical(bool rebuild)
        {
            var issue = _ownedElectrical.Inspect(stage, stage.InitialElectrical ?? preparedElectrical, rebuild,
                stage.MaximumElectricalWork - electricalWork, out var receipt, out var work, cancellationToken: cancellationToken);
            electricalWork += work; preparedElectrical = receipt ?? preparedElectrical;
            return issue;
        }
        var result = _lifecycle.TryInitialize(stage, "OwnedStage", afterInitialization =>
        {
            var issue = ContentsIssue(stage);
            if (issue != null) return issue;
            issue = InspectElectrical(afterInitialization);
            if (issue != null) return issue;
            if (stage.Gas != null)
            {
                issue = _ownedAtmosphere.InitializeOrVerify(stage, afterInitialization, out var receipt);
                preparedReceipt = receipt ?? preparedReceipt;
                if (issue != null) return issue;
            }
            issue = ContentsIssue(stage);
            if (issue != null) return issue;
            // Gas/native callbacks cannot invalidate earlier wiring and still publish a receipt.
            return afterInitialization && stage.Electrical != null ? InspectElectrical(false) : null;
        }, cancellationToken);
        // Publication of evidence follows the lifecycle's final cancellation/ownership checks.
        if (result.Status == KsProcgenStageInitializationStatus.Initialized)
        {
            stage.InitialAtmosphere ??= preparedReceipt;
            stage.InitialElectrical ??= preparedElectrical?.WithWork(electricalWork);
        }
        return result.Status == KsProcgenStageInitializationStatus.Initialized
            ? new(KsProcgenOwnedStageStatus.InitializedPreview, null, stage, null)
            : Failure(stage.Electrical != null && result.IssueCode?.EndsWith("Budget", StringComparison.Ordinal) == true
                ? KsProcgenOwnedStageStatus.BudgetExceeded : result.Status switch
            {
                KsProcgenStageInitializationStatus.InvalidInput => KsProcgenOwnedStageStatus.InvalidInput,
                KsProcgenStageInitializationStatus.BudgetExceeded => KsProcgenOwnedStageStatus.BudgetExceeded,
                KsProcgenStageInitializationStatus.Cancelled => KsProcgenOwnedStageStatus.Cancelled,
                KsProcgenStageInitializationStatus.EngineFailure => KsProcgenOwnedStageStatus.EngineFailure,
                _ => KsProcgenOwnedStageStatus.Rejected,
            }, result.IssueCode!);
    }

    public bool Discard(KsProcgenOwnedStage stage)
    {
        if (!_ownership.Owns(stage) || !_operations.TryEnter(this)) return false;
        try { return _ownership.Release(stage); }
        finally { _operations.Exit(this); }
    }

    public int DiscardAll()
    {
        if (_operations.Active) return 0;
        var stages = _ownership.Snapshot(KsProcgenStageKind.Owned).Cast<KsProcgenOwnedStage>().ToArray();
        foreach (var stage in stages) Discard(stage);
        return stages.Length;
    }

    [SubscribeLocalEvent]
    private void OnRoundRestartCleanup(RoundRestartCleanupEvent args) => DiscardAll();

    public override void Shutdown() { DiscardAll(); base.Shutdown(); }

    private static KsProcgenOwnedStageResult Failure(KsProcgenOwnedStageStatus status, string code,
        KsProcgenOwnedInteriorResult? proposal = null) => new(status,
        new(code, "Complete owned procedural preview could not be retained."), null, proposal);
}
