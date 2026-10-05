using System.Globalization;
using System.Linq;

namespace Content.Shared._KS14.Procedural;

public enum KsProcgenPlanningDisposition : byte
{
    Proposed,
    Degraded,
    Rejected,
    NoOp,
}

public enum KsProcgenConstraintState : byte
{
    Satisfied,
    Missed,
    Unverified,
    NotApplicable,
}

public sealed record KsProcgenConstraintOutcome(
    string Id,
    KsProcgenConstraintState State,
    bool Hard,
    string? Requested = null,
    string? Achieved = null,
    string? ReasonCode = null);

public sealed record KsProcgenFallbackEvent(string Code, string Scope, string ReasonCode);

/// <summary>
/// A bounded report about planning facts only. Proposed and Degraded are never publication claims.
/// </summary>
public sealed class KsProcgenPlanningReport
{
    public KsProcgenPlanningDisposition Disposition { get; init; }
    public KsProcgenGeometryPipelineStatus PipelineStatus { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public int Seed { get; init; }
    public ulong SemanticHash { get; init; }
    public ulong? ConstantContractHash { get; init; }
    public bool SearchComplete { get; init; }
    public bool Published => false;
    public bool EntriesTruncated { get; init; }
    public IReadOnlyList<KsProcgenConstraintOutcome> Constraints { get; init; } = [];
    public IReadOnlyList<KsProcgenFallbackEvent> Fallbacks { get; init; } = [];
}

public static class KsProcgenPlanningReportBuilder
{
    public static KsProcgenPlanningReport Build(
        KsProcgenRequest request,
        KsProcgenGeometryPipelineResult result,
        int maxEntries = 4_096)
    {
        if (request == null || result == null || maxEntries <= 0 || maxEntries > 4_096)
            throw new ArgumentException("A request, pipeline result, and bounded entry limit are required.");

        var fallbackPolicy = request.FallbackPolicy ?? new KsProcgenFallbackPolicy();
        var windowGoal = request.WindowGoal ?? new KsProcgenWindowGoal();
        var sizeGoals = request.SizeMix ?? [];
        var constantCount = request.ConstantRegions?.Count ?? 0;

        var constraints = new List<KsProcgenConstraintOutcome>();
        var fallbacks = new List<KsProcgenFallbackEvent>();
        var truncated = false;
        var hardMiss = false;
        var softMiss = false;
        void Constraint(KsProcgenConstraintOutcome outcome)
        {
            hardMiss |= outcome.Hard && outcome.State == KsProcgenConstraintState.Missed;
            softMiss |= !outcome.Hard && outcome.State == KsProcgenConstraintState.Missed;
            if (constraints.Count < maxEntries)
                constraints.Add(outcome);
            else
                truncated = true;
        }
        void Fallback(KsProcgenFallbackEvent outcome)
        {
            if (fallbacks.Count < maxEntries)
                fallbacks.Add(outcome);
            else
                truncated = true;
        }

        var planned = result.Status == KsProcgenGeometryPipelineStatus.GeometryPlanned;
        var empty = planned && result.Packing?.CellClaims.Count == 0;
        Constraint(new KsProcgenConstraintOutcome("exact-cover",
            result.Packing?.Status == KsProcgenPackingStatus.GeometryReady
                ? KsProcgenConstraintState.Satisfied
                : result.Packing?.Status == KsProcgenPackingStatus.NoGeometricCover
                    ? KsProcgenConstraintState.Missed : KsProcgenConstraintState.Unverified,
            true, ReasonCode: result.Packing?.Issue?.Code));
        Constraint(new KsProcgenConstraintOutcome("preliminary-access",
            result.PortNetwork?.Status switch
            {
                KsProcgenPortNetworkStatus.Connected => KsProcgenConstraintState.Satisfied,
                KsProcgenPortNetworkStatus.Disconnected when
                    request.ConnectivityPolicy == KsProcgenConnectivityPolicy.PerIsland =>
                    KsProcgenConstraintState.Satisfied,
                KsProcgenPortNetworkStatus.Disconnected => KsProcgenConstraintState.Missed,
                _ => KsProcgenConstraintState.Unverified,
            }, true, ReasonCode: result.PortNetwork?.Issue?.Code));
        Constraint(new KsProcgenConstraintOutcome("operational-access",
            empty ? KsProcgenConstraintState.NotApplicable : KsProcgenConstraintState.Unverified, true));
        Constraint(new KsProcgenConstraintOutcome("constant-content",
            constantCount == 0 ? KsProcgenConstraintState.NotApplicable :
                KsProcgenConstraintState.Unverified, constantCount > 0,
            Requested: constantCount.ToString(CultureInfo.InvariantCulture),
            Achieved: result.ConstantContractHash.HasValue ? "declared-only" : null));
        Constraint(new KsProcgenConstraintOutcome("gas-closure",
            empty ? KsProcgenConstraintState.NotApplicable : KsProcgenConstraintState.Unverified, true,
            ReasonCode: result.HullBoundary?.Issue?.Code));
        var partitionState = result.Partition == null ? KsProcgenConstraintState.Unverified :
            result.Partition.Status == KsProcgenPartitionStatus.Proposed
                ? KsProcgenConstraintState.Satisfied :
            result.Partition.Status is KsProcgenPartitionStatus.MergedFallback or
                KsProcgenPartitionStatus.OpenFallback ? KsProcgenConstraintState.Missed :
            KsProcgenConstraintState.Unverified;
        Constraint(new KsProcgenConstraintOutcome("preferred-partitions", partitionState,
            !fallbackPolicy.AllowMergedPartition, ReasonCode: result.Partition?.Issue?.Code));

        foreach (var goal in sizeGoals.Where(goal => goal != null))
        {
            var outcome = result.FinalSizeMixOutcomes.FirstOrDefault(item => item.GoalId == goal.Id);
            var state = outcome == null ? KsProcgenConstraintState.Unverified :
                outcome.AchievedCount >= goal.TargetCount ? KsProcgenConstraintState.Satisfied :
                KsProcgenConstraintState.Missed;
            Constraint(new KsProcgenConstraintOutcome($"room-size:{goal.Id}", state,
                !fallbackPolicy.AllowRoomSizeShortfall,
                goal.TargetCount.ToString(CultureInfo.InvariantCulture),
                outcome?.AchievedCount.ToString(CultureInfo.InvariantCulture),
                state == KsProcgenConstraintState.Missed ? "RoomSizeShortfall" : null));
            if (state == KsProcgenConstraintState.Missed && fallbackPolicy.AllowRoomSizeShortfall)
                Fallback(new KsProcgenFallbackEvent("RoomSizeShortfall", goal.Id, "SoftRoomCountMissed"));
        }

        var windows = result.Windows;
        var windowState = windows == null ? KsProcgenConstraintState.Unverified :
            windows.Status == KsProcgenWindowPlanStatus.HardTargetUnmet &&
            windows.Issue?.Code == "WindowCountInfeasible" ? KsProcgenConstraintState.Unverified :
            windows.Status == KsProcgenWindowPlanStatus.HardTargetUnmet ? KsProcgenConstraintState.Missed :
            windows.Status == KsProcgenWindowPlanStatus.InvalidInput ? KsProcgenConstraintState.Unverified :
            windows.Status == KsProcgenWindowPlanStatus.NotApplicable ? KsProcgenConstraintState.NotApplicable :
            Math.Abs(windows.AchievedWindowCells - windows.RequestedWindowCells) <=
            windowGoal.ToleranceCells ? KsProcgenConstraintState.Satisfied :
            KsProcgenConstraintState.Missed;
        Constraint(new KsProcgenConstraintOutcome("window-fraction", windowState,
            windowGoal.HardFraction,
            windowGoal.ExteriorWindowFraction.ToString("R", CultureInfo.InvariantCulture),
            windows?.AchievedFraction.ToString("R", CultureInfo.InvariantCulture), windows?.Issue?.Code));
        var hasWindowCountBound = windowGoal.MinimumCount > 0 || windowGoal.MaximumCount.HasValue;
        var windowCountState = !hasWindowCountBound ? KsProcgenConstraintState.NotApplicable :
            windows == null || windows.Status == KsProcgenWindowPlanStatus.InvalidInput
                ? KsProcgenConstraintState.Unverified :
            windows.Issue?.Code == "WindowCountInfeasible" ||
            windows.AchievedWindowCells < windowGoal.MinimumCount ||
            windowGoal.MaximumCount.HasValue &&
            windows.AchievedWindowCells > windowGoal.MaximumCount.Value
                ? KsProcgenConstraintState.Missed : KsProcgenConstraintState.Satisfied;
        Constraint(new KsProcgenConstraintOutcome("window-count", windowCountState,
            hasWindowCountBound,
            $"{windowGoal.MinimumCount.ToString(CultureInfo.InvariantCulture)}.." +
            (windowGoal.MaximumCount?.ToString(CultureInfo.InvariantCulture) ?? "unbounded"),
            windows?.AchievedWindowCells.ToString(CultureInfo.InvariantCulture),
            windowCountState == KsProcgenConstraintState.Missed ? "WindowCountInfeasible" : null));
        if (windowState == KsProcgenConstraintState.Missed && !windowGoal.HardFraction)
            Fallback(new KsProcgenFallbackEvent("SoftWindowMiss", "exterior", "WindowFractionMissed"));
        Constraint(new KsProcgenConstraintOutcome("window-airtightness",
            windows?.EligibleCells > 0 ? KsProcgenConstraintState.Unverified :
                KsProcgenConstraintState.NotApplicable, true));

        if (fallbackPolicy.AllowMergedPartition && result.Partition?.Status is
            KsProcgenPartitionStatus.MergedFallback or KsProcgenPartitionStatus.OpenFallback)
            Fallback(new KsProcgenFallbackEvent("PartitionMerge", "generated",
                result.Partition.Issue?.Code ?? "PartitionFallback"));
        foreach (var furnished in result.Furnishings.OrderBy(item => item.RegionId, StringComparer.Ordinal))
        {
            foreach (var witness in furnished.Proposal.RelationWitnesses)
            {
                Constraint(new KsProcgenConstraintOutcome(
                    $"assembly-geometry:{furnished.RegionId}:{witness.PackId}:{witness.CoreId}:" +
                    $"{witness.ClusterIndex.ToString(CultureInfo.InvariantCulture)}:{witness.RelationId}",
                    witness.State, witness.Severity == KsProcgenRelationSeverity.Required,
                    Requested: witness.Kind == KsProcgenRelationKind.Near
                        ? $"{witness.MinimumDistance?.ToString(CultureInfo.InvariantCulture)}.." +
                          $"{witness.MaximumDistance?.ToString(CultureInfo.InvariantCulture)} clean steps" : witness.Kind.ToString(),
                    Achieved: witness.PathDistance?.ToString(CultureInfo.InvariantCulture),
                    ReasonCode: witness.ReasonCode));
            }
            if (furnished.Proposal.PreferenceSearchTruncated)
                Constraint(new KsProcgenConstraintOutcome($"assembly-preference-search:{furnished.RegionId}",
                    KsProcgenConstraintState.Unverified, false, ReasonCode: "FurnishingPreferenceProbeBudget"));
            if (furnished.Proposal.RelationPathSearchTruncated)
                Constraint(new KsProcgenConstraintOutcome($"assembly-path-search:{furnished.RegionId}",
                    KsProcgenConstraintState.Unverified, false, ReasonCode: "FurnishingRelationPathBudget"));
            if (!fallbackPolicy.AllowSparseFurnishing)
                continue;
            if (furnished.Proposal.Status == KsProcgenFurnishingStatus.Sparse &&
                furnished.Proposal.Omissions.Count == 0)
                Fallback(new KsProcgenFallbackEvent("SparseFurnishing", furnished.RegionId,
                    furnished.Proposal.RelationPathSearchTruncated ? "FurnishingRelationPathBudget" :
                    furnished.Proposal.PreferenceSearchTruncated ? "FurnishingPreferenceProbeBudget" :
                    furnished.Proposal.RelationWitnesses.Any(witness =>
                        witness.Severity == KsProcgenRelationSeverity.Preferred &&
                        witness.State == KsProcgenConstraintState.Missed) ? "PreferredAssemblyRelationMissed" :
                    furnished.Proposal.Issue?.Code ?? "SparseWithoutPack"));
            foreach (var omission in furnished.Proposal.Omissions.OrderBy(item => item.PackId,
                         StringComparer.Ordinal))
                Fallback(new KsProcgenFallbackEvent("SparseFurnishing",
                    $"{furnished.RegionId}:{omission.PackId}", omission.Reason));
        }
        foreach (var lit in result.Lighting.OrderBy(item => item.RegionId, StringComparer.Ordinal))
        {
            if (lit.Proposal.Status == KsProcgenLightingPlanStatus.Sparse &&
                fallbackPolicy.AllowSparseLighting)
                Fallback(new KsProcgenFallbackEvent("SparseLighting", lit.RegionId,
                    lit.Proposal.Issue?.Code ?? "EstimatedCoverageShortfall"));
        }
        var sparseFurnishing = result.Furnishings.Any(item =>
            item.Proposal.Status == KsProcgenFurnishingStatus.Sparse);
        Constraint(new KsProcgenConstraintOutcome("optional-furnishing", sparseFurnishing
            ? KsProcgenConstraintState.Missed : result.Furnishings.Count > 0
                ? KsProcgenConstraintState.Satisfied : KsProcgenConstraintState.Unverified,
            !fallbackPolicy.AllowSparseFurnishing,
            ReasonCode: sparseFurnishing ? "SparseFurnishing" : null));
        var sparseLighting = result.Lighting.Any(item =>
            item.Proposal.Status == KsProcgenLightingPlanStatus.Sparse);
        Constraint(new KsProcgenConstraintOutcome("estimated-lighting", sparseLighting
            ? KsProcgenConstraintState.Missed : result.Lighting.Count > 0
                ? KsProcgenConstraintState.Satisfied : KsProcgenConstraintState.Unverified,
            !fallbackPolicy.AllowSparseLighting,
            ReasonCode: sparseLighting ? "SparseLighting" : null));
        Constraint(new KsProcgenConstraintOutcome("mandatory-entity-packs",
            result.HasUnplacedRequiredEntityPacks ? KsProcgenConstraintState.Missed :
                planned ? KsProcgenConstraintState.Satisfied : KsProcgenConstraintState.Unverified,
            true));
        Constraint(new KsProcgenConstraintOutcome("working-lighting",
            result.Lighting.Any(item => item.Proposal.Lights.Count > 0)
                ? KsProcgenConstraintState.Unverified : KsProcgenConstraintState.NotApplicable,
            true));

        var disposition = !planned || hardMiss ? KsProcgenPlanningDisposition.Rejected :
            empty ? KsProcgenPlanningDisposition.NoOp :
            softMiss || fallbacks.Count > 0 ? KsProcgenPlanningDisposition.Degraded :
            KsProcgenPlanningDisposition.Proposed;
        return new KsProcgenPlanningReport
        {
            Disposition = disposition,
            PipelineStatus = result.Status,
            Issue = result.Issue,
            Seed = request.Seed,
            SemanticHash = planned ? result.SemanticHash : 0,
            ConstantContractHash = result.ConstantContractHash,
            SearchComplete = result.Packing?.SearchComplete ?? false,
            EntriesTruncated = truncated,
            Constraints = constraints,
            Fallbacks = fallbacks,
        };
    }
}
