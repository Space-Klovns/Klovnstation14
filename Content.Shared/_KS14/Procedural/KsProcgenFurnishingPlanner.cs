using System.Linq;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Procedural;

public enum KsProcgenFurnishingStatus : byte
{
    Proposed,
    Sparse,
    MandatoryUnmet,
    BudgetExceeded,
    InvalidInput,
}

/// <summary>Value-comparable occupied tiles of one proposed entity.</summary>
public readonly struct KsProcgenEntityFootprint : IEquatable<KsProcgenEntityFootprint>
{
    private readonly Vector2i[]? _cells;
    public IReadOnlyList<Vector2i> Cells => _cells ?? [];

    public KsProcgenEntityFootprint(IEnumerable<Vector2i> cells)
    {
        _cells = cells.ToArray();
    }

    public bool Equals(KsProcgenEntityFootprint other) => Cells.SequenceEqual(other.Cells);
    public override bool Equals(object? obj) => obj is KsProcgenEntityFootprint other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var cell in Cells)
            hash.Add(cell);
        return hash.ToHashCode();
    }
}

/// <summary>
/// One speculative entity placement. Real collision, anchoring, direction, and operation follow.
/// </summary>
public sealed record KsProcgenEntityProposal(
    string PackId,
    string EntryId,
    string EntityId,
    KsProcgenEntityRole Role,
    KsProcgenMovementClass Movement,
    Vector2i Cell,
    int QuarterTurns,
    Vector2i? InteractionApproach,
    int ClusterIndex = 0,
    KsProcgenEntityFootprint Footprint = default,
    string? CoreId = null,
    string? AssemblyId = null,
    string? VariantId = null,
    Vector2i? DeclaredApproachLanding = null)
{
    public IReadOnlyList<Vector2i> OccupiedCells => Footprint.Cells.Count == 0 ? [Cell] : Footprint.Cells;
}

public sealed record KsProcgenPackOmission(string PackId, string Reason);

public sealed class KsProcgenFurnishingResult
{
    public KsProcgenFurnishingStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public IReadOnlyList<KsProcgenEntityProposal> Entities { get; init; } = [];
    public IReadOnlyList<KsProcgenPackOmission> Omissions { get; init; } = [];
    public IReadOnlyList<Vector2i> ProtectedPassageCells { get; init; } = [];
    public int CandidateProbes { get; init; }
    public int TargetDominantClusters { get; init; }
    public int PlacedDominantClusters { get; init; }
    public IReadOnlyList<KsProcgenAssemblyRelationWitness> RelationWitnesses { get; init; } = [];
    public bool PreferenceSearchTruncated { get; init; }
    public int RelationPathExpandedCells { get; init; }
    public bool RelationPathSearchTruncated { get; init; }
    public int MandatoryBacktracks { get; init; }
}

/// <summary>
/// Bounded floor assembly proposals. Each selected variant is atomic; unrelated singleton cores
/// may be omitted independently. Surface/container capabilities remain the engine adapter's responsibility.
/// </summary>
public static class KsProcgenFurnishingPlanner
{
    private static readonly Vector2i[] Faces = [new(0, -1), new(-1, 0), new(0, 1), new(1, 0)];

