using System.Linq;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Procedural;

/// <summary>A discarded attempt retains diagnostics, never reusable placement artifacts.</summary>
public sealed record KsProcgenOwnedConfigurationAttempt(int Ordinal, string ConfigurationId,
    KsProcgenOwnedPlanStatus Status, string? ReasonCode, int ExpandedCells, int FurnishingProbes,
    int OrderProbes, int PartitionMerges, int FixtureSelections, IReadOnlyList<KsProcgenOwnedFallback> Fallbacks)
{
    public int PartitionSeamProbes { get; init; }
    public int PreferredMaximumRoomCells { get; init; }
    public bool SizeGoalsRequested { get; init; }
}

public static partial class KsProcgenOwnedInteriorPlanner
{
    /// <summary>Explicit proposal from inspected paint. Snapshotting alone never starts generation.</summary>
    public static KsProcgenOwnedInteriorResult Plan(IPrototypeManager prototypeManager, KsProcgenAreaBlob blob, int seed,
        KsProcgenGroupRouteContext context, IReadOnlyList<KsProcgenExteriorWindowFact> exteriorFacts,
        KsProcgenOwnedPlanBudgets? budgets = null)
    {
        if (blob.Profile.GenerationGoals != null && !KsProcgenAreaGoals.Valid(blob.Profile.GenerationGoals))
            return new() { Status = KsProcgenOwnedPlanStatus.InvalidInput,
                Issue = new("InvalidOwnedInteriorInput", "The blob's generation goals are invalid.") };
        if (blob.Profile.RoomSubdivision is {} subdivision &&
            !KsProcgenRoomSubdivision.Valid(subdivision.MinimumRoomCells, subdivision.PreferredMaximumRoomCells,
                subdivision.AlternativeMaximumRoomCells))
            return new() { Status = KsProcgenOwnedPlanStatus.InvalidInput,
                Issue = new("InvalidOwnedInteriorInput", "The blob's subdivision preferences are invalid.") };
        return Plan(prototypeManager, blob.CreateRequest(seed), blob.Profile.Theme ?? string.Empty,
            context, exteriorFacts, budgets: blob.CreateOwnedBudgets(budgets: budgets));
    }

