using System.Linq;
using System.Globalization;
using Robust.Shared.Prototypes;
using Robust.Shared.GameObjects;

namespace Content.Shared._KS14.Procedural;

/// <summary>A discarded attempt retains diagnostics, never reusable placement artifacts.</summary>
public sealed record KsProcgenOwnedConfigurationAttempt(int Ordinal, string ConfigurationId,
    KsProcgenOwnedPlanStatus Status, string? ReasonCode, int ExpandedCells, int FurnishingProbes,
    int OrderProbes, int PartitionMerges, int FixtureSelections, IReadOnlyList<KsProcgenOwnedFallback> Fallbacks)
{
    public int PartitionSeamProbes { get; init; }
    public int PreferredMaximumRoomCells { get; init; }
    public bool SizeGoalsRequested { get; init; }
    public int InfrastructureHomeOrdinal { get; init; }
    public IReadOnlyList<KsProcgenUtilityRoomChoice> UtilityRoomChoices { get; init; } = [];
}

public static partial class KsProcgenOwnedInteriorPlanner
{
    /// <summary>
    /// Plan from retained canonical input. Content, inspected host context and budgets are separate
    /// caller obligations; this request snapshot does not certify them. Gas planning additionally
    /// requires retained initial prototype facts; it does not initialize or certify an atmosphere.
    /// </summary>
    public static KsProcgenOwnedInteriorResult PlanSnapshot(IPrototypeManager prototypeManager, KsProcgenRequestSnapshot snapshot,
        string themeId, KsProcgenGroupRouteContext context, IReadOnlyList<KsProcgenExteriorWindowFact> exteriorFacts,
        KsProcgenOwnedPlanBudgets? budgets = null, KsProcgenThemeCatalog? content = null, IComponentFactory? componentFactory = null,
        KsProcgenGasPlanningContext? gasContext = null, KsProcgenElectricalPlanningContext? electricalContext = null)
    {
        if (snapshot == null)
            return new() { Status = KsProcgenOwnedPlanStatus.InvalidInput,
                Issue = new("MissingRequestSnapshot", "Planning needs a retained canonical request snapshot.") };
        return Plan(prototypeManager, snapshot.CopyRequest(), themeId, context, exteriorFacts, budgets: budgets, content: content,
            componentFactory: componentFactory, gasContext: gasContext, electricalContext: electricalContext);
    }