    public static KsProcgenFurnishingResult Plan(
        IPrototypeManager prototypeManager,
        KsProcgenThemedRegion region,
        KsProcgenPartitionResult partition,
        int seed,
        IReadOnlySet<Vector2i>? inspectedWalls = null,
        int maxCandidateProbes = 4_096,
        int maxRoomCells = 512)
    {
        if (prototypeManager == null || region == null || partition == null ||
            partition.Status == KsProcgenPartitionStatus.InvalidInput ||
            maxCandidateProbes <= 0 || maxCandidateProbes > 4_096 ||
            maxRoomCells <= 0 || maxRoomCells > 65_536 ||
            !float.IsFinite(region.Theme.FurnishingDensity) ||
            region.Theme.FurnishingDensity < 0f || region.Theme.FurnishingDensity > 1f)
            return Failure(KsProcgenFurnishingStatus.InvalidInput, "InvalidFurnishingInput");

        var floor = new HashSet<Vector2i>(region.FloorCells);
        if (floor.Count != region.FloorCells.Count || floor.Count == 0 ||
            floor.Any(cell => !partition.FloorCells.Contains(cell)))
            return Failure(KsProcgenFurnishingStatus.InvalidInput, "InvalidFurnishingFloor");
        var selectedPacks = new List<string>();
        if (region.Theme.DominantEntityPackId != null)
            selectedPacks.Add(region.Theme.DominantEntityPackId);
        selectedPacks.AddRange(region.Theme.SupportingEntityPackIds
            .Where(id => id != region.Theme.DominantEntityPackId)
            .OrderBy(id => id, StringComparer.Ordinal));
        var compiledPacks = new Dictionary<string, IReadOnlyList<KsProcgenResolvedEntityCore>>(StringComparer.Ordinal);
        foreach (var packId in selectedPacks)
        {
            if (!prototypeManager.TryIndex<KsProcgenEntityPackPrototype>(packId, out var pack))
                return Failure(KsProcgenFurnishingStatus.InvalidInput, "UnknownFurnishingPack");
            if (!KsProcgenAssemblyCompiler.TryResolvePack(prototypeManager, pack, out var cores, out _))
                return Failure(KsProcgenFurnishingStatus.InvalidInput, "InvalidFurnishingCore");
            compiledPacks[packId] = cores;
        }
        var hasRequiredContents = region.UnplacedRequiredPacks.Count > 0 ||
                                  compiledPacks.Values.Any(cores => cores.Any(core => core.MinimumCount > 0));
        if (region.Kind == KsProcgenZoneKind.Passage)
        {
            if (hasRequiredContents)
                return Failure(KsProcgenFurnishingStatus.MandatoryUnmet, "MandatoryPackInPassage");
            return new KsProcgenFurnishingResult
            {
                Status = KsProcgenFurnishingStatus.Proposed,
                ProtectedPassageCells = KsProcgenGeometry.SortCells(floor),
            };
        }

        if (floor.Count > maxRoomCells)
        {
            if (hasRequiredContents)
                return Failure(KsProcgenFurnishingStatus.BudgetExceeded, "FurnishingRoomCellBudget");
            var skipped = new List<KsProcgenPackOmission>();
            if (region.Theme.DominantEntityPackId != null)
                skipped.Add(new KsProcgenPackOmission(region.Theme.DominantEntityPackId, "FurnishingRoomCellBudget"));
            skipped.AddRange(region.Theme.SupportingEntityPackIds.Select(id =>
                new KsProcgenPackOmission(id, "FurnishingRoomCellBudget")));
            return new KsProcgenFurnishingResult
            {
                Status = KsProcgenFurnishingStatus.Sparse,
                Omissions = skipped,
                ProtectedPassageCells = KsProcgenGeometry.SortCells(floor),
            };
        }

        var terminals = new HashSet<Vector2i>();
        foreach (var door in partition.DoorOpenings)
        {
            if (floor.Contains(door.Threshold)) terminals.Add(door.Threshold);
            if (floor.Contains(door.InsideApproach)) terminals.Add(door.InsideApproach);
            if (floor.Contains(door.OutsideApproach)) terminals.Add(door.OutsideApproach);
        }
        if (terminals.Count == 0)
            terminals.Add(KsProcgenGeometry.SortCells(floor)[0]);

        var protectedCells = new HashSet<Vector2i>(terminals);
        var root = KsProcgenGeometry.SortCells(terminals)[0];
        foreach (var terminal in KsProcgenGeometry.SortCells(terminals))
        {
            var path = KsProcgenTraversal.FindCleanPath(root, terminal, floor, new HashSet<Vector2i>());
            if (path.Count == 0)
                return Failure(KsProcgenFurnishingStatus.InvalidInput, "FurnishingRoomRouteUnavailable");
            protectedCells.UnionWith(path);
        }

        if (region.UnplacedRequiredPacks.Any(required =>
                required.MinimumCount != 1 || !selectedPacks.Contains(required.PackId)))
            return Failure(KsProcgenFurnishingStatus.MandatoryUnmet, "UnsupportedMandatoryPackCount");

        var walls = new HashSet<Vector2i>(partition.WallCells);
        if (inspectedWalls != null)
            walls.UnionWith(inspectedWalls);
        var occupied = new HashSet<Vector2i>();
        var blocked = new HashSet<Vector2i>();
        var entities = new List<KsProcgenEntityProposal>();
        var omissions = new List<KsProcgenPackOmission>();
        var relationWitnesses = new List<KsProcgenAssemblyRelationWitness>();
        var preferenceSearchTruncated = false;
        var relationPathBudget = new KsProcgenRelationPathBudget();
        var probes = 0;
        Vector2i? dominantAnchor = null;
        var dominantCores = new List<KsProcgenResolvedEntityCore>();
        var dominantAnchors = new List<Vector2i>();
        var packCounts = compiledPacks.Keys.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        var initializedCores = new HashSet<(string PackId, string CoreId)>();
        if (region.Theme.DominantEntityPackId is { } dominantPackId)
            dominantCores.AddRange(compiledPacks[dominantPackId].Where(core => core.Weight > 0f));

        var mandatoryTasks = new List<(string PackId, IReadOnlyList<KsProcgenResolvedEntityCore> Choices)>();
        foreach (var packId in selectedPacks)
        foreach (var core in compiledPacks[packId].Where(core => core.MinimumCount > 0)
                     .OrderBy(core => core.Id, StringComparer.Ordinal))
        for (var copy = 0; copy < core.MinimumCount; copy++)
        {
            if (mandatoryTasks.Count == 64)
                return Failure(KsProcgenFurnishingStatus.BudgetExceeded, "FurnishingMandatoryDepthBudget");
            mandatoryTasks.Add((packId, new[] { core }));
        }

        // A pack minimum needs any complete core, including packs with no explicit core minimum.
        foreach (var packId in selectedPacks.Where(id => IsRequired(region, id) &&
                     !compiledPacks[id].Any(core => core.MinimumCount > 0)))
        {
            if (mandatoryTasks.Count == 64)
                return Failure(KsProcgenFurnishingStatus.BudgetExceeded, "FurnishingMandatoryDepthBudget");
            mandatoryTasks.Add((packId, KsProcgenCoreSelector.Order(compiledPacks[packId], seed, region.Id, packId, 0)));
        }
        // Bound nested member and mandatory continuations as well as the existing probe/path budgets.
        if (mandatoryTasks.Sum(task => task.Choices.Max(core => core.Variants.Max(variant => variant.Members.Count))) > 256)
            return Failure(KsProcgenFurnishingStatus.BudgetExceeded, "FurnishingMandatoryDepthBudget");
        var mandatoryBacktracks = 0;
        KsProcgenFurnishingResult? mandatoryFailure = null;
        if (!SearchMandatory(0))
            return mandatoryFailure ?? Failure(KsProcgenFurnishingStatus.MandatoryUnmet, "RequiredPackCannotFit");

        bool SearchMandatory(int taskIndex)
        {
            if (taskIndex == mandatoryTasks.Count)
                return true;
            var task = mandatoryTasks[taskIndex];
            foreach (var core in task.Choices)
            {
                var clusterIndex = task.PackId == region.Theme.DominantEntityPackId ? dominantAnchors.Count : packCounts[task.PackId];
                KsProcgenAssemblyContinuation? continuation = null;
                if (taskIndex + 1 < mandatoryTasks.Count)
                    continuation = (candidate, witnesses, anchor, candidateProbes) =>
                    {
                        probes = candidateProbes;
                        var savedEntityCount = entities.Count;
                        var savedWitnessCount = relationWitnesses.Count;
                        var savedOccupied = occupied.ToArray();
                        var savedBlocked = blocked.ToArray();
                        var savedProtected = protectedCells.ToArray();
                        var savedAnchors = dominantAnchors.ToArray();
                        var savedDominantAnchor = dominantAnchor;
                        var savedCounts = packCounts.ToArray();
                        var savedCores = initializedCores.ToArray();
                        var savedPreferenceTruncation = preferenceSearchTruncated;
                        mandatoryFailure = CommitInitialCore(task.PackId, core, candidate, witnesses, anchor, false);
                        if (mandatoryFailure == null && SearchMandatory(taskIndex + 1))
                            return new KsProcgenAssemblyContinuationResult(true, probes, false);
                        entities.RemoveRange(savedEntityCount, entities.Count - savedEntityCount);
                        relationWitnesses.RemoveRange(savedWitnessCount, relationWitnesses.Count - savedWitnessCount);
                        occupied.Clear();
                        occupied.UnionWith(savedOccupied);
                        blocked.Clear();
                        blocked.UnionWith(savedBlocked);
                        protectedCells.Clear();
                        protectedCells.UnionWith(savedProtected);
                        dominantAnchors.Clear();
                        dominantAnchors.AddRange(savedAnchors);
                        dominantAnchor = savedDominantAnchor;
                        foreach (var (packId, count) in savedCounts)
                            packCounts[packId] = count;
                        initializedCores.Clear();
                        initializedCores.UnionWith(savedCores);
                        preferenceSearchTruncated = savedPreferenceTruncation;
                        mandatoryBacktracks++;
                        return new KsProcgenAssemblyContinuationResult(false, probes, mandatoryFailure != null);
                    };
                var placed = TryCore(task.PackId, core, floor, protectedCells, walls, occupied, blocked,
                    [], null, clusterIndex, seed, region.Id, maxCandidateProbes, relationPathBudget, ref probes,
                    out var accepted, out var acceptedAnchor, out var exhausted, out var unsupported, out var acceptedWitnesses,
                    continuation: continuation);
                if (mandatoryFailure != null)
                    return false;
                if (unsupported)
                {
                    mandatoryFailure = Failure(KsProcgenFurnishingStatus.InvalidInput, "AssemblyPlacementUnsupported");
                    return false;
                }
                if (exhausted && !placed)
                {
                    mandatoryFailure = Failure(KsProcgenFurnishingStatus.BudgetExceeded,
                        relationPathBudget.Truncated ? "FurnishingRelationPathBudget" : "FurnishingCandidateBudget",
                        relationPathBudget: relationPathBudget);
                    return false;
                }
                if (!placed)
                    continue;
                if (continuation == null)
                    mandatoryFailure = CommitInitialCore(task.PackId, core, accepted, acceptedWitnesses, acceptedAnchor, exhausted);
                return mandatoryFailure == null;
            }
            return false;
        }

        foreach (var packId in selectedPacks)
        {
            if (packId != region.Theme.DominantEntityPackId &&
                region.Theme.DominantEntityPackId != null && dominantAnchors.Count == 0)
            {
                if (compiledPacks[packId].Any(core => core.Weight > 0f && core.MinimumCount == 0 &&
                                                    !initializedCores.Contains((packId, core.Id))))
                    omissions.Add(new KsProcgenPackOmission(packId, "DominantPackUnavailable"));
                continue;
            }
            foreach (var core in KsProcgenCoreSelector.Order(compiledPacks[packId]
                         .Where(core => core.MinimumCount == 0 && !initializedCores.Contains((packId, core.Id)))
                         .ToArray(), seed, region.Id, packId, 0))
            {
                var failure = PlaceOptionalCore(packId, core);
                if (failure != null)
                    return failure;
            }
        }

        KsProcgenFurnishingResult? PlaceOptionalCore(string packId, KsProcgenResolvedEntityCore core)
        {
            var clusterIndex = packId == region.Theme.DominantEntityPackId ? dominantAnchors.Count : packCounts[packId];
            var placed = TryCore(packId, core, floor, protectedCells, walls,
                occupied, blocked, [], dominantAnchor, clusterIndex, seed, region.Id,
                maxCandidateProbes, relationPathBudget, ref probes,
                out var accepted, out var anchor, out var exhausted, out var unsupported, out var witnesses);
            if (unsupported)
                return Failure(KsProcgenFurnishingStatus.InvalidInput, "AssemblyPlacementUnsupported");
            if (exhausted && !placed)
                return Failure(KsProcgenFurnishingStatus.BudgetExceeded,
                    relationPathBudget.Truncated ? "FurnishingRelationPathBudget" : "FurnishingCandidateBudget",
                    relationPathBudget: relationPathBudget);
            if (!placed)
            {
                omissions.Add(new KsProcgenPackOmission(packId, $"CoreCannotFit/{core.Id}"));
                return null;
            }

            return CommitInitialCore(packId, core, accepted, witnesses, anchor, exhausted);
        }

        KsProcgenFurnishingResult? CommitInitialCore(string packId, KsProcgenResolvedEntityCore core,
            IReadOnlyList<KsProcgenEntityProposal> accepted, IReadOnlyList<KsProcgenAssemblyRelationWitness> witnesses,
            Vector2i? anchor, bool exhausted)
        {
            if (packId == region.Theme.DominantEntityPackId && dominantAnchor == null)
                dominantAnchor = anchor;
            relationWitnesses.AddRange(witnesses);
            preferenceSearchTruncated |= exhausted;
            if (packId == region.Theme.DominantEntityPackId && anchor.HasValue)
                dominantAnchors.Add(anchor.Value);
            packCounts[packId]++;
            initializedCores.Add((packId, core.Id));
            foreach (var entity in accepted)
            {
                entities.Add(entity);
                occupied.UnionWith(entity.OccupiedCells);
                if (entity.Movement != KsProcgenMovementClass.Clear)
                    blocked.UnionWith(entity.OccupiedCells);
            }
            if (!ReserveApproachPaths(accepted, root, floor, blocked, occupied, protectedCells))
                return Failure(KsProcgenFurnishingStatus.InvalidInput,
                    "FurnishingApproachRouteUnavailable");
            foreach (var cell in witnesses.SelectMany(witness => witness.CleanPath.Cells))
                if (!occupied.Contains(cell))
                    protectedCells.Add(cell);
            return null;
        }

        var averageCoreCells = dominantAnchors.Count == 0 ? 1d : entities
            .Where(entity => entity.PackId == region.Theme.DominantEntityPackId)
            .GroupBy(entity => entity.ClusterIndex)
            .Average(cluster => cluster.SelectMany(entity => entity.OccupiedCells).Distinct().Count());
        var targetClusters = dominantCores.Count == 0 || dominantAnchors.Count == 0 ? 0 :
            Math.Clamp((int) Math.Floor(floor.Count * region.Theme.FurnishingDensity /
                averageCoreCells), 1, 8);
        for (var cluster = dominantAnchors.Count; cluster < targetClusters; cluster++)
        {
            var placed = false;
            IReadOnlyList<KsProcgenEntityProposal> accepted = [];
            Vector2i? anchor = null;
            var exhausted = false;
            IReadOnlyList<KsProcgenAssemblyRelationWitness> witnesses = [];
            foreach (var core in KsProcgenCoreSelector.Order(dominantCores, seed, region.Id,
                         region.Theme.DominantEntityPackId!, cluster + 1))
            {
                placed = TryCore(region.Theme.DominantEntityPackId!, core, floor,
                    protectedCells, walls, occupied, blocked, dominantAnchors, null, cluster,
                    seed, region.Id,
                    maxCandidateProbes, relationPathBudget, ref probes, out accepted, out anchor, out exhausted,
                    out var unsupported, out witnesses);
                if (unsupported)
                    return Failure(KsProcgenFurnishingStatus.InvalidInput, "AssemblyPlacementUnsupported");
                if (placed || exhausted)
                    break;
            }
            if (!placed)
            {
                omissions.Add(new KsProcgenPackOmission(region.Theme.DominantEntityPackId!,
                    exhausted ? relationPathBudget.Truncated ? "FurnishingRelationPathBudget" :
                        "FurnishingDensityProbeBudget" : "FurnishingDensityUnmet"));
                break;
            }
            dominantAnchors.Add(anchor!.Value);
            relationWitnesses.AddRange(witnesses);
            preferenceSearchTruncated |= exhausted;
            foreach (var entity in accepted)
            {
                entities.Add(entity);
                occupied.UnionWith(entity.OccupiedCells);
                if (entity.Movement != KsProcgenMovementClass.Clear)
                    blocked.UnionWith(entity.OccupiedCells);
            }
            if (!ReserveApproachPaths(accepted, root, floor, blocked, occupied, protectedCells))
                return Failure(KsProcgenFurnishingStatus.InvalidInput,
                    "FurnishingApproachRouteUnavailable");
            foreach (var cell in witnesses.SelectMany(witness => witness.CleanPath.Cells))
                if (!occupied.Contains(cell))
                    protectedCells.Add(cell);
        }

        return new KsProcgenFurnishingResult
        {
            Status = omissions.Count == 0 && !preferenceSearchTruncated &&
                     relationWitnesses.All(witness => witness.State != KsProcgenConstraintState.Missed)
                ? KsProcgenFurnishingStatus.Proposed : KsProcgenFurnishingStatus.Sparse,
            Entities = entities,
            Omissions = omissions,
            ProtectedPassageCells = KsProcgenGeometry.SortCells(protectedCells),
            CandidateProbes = probes,
            TargetDominantClusters = targetClusters,
            PlacedDominantClusters = dominantAnchors.Count,
            RelationWitnesses = relationWitnesses,
            PreferenceSearchTruncated = preferenceSearchTruncated,
            RelationPathExpandedCells = relationPathBudget.ExpandedCells,
            RelationPathSearchTruncated = relationPathBudget.Truncated,
            MandatoryBacktracks = mandatoryBacktracks,
        };
    }