    /// <summary>
    /// Seeded complete-alternative search. Rejected geometry/content and denied fallbacks advance
    /// through subdivision alternatives, then the next configuration. Invalid input and exhausted
    /// work stop immediately. Failed
    /// candidates cannot refund probes, expansions, merges or provisional fixture selections.
    /// </summary>
    public static KsProcgenOwnedInteriorResult Plan(IPrototypeManager prototypeManager, KsProcgenRequest request,
        string themeId, KsProcgenGroupRouteContext context, IReadOnlyList<KsProcgenExteriorWindowFact> exteriorFacts,
        KsProcgenOwnedPlanBudgets? budgets = null)
    {
        budgets ??= new();
        var stages = new List<KsProcgenOwnedStageOutcome>();
        var attempts = new List<KsProcgenOwnedConfigurationAttempt>();
        var expanded = 0;
        var furnishingProbes = 0;
        var orderProbes = 0;
        var partitionMerges = 0;
        var partitionSeamProbes = 0;
        var fixtureCount = 0;
        var configurationProbes = 0;
        KsProcgenOwnedInteriorResult Snapshot(KsProcgenOwnedInteriorResult outcome) => new()
        {
            Status = outcome.Status, Issue = outcome.Issue,
            Floor = outcome.Floor, Partition = outcome.Partition, Themes = outcome.Themes, Structure = outcome.Structure,
            Furnishings = outcome.Furnishings, Lighting = outcome.Lighting, Access = outcome.Access, SizeMix = outcome.SizeMix,
            Fallbacks = outcome.Fallbacks, SemanticHash = outcome.SemanticHash,
            Stages = KsProcgenReadOnly.Copy(stages), Attempts = KsProcgenReadOnly.Copy(attempts),
            ExpandedCells = expanded, FurnishingProbes = furnishingProbes, OrderProbes = orderProbes,
            PartitionMerges = partitionMerges, PartitionSeamProbes = partitionSeamProbes, FixtureSelections = fixtureCount,
            ConfigurationProbes = configurationProbes,
        };
        KsProcgenOwnedInteriorResult Stop(KsProcgenOwnedPlanStatus status, string code) => Snapshot(new()
            { Status = status, Issue = new(code, "Complete configuration search could not validate an interior.") });
        if (!ValidateInput(prototypeManager, request, themeId, context, exteriorFacts, budgets, out var shape, out var issue))
            return Stop(KsProcgenOwnedPlanStatus.InvalidInput, issue!.Code);

        KsProcgenOwnedInteriorResult? last = null;
        // Snapshot caller-owned profile order before search; a failed layout never contributes geometry.
        var roomSizes = new[] { budgets.PreferredMaximumRoomCells }.Concat(budgets.AlternativeMaximumRoomCells).ToArray();
        var layouts = roomSizes.Select(size => (Size: size, UseSizeGoals: false)).ToList();
        if (budgets.UseSizeGoals && request.SizeMix.Count != 0)
            layouts.Insert(0, (budgets.PreferredMaximumRoomCells, true));
        foreach (var configuration in KsProcgenEntranceConfigurationOrder.Order(shape!.EntranceContract!.Configurations,
                     request.Seed, request.RequestId))
        {
            var exhaustedCode = configurationProbes >= budgets.MaximumConfigurationProbes ? "GroupFloorCandidateBudget" : null;
            if (exhaustedCode != null)
            {
                stages.Add(new(KsProcgenOwnedPlanStage.Floor, null, false, exhaustedCode, 0, 0)
                {
                    ConfigurationId = configuration.Id, LayoutOrdinal = attempts.Count,
                    PreferredMaximumRoomCells = roomSizes[0],
                    SizeGoalsRequested = layouts[0].UseSizeGoals,
                });
                return Stop(KsProcgenOwnedPlanStatus.BudgetExceeded, exhaustedCode);
            }
            var firstLayout = true;
            foreach (var layout in layouts)
            {
                var roomSize = layout.Size;
                exhaustedCode = attempts.Count >= budgets.MaximumLayoutProbes ? "OwnedLayoutProbeBudget"
                    : furnishingProbes >= budgets.MaximumFurnishingProbes ? "OwnedFurnishingProbeBudget" : null;
                if (exhaustedCode != null)
                {
                    stages.Add(new(KsProcgenOwnedPlanStage.Floor, null, false, exhaustedCode, 0, 0)
                    {
                        ConfigurationId = configuration.Id, LayoutOrdinal = attempts.Count,
                        PreferredMaximumRoomCells = roomSize,
                        SizeGoalsRequested = layout.UseSizeGoals,
                    });
                    return Stop(KsProcgenOwnedPlanStatus.BudgetExceeded, exhaustedCode);
                }
                if (firstLayout) configurationProbes++;
                firstLayout = false;
                var remaining = budgets with
                {
                    MaximumReportedGraphExpansions = budgets.MaximumReportedGraphExpansions - expanded,
                    MaximumConfigurationProbes = 1,
                    MaximumOrderProbes = budgets.MaximumOrderProbes - orderProbes,
                    MaximumPartitionMerges = budgets.MaximumPartitionMerges - partitionMerges,
                    MaximumPartitionSeamProbes = budgets.MaximumPartitionSeamProbes - partitionSeamProbes,
                    MaximumFurnishingProbes = budgets.MaximumFurnishingProbes - furnishingProbes,
                    MaximumLightingFixtures = budgets.MaximumLightingFixtures - fixtureCount,
                    PreferredMaximumRoomCells = roomSize,
                    AlternativeMaximumRoomCells = [],
                    UseSizeGoals = layout.UseSizeGoals,
                };
                var outcome = PlanConfiguration(prototypeManager, request, themeId, context, exteriorFacts, configuration.Id, remaining);
                expanded += outcome.ExpandedCells;
                furnishingProbes += outcome.FurnishingProbes;
                orderProbes += outcome.OrderProbes;
                partitionMerges += outcome.PartitionMerges;
                partitionSeamProbes += outcome.PartitionSeamProbes;
                fixtureCount += outcome.FixtureSelections;
                foreach (var stage in outcome.Stages)
                    stages.Add(stage with
                    {
                        ConfigurationId = configuration.Id, LayoutOrdinal = attempts.Count,
                        PreferredMaximumRoomCells = roomSize,
                        SizeGoalsRequested = layout.UseSizeGoals,
                    });
                attempts.Add(new(attempts.Count, configuration.Id, outcome.Status, outcome.Issue?.Code, outcome.ExpandedCells,
                    outcome.FurnishingProbes, outcome.OrderProbes, outcome.PartitionMerges, outcome.FixtureSelections, outcome.Fallbacks)
                    { PartitionSeamProbes = outcome.PartitionSeamProbes, PreferredMaximumRoomCells = roomSize,
                        SizeGoalsRequested = layout.UseSizeGoals });
                if (outcome.Status is KsProcgenOwnedPlanStatus.Candidate or KsProcgenOwnedPlanStatus.InvalidInput or KsProcgenOwnedPlanStatus.BudgetExceeded)
                    return Snapshot(outcome);
                last = outcome;
                // Subdivision cannot repair a floor failure; advance the entrance configuration directly.
                if (outcome.Stages.All(stage => stage.Stage == KsProcgenOwnedPlanStage.Floor)) break;
            }
        }
        // Retain a specific terminal miss; only complete candidates carry an identity or placements.
        return last == null ? Stop(KsProcgenOwnedPlanStatus.Rejected, "NoOwnedInteriorConfiguration") : Snapshot(last);
    }