    /// <summary>Explicit proposal from inspected paint. Snapshotting alone never starts generation.</summary>
    public static KsProcgenOwnedInteriorResult Plan(IPrototypeManager prototypeManager, KsProcgenAreaBlob blob, int seed,
        KsProcgenGroupRouteContext context, IReadOnlyList<KsProcgenExteriorWindowFact> exteriorFacts,
        KsProcgenOwnedPlanBudgets? budgets = null, IComponentFactory? componentFactory = null)
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
            context, exteriorFacts, budgets: blob.CreateOwnedBudgets(budgets: budgets), componentFactory: componentFactory);
    }

    /// <summary>
    /// Seeded complete-alternative search. Rejected geometry/content and denied fallbacks advance
    /// through subdivision alternatives, then the next configuration. Invalid input and exhausted
    /// work stop immediately. Failed
    /// candidates cannot refund probes, expansions, merges or provisional fixture selections.
    /// </summary>
    public static KsProcgenOwnedInteriorResult Plan(IPrototypeManager prototypeManager, KsProcgenRequest request,
        string themeId, KsProcgenGroupRouteContext context, IReadOnlyList<KsProcgenExteriorWindowFact> exteriorFacts,
        KsProcgenOwnedPlanBudgets? budgets = null, KsProcgenThemeCatalog? content = null, IComponentFactory? componentFactory = null,
        KsProcgenGasPlanningContext? gasContext = null, KsProcgenElectricalPlanningContext? electricalContext = null)
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
            Furnishings = outcome.Furnishings, Lighting = outcome.Lighting, Access = outcome.Access, SizeMix = outcome.SizeMix, Gas = outcome.Gas, Electrical = outcome.Electrical,
            InfrastructureHomeCount = outcome.InfrastructureHomeCount, InfrastructureHomeOrdinal = outcome.InfrastructureHomeOrdinal,
            UtilityRoomChoices = outcome.UtilityRoomChoices,
            Fallbacks = outcome.Fallbacks, SemanticHash = outcome.SemanticHash,
            ContentDeclarationHash = content != null && content.Theme.Id == themeId ? content.DeclarationHash : 0,
            Stages = KsProcgenReadOnly.Copy(stages), Attempts = KsProcgenReadOnly.Copy(attempts),
            ExpandedCells = expanded, FurnishingProbes = furnishingProbes, OrderProbes = orderProbes,
            PartitionMerges = partitionMerges, PartitionSeamProbes = partitionSeamProbes, FixtureSelections = fixtureCount,
            ConfigurationProbes = configurationProbes,
        };
        KsProcgenOwnedInteriorResult Stop(KsProcgenOwnedPlanStatus status, string code) => Snapshot(new()
            { Status = status, Issue = new(code, "Complete configuration search could not validate an interior.") });
        if (gasContext != null || electricalContext != null)
        {
            if (prototypeManager == null) return Stop(KsProcgenOwnedPlanStatus.InvalidInput, "InvalidOwnedInteriorInput");
            if (!KsProcgenRequestSnapshot.TryCapture(prototypeManager, request, out var snapshot, out var captureIssue))
                return Stop(KsProcgenOwnedPlanStatus.InvalidInput, captureIssue!.Code);
            request = snapshot!.CopyRequest();
        }
        if (!ValidateInput(prototypeManager, request, themeId, context, exteriorFacts, budgets, out var shape, out var issue,
                content: content, gasContext: gasContext, electricalContext: electricalContext))
            return Stop(KsProcgenOwnedPlanStatus.InvalidInput, issue!.Code);
        if (content == null && !KsProcgenThemeCatalog.TryCapture(prototypeManager, themeId, out content, out issue))
            return Stop(KsProcgenOwnedPlanStatus.Rejected, issue!.Code);

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
                var frontier = new Queue<Dictionary<string, int>>();
                frontier.Enqueue(new(StringComparer.Ordinal));
                var seenChoices = new HashSet<string>(StringComparer.Ordinal) { string.Empty };
                var unavailable = new Dictionary<string, int>(StringComparer.Ordinal);
                var truncatedChoices = false;
                while (frontier.TryDequeue(out var geometryChoice))
                {
                    if (unavailable.Any(pair => (geometryChoice.TryGetValue(pair.Key, out var ordinal) ? ordinal : 0) >= pair.Value)) continue;
                    var infrastructureHomes = 1;
                    KsProcgenOwnedInteriorResult? geometryOutcome = null;
                    for (var infrastructureHome = 0; infrastructureHome < infrastructureHomes; infrastructureHome++)
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
                        var outcome = PlanConfiguration(prototypeManager, request, themeId, context, exteriorFacts, configuration.Id, remaining, content!, componentFactory, gasContext, electricalContext, infrastructureHome, geometryChoice);
                        geometryOutcome = outcome;
                        infrastructureHomes = outcome.InfrastructureHomeCount;
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
                                SizeGoalsRequested = layout.UseSizeGoals, InfrastructureHomeOrdinal = infrastructureHome,
                                UtilityRoomChoices = outcome.UtilityRoomChoices });
                        if (outcome.Status is KsProcgenOwnedPlanStatus.Candidate or KsProcgenOwnedPlanStatus.InvalidInput or KsProcgenOwnedPlanStatus.BudgetExceeded)
                            return Snapshot(outcome);
                        last = outcome;
                        // Subdivision cannot repair a floor failure; advance the entrance configuration directly.
                        if (outcome.Stages.All(stage => stage.Stage == KsProcgenOwnedPlanStage.Floor)) break;
                    }
                    if (geometryOutcome == null || geometryOutcome.Stages.All(stage => stage.Stage == KsProcgenOwnedPlanStage.Floor)) break;
                    foreach (var choice in geometryOutcome.UtilityRoomChoices)
                    {
                        if (!choice.Allocated && choice.IssueCode == "UtilityRoomNoPlacement")
                        {
                            if (!unavailable.TryGetValue(choice.NetworkId, out var bound) || choice.Ordinal < bound)
                                unavailable[choice.NetworkId] = choice.Ordinal;
                        }
                    }
                    foreach (var choice in geometryOutcome.UtilityRoomChoices.Where(choice => choice.Allocated)
                                 .OrderBy(choice => choice.NetworkId, StringComparer.Ordinal))
                    {
                        if (choice.Ordinal >= 65_535) { truncatedChoices = true; continue; }
                        var next = new Dictionary<string, int>(geometryChoice, StringComparer.Ordinal) { [choice.NetworkId] = choice.Ordinal + 1 };
                        if (unavailable.Any(pair => (next.TryGetValue(pair.Key, out var ordinal) ? ordinal : 0) >= pair.Value)) continue;
                        var key = string.Join(";", next.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
                            pair.Key.Length.ToString(CultureInfo.InvariantCulture) + ":" + pair.Key + "=" + pair.Value.ToString(CultureInfo.InvariantCulture)));
                        if (seenChoices.Contains(key)) continue;
                        if (seenChoices.Count >= budgets.MaximumLayoutProbes) { truncatedChoices = true; continue; }
                        seenChoices.Add(key); frontier.Enqueue(next);
                    }
                }
                if (truncatedChoices) return Stop(KsProcgenOwnedPlanStatus.BudgetExceeded, "OwnedUtilityGeometryProbeBudget");
                if (last?.Stages.All(stage => stage.Stage == KsProcgenOwnedPlanStage.Floor) == true) break;
            }
        }
        // Retain a specific terminal miss; only complete candidates carry an identity or placements.
        return last == null ? Stop(KsProcgenOwnedPlanStatus.Rejected, "NoOwnedInteriorConfiguration") : Snapshot(last);
    }

    private static bool ValidateInput(IPrototypeManager prototypeManager, KsProcgenRequest request, string themeId,
        KsProcgenGroupRouteContext context, IReadOnlyList<KsProcgenExteriorWindowFact> exteriorFacts,
        KsProcgenOwnedPlanBudgets budgets, out KsProcgenNormalizedShape? shape, out KsProcgenIssue? issue,
        KsProcgenThemeCatalog? content = null, KsProcgenGasPlanningContext? gasContext = null, KsProcgenElectricalPlanningContext? electricalContext = null)
    {
        if (!KsProcgenGeometry.TryNormalize(request, out shape, out issue))
            return false;
        if (!ValidateUtilityContexts(prototypeManager, request, themeId, shape!, content, gasContext, electricalContext, out issue))
            return false;
        if (content != null && content.Theme.Id != themeId)
        {
            issue = new("MismatchedThemeCatalog", "Captured theme content must match the requested theme.");
            return false;
        }
        if (gasContext == null && prototypeManager != null &&
            !KsProcgenAtmosphereProfiles.CheckPlanningSupport(prototypeManager, request, out issue, themeId: content == null ? themeId : null))
            return false;
        if (electricalContext == null && prototypeManager != null &&
            !KsProcgenElectricalProfiles.CheckPlanningSupport(prototypeManager, request, out issue, themeId: content == null ? themeId : null))
            return false;
        if (gasContext == null && content?.Theme.Atmosphere != null || electricalContext == null && content?.Theme.Electrical != null)
        {
            issue = gasContext == null && content.Theme.Atmosphere != null
                ? new("AtmospherePlanningUnsupported", "The owned planner cannot yet realize captured atmosphere defaults.")
                : new("ElectricalPlanningUnsupported", "The owned planner cannot yet realize captured electrical defaults.");
            return false;
        }
        if (prototypeManager == null || string.IsNullOrWhiteSpace(themeId) || context == null || exteriorFacts == null ||
            context.ExistingCleanDomainCells == null || context.CleanHostCells == null || context.ExistingBlockedThresholds == null ||
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
        if (context.ExteriorIsEmptySpace && (exteriorFacts.Count != 0 ||
            !context.HostContextComplete || !context.PreservedContextComplete ||
            context.ExistingCleanDomainCells.Count != 0 || context.CleanHostCells.Count != 0 || context.ExistingBlockedThresholds.Count != 0 ||
            shape.ConstantRegions.Count != 0 || shape.PreservedCells.Count != 0 || shape.Entrances.Count != 0))
        {
            issue = new("ConflictingEmptyExteriorContext", "Fresh empty exterior cannot replace authored or inspected host context.");
            return false;
        }
        return true;
    }
}