    private static bool TryCore(
        string packId,
        KsProcgenResolvedEntityCore core,
        IReadOnlySet<Vector2i> floor,
        IReadOnlySet<Vector2i> protectedCells,
        IReadOnlySet<Vector2i> walls,
        IReadOnlySet<Vector2i> alreadyOccupied,
        IReadOnlySet<Vector2i> alreadyBlocked,
        IReadOnlyCollection<Vector2i> previousDominantAnchors,
        Vector2i? preferredAnchor,
        int clusterIndex,
        int seed,
        string regionId,
        int maximumProbes,
        KsProcgenRelationPathBudget relationPathBudget,
        ref int probes,
        out IReadOnlyList<KsProcgenEntityProposal> accepted,
        out Vector2i? acceptedAnchor,
        out bool exhausted,
        out bool unsupported,
        out IReadOnlyList<KsProcgenAssemblyRelationWitness> witnesses,
        KsProcgenAssemblyContinuation? continuation = null)
    {
        accepted = [];
        acceptedAnchor = null;
        exhausted = false;
        unsupported = true;
        witnesses = [];
        foreach (var assembly in core.Variants)
        {
            // Unsupported variants are skipped whole; never salvage their members into another variant.
            if (assembly.Relations.Any(relation => !KsProcgenAssemblyRelationEvaluator.Supported(relation.Kind)) ||
                assembly.Members.Any(member => member.Entry.Role == KsProcgenEntityRole.Seat &&
                                               member.Entry.Footprint.Count != 1))
                continue;
            unsupported = false;
            var bestScore = -1;
            for (var assemblyTurn = 0; assemblyTurn < 4; assemblyTurn++)
            {
                var placed = TryAssembly(packId, core.Id, assembly, assemblyTurn, floor, protectedCells, walls,
                        alreadyOccupied, alreadyBlocked, previousDominantAnchors, preferredAnchor,
                        clusterIndex, seed, regionId, maximumProbes, relationPathBudget, ref probes,
                        out var candidate, out var candidateAnchor, out exhausted, out var candidateWitnesses,
                        continuation: continuation);
                if (placed)
                {
                    var score = candidateWitnesses.Count(witness => witness.Severity == KsProcgenRelationSeverity.Preferred &&
                        witness.State == KsProcgenConstraintState.Satisfied);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        accepted = candidate;
                        acceptedAnchor = candidateAnchor;
                        witnesses = candidateWitnesses;
                    }
                    if (continuation != null || candidateWitnesses.All(witness => witness.Severity != KsProcgenRelationSeverity.Preferred ||
                                                         witness.State != KsProcgenConstraintState.Missed))
                        return true;
                }
                if (exhausted)
                    return accepted.Count > 0;
                if (assembly.Members.All(member => member.RotationMode == KsProcgenMemberRotation.Independent))
                    break;
            }
            if (accepted.Count > 0)
                return true;
        }
        return false;
    }

    private static bool TryAssembly(
        string packId,
        string coreId,
        KsProcgenResolvedAssembly assembly,
        int assemblyTurn,
        IReadOnlySet<Vector2i> floor,
        IReadOnlySet<Vector2i> protectedCells,
        IReadOnlySet<Vector2i> walls,
        IReadOnlySet<Vector2i> alreadyOccupied,
        IReadOnlySet<Vector2i> alreadyBlocked,
        IReadOnlyCollection<Vector2i> previousDominantAnchors,
        Vector2i? preferredAnchor,
        int clusterIndex,
        int seed,
        string regionId,
        int maximumProbes,
        KsProcgenRelationPathBudget relationPathBudget,
        ref int probes,
        out IReadOnlyList<KsProcgenEntityProposal> accepted,
        out Vector2i? acceptedAnchor,
        out bool exhausted,
        out IReadOnlyList<KsProcgenAssemblyRelationWitness> witnesses,
        KsProcgenAssemblyContinuation? continuation = null)
    {
        accepted = [];
        acceptedAnchor = null;
        exhausted = false;
        witnesses = [];
        var members = assembly.Members.OrderBy(member => member.Id == assembly.AnchorMember ? 0 : 1)
            .ThenBy(member => member.Required ? 0 : 1)
            .ThenBy(member => RoleOrder(member.Entry.Role))
            .ThenBy(member => member.Id, StringComparer.Ordinal).ToArray();
        var centerX = floor.Sum(cell => (long) cell.X);
        var centerY = floor.Sum(cell => (long) cell.Y);
        var anchors = floor.Where(cell => !protectedCells.Contains(cell) && !alreadyOccupied.Contains(cell) &&
                                          previousDominantAnchors.All(previous => Distance(cell, previous) >= 3) &&
                                          (!preferredAnchor.HasValue || Distance(cell, preferredAnchor.Value) <= 4))
            .OrderByDescending(cell => assembly.Relations.Count(relation =>
                relation.Subject == assembly.AnchorMember && relation.Kind == KsProcgenRelationKind.AtCorner &&
                KsProcgenAssemblyRelationEvaluator.AtCorner(members[0].Entry.Footprint.Select(offset =>
                    cell + KsProcgenInteractionFacingPlanner.RotateFootprintOffset(offset,
                        members[0].RotationMode == KsProcgenMemberRotation.Independent
                            ? members[0].Entry.AllowedQuarterTurns[0]
                            : (assemblyTurn + members[0].LocalQuarterTurns) % 4)).ToArray(), walls)))
            .ThenBy(cell => preferredAnchor.HasValue
                ? Distance(cell, preferredAnchor.Value)
                : Math.Abs(cell.X * (long) floor.Count - centerX) +
                  Math.Abs(cell.Y * (long) floor.Count - centerY))
            .ThenBy(cell => CellRank(seed, regionId, packId, cell))
            .ThenBy(cell => cell.Y).ThenBy(cell => cell.X).ToArray();
        var search = new KsProcgenFloorAssemblySearch(assembly, packId, coreId, assemblyTurn,
            floor, protectedCells, walls, alreadyOccupied, alreadyBlocked, seed, regionId,
            clusterIndex, maximumProbes, probes, relationPathBudget, continuation: continuation);
        foreach (var anchor in anchors)
            if (search.ExploreAnchor(anchor))
                break;
        probes = search.Probes;
        accepted = search.Accepted;
        acceptedAnchor = search.AcceptedAnchor;
        exhausted = search.Exhausted;
        witnesses = search.Witnesses;
        return accepted.Count > 0;
    }

    internal static bool FacesTarget(Vector2i subject, int turn, Vector2i target)
    {
        var deltaX = (long) target.X - subject.X;
        var deltaY = (long) target.Y - subject.Y;
        var face = Faces[turn];
        return deltaX * face.Y == deltaY * face.X && deltaX * face.X + deltaY * face.Y > 0;
    }

    internal static bool ValidateTrial(
        IReadOnlyList<KsProcgenEntityProposal> trial,
        IReadOnlyList<KsProcgenEntityEntry> entries,
        KsProcgenResolvedAssembly assembly,
        IReadOnlySet<Vector2i> floor,
        IReadOnlySet<Vector2i> walls,
        IReadOnlySet<Vector2i> protectedCells,
        IReadOnlySet<Vector2i> alreadyBlocked,
        int seed,
        string regionId)
    {
        var blocked = new HashSet<Vector2i>(alreadyBlocked);
        blocked.UnionWith(walls);
        foreach (var entity in trial.Where(entity => entity.Movement != KsProcgenMovementClass.Clear))
            blocked.UnionWith(entity.OccupiedCells);
        var chairs = trial.Where(entity => entity.Role == KsProcgenEntityRole.Seat &&
                                           entity.Movement == KsProcgenMovementClass.Clear)
            .Select(entity => entity.Cell).ToHashSet();
        foreach (var entity in trial)
        {
            if (entity.DeclaredApproachLanding is { } landing &&
                (walls.Contains(landing) || KsProcgenTraversal.FindCleanPath(
                    KsProcgenGeometry.SortCells(protectedCells)[0], landing, floor, blocked).Count == 0))
                return false;
            if (entity.Role == KsProcgenEntityRole.Seat)
            {
                var chair = KsProcgenInteractionFacingPlanner.FindChairApproach(entity.Cell, floor,
                    blocked, protectedCells);
                if (!entity.InteractionApproach.HasValue || !chair.Approaches.Contains(entity.InteractionApproach.Value))
                    return false;
            }
            if (entity.Role != KsProcgenEntityRole.Seat && entity.InteractionApproach.HasValue)
            {
                var withoutSelf = new HashSet<Vector2i>(blocked);
                withoutSelf.ExceptWith(entity.OccupiedCells);
                var entry = entries.First(candidate => candidate.Id == entity.EntryId);
                var seatRelation = assembly.Relations.FirstOrDefault(relation =>
                    relation.Subject == entity.EntryId && relation.Kind == KsProcgenRelationKind.UsesSeat);
                var associatedChair = trial.FirstOrDefault(proposal =>
                    proposal.EntryId == seatRelation?.Target)?.Cell;
                var facing = KsProcgenInteractionFacingPlanner.ChooseMachineFacing(entity.Cell,
                    floor, walls, withoutSelf, chairs, protectedCells, [entity.QuarterTurns],
                    seed, $"{regionId}/{entity.PackId}/{entity.EntryId}",
                    associatedChair: associatedChair,
                    localFootprint: entry.Footprint);
                if (facing.Status != KsProcgenInteractionStatus.Ready ||
                    facing.Options.All(option => option.Approach != entity.InteractionApproach.Value))
                    return false;
            }
        }

        return true;
    }

    private static bool ReserveApproachPaths(
        IReadOnlyList<KsProcgenEntityProposal> accepted,
        Vector2i root,
        IReadOnlySet<Vector2i> floor,
        IReadOnlySet<Vector2i> blocked,
        IReadOnlySet<Vector2i> occupied,
        HashSet<Vector2i> protectedCells)
    {
        foreach (var entity in accepted)
        {
            foreach (var landing in new[] { entity.InteractionApproach, entity.DeclaredApproachLanding }.Distinct())
            {
                if (!landing.HasValue)
                    continue;
                var path = KsProcgenTraversal.FindCleanPath(root,
                    landing.Value, floor, blocked);
                if (path.Count == 0)
                    return false;
                foreach (var cell in path)
                    if (!occupied.Contains(cell))
                        protectedCells.Add(cell);
            }
        }
        return true;
    }

    internal static int RoleOrder(KsProcgenEntityRole role) => role switch
    {
        KsProcgenEntityRole.PrimaryFurniture => 0,
        KsProcgenEntityRole.Seat => 1,
        KsProcgenEntityRole.Equipment => 2,
        _ => 3,
    };

    private static bool IsRequired(KsProcgenThemedRegion region, string packId) =>
        region.UnplacedRequiredPacks.Any(required => required.PackId == packId);

    private static int Distance(Vector2i first, Vector2i second) =>
        Math.Abs(first.X - second.X) + Math.Abs(first.Y - second.Y);

    internal static ulong CellRank(int seed, string regionId, string packId, Vector2i cell)
    {
        var hash = KsProcgenStableHash.Create();
        hash.AddInt(seed);
        hash.AddString("furnishing-cell");
        hash.AddString(regionId);
        hash.AddString(packId);
        hash.AddInt(cell.X);
        hash.AddInt(cell.Y);
        return hash.Value;
    }

    private static KsProcgenFurnishingResult Failure(KsProcgenFurnishingStatus status, string code,
        KsProcgenRelationPathBudget? relationPathBudget = null) => new()
    {
        Status = status,
        Issue = new KsProcgenIssue(code, "Furnishing could not satisfy this room's content contract."),
        RelationPathExpandedCells = relationPathBudget?.ExpandedCells ?? 0,
        RelationPathSearchTruncated = relationPathBudget?.Truncated ?? false,
    };
}