    private static bool ValidateInput(IPrototypeManager prototypeManager, KsProcgenRequest request, string themeId,
        KsProcgenGroupRouteContext context, IReadOnlyList<KsProcgenExteriorWindowFact> exteriorFacts,
        KsProcgenOwnedPlanBudgets budgets, out KsProcgenNormalizedShape? shape, out KsProcgenIssue? issue)
    {
        if (!KsProcgenGeometry.TryNormalize(request, out shape, out issue))
            return false;
        if (prototypeManager == null || string.IsNullOrWhiteSpace(themeId) || context == null || exteriorFacts == null ||
            exteriorFacts.Count > 16_384 || request.SizeMix.Count > 64 || shape!.EntranceContract == null || request.Mode != KsProcgenMode.Procedural ||
            request.GeometryMode != KsProcgenGeometryMode.Footprint || shape.TargetCells.Count > 4_096 ||
            budgets.MaximumReportedGraphExpansions is < 0 or > 1_000_000 || budgets.MaximumConfigurationProbes is < 0 or > 64 ||
            budgets.MaximumLayoutProbes is < 0 or > 64 ||
            !KsProcgenRoomSubdivision.Valid(budgets.MinimumRoomCells, budgets.PreferredMaximumRoomCells,
                budgets.AlternativeMaximumRoomCells) ||
            budgets.MaximumOrderProbes is < 0 or > 64 || budgets.MaximumPartitionMerges is < 0 or > 64 ||
            budgets.MaximumPartitionSeamProbes is < 0 or > 1_000_000 ||
            budgets.MaximumFurnishingProbes is < 1 or > 65_536 || budgets.MaximumLightingFixtures is < 0 or > 4_096 ||
            budgets.MaximumContentRoomCells is < 1 or > 512)
        {
            issue = new("InvalidOwnedInteriorInput", "A bounded procedural footprint and entrance contract are required.");
            return false;
        }
        return true;
    }
}
