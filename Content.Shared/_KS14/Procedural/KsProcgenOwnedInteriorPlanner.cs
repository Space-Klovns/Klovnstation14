using System.Linq;
using Robust.Shared.Prototypes;
using Robust.Shared.GameObjects;

namespace Content.Shared._KS14.Procedural;

public enum KsProcgenOwnedPlanStatus : byte { Candidate, InvalidInput, Rejected, BudgetExceeded, FallbackDisallowed }
public enum KsProcgenOwnedPlanStage : byte { Floor, Partition, Themes, Structure, Furnishing, Lighting, InteriorAudit, GasBinding, Infrastructure, ElectricalRouting }
public sealed record KsProcgenOwnedStageOutcome(KsProcgenOwnedPlanStage Stage, string? RegionId,
    bool Completed, string? ReasonCode, int ExpandedCells, int CandidateProbes)
{
    public string? ConfigurationId { get; init; }
    public int LayoutOrdinal { get; init; }
    public int PreferredMaximumRoomCells { get; init; }
    public bool SizeGoalsRequested { get; init; }
    public int PartitionSeamProbes { get; init; }
}
public sealed record KsProcgenOwnedFallback(string Code, string? RegionId);

/// <summary>Shared reported work; allocation, sorting and content compilation remain input bounded.</summary>
public sealed record KsProcgenOwnedPlanBudgets
{
    public int MaximumReportedGraphExpansions { get; init; } = 262_144;
    public int MaximumConfigurationProbes { get; init; } = 64;
    public int MaximumLayoutProbes { get; init; } = 64;
    public int MaximumOrderProbes { get; init; } = 64;
    public int MaximumPartitionMerges { get; init; } = 64;
    public int MaximumPartitionSeamProbes { get; init; } = 262_144;
    public int MaximumFurnishingProbes { get; init; } = 4_096;
    public int MaximumLightingFixtures { get; init; } = 64;
    public int PreferredMaximumRoomCells { get; init; } = 24;
    /// <summary>Ordered subdivision alternatives after the preferred size; no duplicates, at most 63.</summary>
    public IReadOnlyList<int> AlternativeMaximumRoomCells { get; init; } = [];
    public int MinimumRoomCells { get; init; } = 4;
    /// <summary>Try size-goal strip subdivision before the scalar ceiling strategy.</summary>
    public bool UseSizeGoals { get; init; }
    public int MaximumContentRoomCells { get; init; } = 512;
}

/// <summary>Only complete proposals expose stage artifacts. Reports describe unpublished attempts.</summary>
public sealed class KsProcgenOwnedInteriorResult
{
    public IReadOnlyList<KsProcgenUtilityRoomChoice> UtilityRoomChoices { get; init; } = [];
    public KsProcgenOwnedPlanStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public KsProcgenGroupFloorSelection? Floor { get; init; }
    public KsProcgenGroupPartitionResult? Partition { get; init; }
    public KsProcgenThemeAssignmentResult? Themes { get; init; }
    public KsProcgenGroupWindowResult? Structure { get; init; }
    public IReadOnlyList<KsProcgenFurnishedRegion> Furnishings { get; init; } = [];
    public KsProcgenGroupLightingResult? Lighting { get; init; }
    public KsProcgenGroupInteriorResult? Access { get; init; }
    public KsProcgenGasBindingResult? Gas { get; init; }
    public KsProcgenElectricalPlan? Electrical { get; init; }
    public int InfrastructureHomeCount { get; init; } = 1;
    public int InfrastructureHomeOrdinal { get; init; }
    public IReadOnlyList<KsProcgenSizeMixOutcome> SizeMix { get; init; } = [];
    public IReadOnlyList<KsProcgenOwnedStageOutcome> Stages { get; init; } = [];
    public IReadOnlyList<KsProcgenOwnedFallback> Fallbacks { get; init; } = [];
    public int ExpandedCells { get; init; }
    public int FurnishingProbes { get; init; }
    public int OrderProbes { get; init; }
    public int PartitionMerges { get; init; }
    public int PartitionSeamProbes { get; init; }
    /// <summary>Provisional selections across every attempt, including discarded lights.</summary>
    public int FixtureSelections { get; init; }
    public IReadOnlyList<KsProcgenOwnedConfigurationAttempt> Attempts { get; init; } = [];
    public int ConfigurationProbes { get; init; }
    public int LayoutProbes => Attempts.Count;
    public ulong SemanticHash { get; init; }
    /// <summary>Retained planning declarations only; not an engine/content verification receipt.</summary>
    public ulong ContentDeclarationHash { get; init; }
    public bool GraphWorkFullyMetered => false;
    public bool EngineAccessVerified => false;
    public bool WorkingLightingVerified => false;
    public bool GasClosureVerified => false;
    public bool Published => false;
}

/// <summary>
/// Atomic composition for procedural entrance-aware footprints, including constant reservations.
/// Retry ordered subdivisions within complete configurations through partition/theme/structure/content/light and
/// independent interior access, sharing work allowances. No speculative engine allocation.
/// Shared graph/probe/fixture budgets include furnishing access and partition seam searches.
/// Allocation, sorting and content compilation remain input bounded. A Candidate is never an engine success.
/// </summary>
public static partial class KsProcgenOwnedInteriorPlanner
{
    private static KsProcgenOwnedInteriorResult PlanConfiguration(IPrototypeManager prototypeManager, KsProcgenRequest request,
        string themeId, KsProcgenGroupRouteContext context, IReadOnlyList<KsProcgenExteriorWindowFact> exteriorFacts,
        string configurationId, KsProcgenOwnedPlanBudgets budgets, KsProcgenThemeCatalog content, IComponentFactory? componentFactory,
        KsProcgenGasPlanningContext? gasContext, KsProcgenElectricalPlanningContext? electricalContext, int infrastructureHomeOrdinal,
        IReadOnlyDictionary<string, int> utilityRoomOrdinals)
    {
        var infrastructureHomeCount = 1;
        IReadOnlyList<KsProcgenUtilityRoomChoice> utilityChoices = [];
        var expanded = 0;
        var furnishingProbes = 0;
        var orderProbes = 0;
        var partitionMerges = 0;
        var partitionSeamProbes = 0;
        var fixtureCount = 0;
        var stages = new List<KsProcgenOwnedStageOutcome>();
        var fallbacks = new List<KsProcgenOwnedFallback>();
        KsProcgenOwnedInteriorResult Fail(KsProcgenOwnedPlanStatus status, string code, string? details = null) => new()
        {
            Status = status, Issue = new(code, details ?? "The complete owned interior proposal did not validate."),
            Stages = KsProcgenReadOnly.Copy(stages), Fallbacks = KsProcgenReadOnly.Copy(fallbacks),
            ExpandedCells = expanded, FurnishingProbes = furnishingProbes,
            OrderProbes = orderProbes, PartitionMerges = partitionMerges, PartitionSeamProbes = partitionSeamProbes,
            FixtureSelections = fixtureCount,
            InfrastructureHomeCount = infrastructureHomeCount, InfrastructureHomeOrdinal = infrastructureHomeOrdinal,
            UtilityRoomChoices = utilityChoices,
        };
        void Record(KsProcgenOwnedPlanStage stage, bool completed, string? code, int work, int probes = 0,
            string? regionId = null, int seamProbes = 0)
        {
            expanded += work;
            stages.Add(new(stage, regionId, completed, code, work, probes) { PartitionSeamProbes = seamProbes });
        }
        KsProcgenOwnedInteriorResult Deny(KsProcgenOwnedPlanStage stage, string code, string? regionId)
        {
            Record(stage, false, code, 0, regionId: regionId);
            return Fail(KsProcgenOwnedPlanStatus.FallbackDisallowed, code);
        }
        static KsProcgenOwnedPlanStatus SceneStatus(KsProcgenEntranceSceneStatus status) => status == KsProcgenEntranceSceneStatus.BudgetExceeded
            ? KsProcgenOwnedPlanStatus.BudgetExceeded : status == KsProcgenEntranceSceneStatus.InvalidInput
                ? KsProcgenOwnedPlanStatus.InvalidInput : KsProcgenOwnedPlanStatus.Rejected;
        if (!ValidateInput(prototypeManager, request, themeId, context, exteriorFacts, budgets, out var shape, out var issue,
                content: content, gasContext: gasContext, electricalContext: electricalContext))
            return Fail(KsProcgenOwnedPlanStatus.InvalidInput, issue!.Code);
        int RemainingGraph() => budgets.MaximumReportedGraphExpansions - expanded;
        var floorProposal = KsProcgenGroupFloorPlanner.Plan(request, configurationId, context,
            maximumOrderProbes: budgets.MaximumOrderProbes,
            maximumExpandedCells: RemainingGraph());
        orderProbes = floorProposal.OrderProbes;
        var floor = new KsProcgenGroupFloorSelection
        {
            Status = floorProposal.Status, Issue = floorProposal.Issue, CandidateProbes = 1,
            OrderProbes = orderProbes, ExpandedCells = floorProposal.ExpandedCells,
            Accepted = floorProposal.Status == KsProcgenEntranceSceneStatus.Candidate ? floorProposal : null,
            SelectionHash = floorProposal.Status == KsProcgenEntranceSceneStatus.Candidate
                ? KsProcgenGroupFloorPlanner.SelectionHash(request, floorProposal) : 0,
        };
        Record(KsProcgenOwnedPlanStage.Floor, floor.Status == KsProcgenEntranceSceneStatus.Candidate, floor.Issue?.Code,
            floor.ExpandedCells, probes: floor.CandidateProbes);
        if (floor.Status != KsProcgenEntranceSceneStatus.Candidate) return Fail(SceneStatus(floor.Status), floor.Issue!.Code);
        if (electricalContext?.Profile.Distribution?.MediumVoltageCirculationBackbone == true &&
            request.Utilities?.CableWriteCells is { } cableMask)
        {
            var allowedCable = cableMask.ToHashSet(); var checkedCells = 0;
            foreach (var cell in floor.Accepted!.ProtectedCells)
            {
                if (checkedCells >= RemainingGraph())
                {
                    Record(KsProcgenOwnedPlanStage.ElectricalRouting, false, "ElectricalRoutingWorkBudget", checkedCells);
                    return Fail(KsProcgenOwnedPlanStatus.BudgetExceeded, "ElectricalRoutingWorkBudget");
                }
                checkedCells++;
                if (allowedCable.Contains(cell)) continue;
                Record(KsProcgenOwnedPlanStage.ElectricalRouting, false, "ElectricalMediumServicePermissionRequired", checkedCells);
                return Fail(KsProcgenOwnedPlanStatus.Rejected, "ElectricalMediumServicePermissionRequired");
            }
            Record(KsProcgenOwnedPlanStage.ElectricalRouting, true, null, checkedCells);
        }
        var partition = KsProcgenGroupPartitionPlanner.Plan(request, floor.Accepted!,
            preferredMaxRoomCells: budgets.PreferredMaximumRoomCells, minimumRoomCells: budgets.MinimumRoomCells,
            maximumMergeAttempts: budgets.MaximumPartitionMerges, maximumExpandedCells: RemainingGraph(),
            maximumSeamProbes: budgets.MaximumPartitionSeamProbes, useSizeGoals: budgets.UseSizeGoals,
            electricalDistribution: electricalContext?.Profile.Distribution,
            utilityRoomOrdinals: utilityRoomOrdinals, preferredUtilityInteriorCells: electricalContext?.PreferredUtilityInteriorCells ?? 0);
        utilityChoices = partition.UtilityRoomChoices;
        partitionMerges = partition.MergeAttempts;
        partitionSeamProbes = partition.SeamProbes;
        Record(KsProcgenOwnedPlanStage.Partition, partition.Status == KsProcgenEntranceSceneStatus.Candidate, partition.Issue?.Code,
            partition.ExpandedCells, probes: partition.MergeAttempts, seamProbes: partition.SeamProbes);
        if (partition.Status != KsProcgenEntranceSceneStatus.Candidate) return Fail(SceneStatus(partition.Status), partition.Issue!.Code);
        foreach (var network in partition.Regions.Where(network => network.Partition.Status != KsProcgenPartitionStatus.Proposed))
        {
            if (!request.FallbackPolicy.AllowMergedPartition) return Deny(KsProcgenOwnedPlanStage.Partition, "MergedPartitionDisallowed", network.Id);
            fallbacks.Add(new("MergedPartition", network.Id));
        }
        var themes = KsProcgenGroupThemePlanner.Plan(prototypeManager, themeId, request.Seed, partition, content: content,
            gasContext: gasContext, electricalContext: electricalContext, utilityContent: electricalContext?.UtilityContent);
        Record(KsProcgenOwnedPlanStage.Themes, themes.Status == KsProcgenThemeAssignmentStatus.Selected, themes.Issue?.Code, 0);
        if (themes.Status != KsProcgenThemeAssignmentStatus.Selected)
            return Fail(themes.Status == KsProcgenThemeAssignmentStatus.InvalidInput ? KsProcgenOwnedPlanStatus.InvalidInput :
                KsProcgenOwnedPlanStatus.Rejected, themes.Issue!.Code);
        var sizeMix = KsProcgenReadOnly.Copy(KsProcgenSizeMixAnalyzer.Analyze(request.SizeMix, themes.Regions));
        foreach (var outcome in sizeMix.Where(outcome => outcome.AchievedCount != outcome.RequestedCount))
        {
            if (!request.FallbackPolicy.AllowRoomSizeShortfall) return Deny(KsProcgenOwnedPlanStage.Themes, "RoomSizeShortfallDisallowed", outcome.GoalId);
            fallbacks.Add(new("RoomSizeDeviation", outcome.GoalId));
        }
        var structure = KsProcgenGroupWindowPlanner.Plan(prototypeManager, request, partition, themes, exteriorFacts,
            maximumExpandedCells: RemainingGraph(), emptyExterior: context.ExteriorIsEmptySpace);
        Record(KsProcgenOwnedPlanStage.Structure, structure.Status == KsProcgenGroupWindowStatus.Planned, structure.Issue?.Code,
            structure.ExpandedCells);
        if (structure.Status != KsProcgenGroupWindowStatus.Planned)
            return Fail(structure.Status == KsProcgenGroupWindowStatus.BudgetExceeded ? KsProcgenOwnedPlanStatus.BudgetExceeded :
                structure.Status == KsProcgenGroupWindowStatus.InvalidInput ? KsProcgenOwnedPlanStatus.InvalidInput :
                KsProcgenOwnedPlanStatus.Rejected, structure.Issue!.Code);
        IReadOnlyDictionary<string, IReadOnlyList<KsProcgenRoomCoreSelection>> infrastructure =
            new Dictionary<string, IReadOnlyList<KsProcgenRoomCoreSelection>>(StringComparer.Ordinal);
        if (electricalContext != null)
        {
            infrastructureHomeCount = electricalContext.HomeCount(themes);
            var selected = electricalContext.TryInfrastructure(request, shape!, themes, infrastructureHomeOrdinal, out infrastructure, out issue);
            Record(KsProcgenOwnedPlanStage.Infrastructure, selected, issue?.Code, 0);
            if (!selected) return Fail(KsProcgenOwnedPlanStatus.Rejected, issue!.Code);
        }
        var furnishings = new List<KsProcgenFurnishedRegion>();
        var inspectedWalls = structure.RemainingWalls.Select(wall => wall.Cell).ToHashSet();
        var windowCells = structure.Windows.Select(window => window.Cell).ToHashSet();
        foreach (var region in themes.Regions.OrderBy(region => region.Id, StringComparer.Ordinal))
        {
            if (furnishingProbes >= budgets.MaximumFurnishingProbes)
            {
                Record(KsProcgenOwnedPlanStage.Furnishing, false, "OwnedFurnishingProbeBudget", 0, regionId: region.Id);
                return Fail(KsProcgenOwnedPlanStatus.BudgetExceeded, "OwnedFurnishingProbeBudget");
            }
            var network = partition.Regions.Single(network => network.GroupIds.SequenceEqual(region.EntranceGroupIds));
            // Read-only floor must also be unavailable to optional furniture and light footprints.
            var protectedRegion = region with { ProtectedPassageCells = KsProcgenGeometry.SortCells(
                region.ProtectedPassageCells.Concat(region.FloorCells.Where(cell => !shape!.CanWrite(cell)))
                    .Concat(network.Partition.DoorOpenings.SelectMany(door => new[] { door.Threshold, door.InsideApproach, door.OutsideApproach })
                        .Where(region.FloorCells.Contains)).Distinct()) };
            // A substituted window is no longer an opaque backing wall for furniture relations.
            var contentPartition = new KsProcgenPartitionResult
            {
                Status = network.Partition.Status, Issue = network.Partition.Issue,
                FloorCells = network.Partition.FloorCells,
                WallCells = KsProcgenGeometry.SortCells(network.Partition.WallCells.Where(cell => !windowCells.Contains(cell))),
                DoorOpenings = network.Partition.DoorOpenings, MergedZoneGroups = network.Partition.MergedZoneGroups,
                RejectedSplits = network.Partition.RejectedSplits,
            };
            var requiredInfrastructure = infrastructure.GetValueOrDefault(region.Id) ?? [];
            if (requiredInfrastructure.Count != 0 && componentFactory == null)
                return Fail(KsProcgenOwnedPlanStatus.InvalidInput, "ElectricalComponentFactoryRequired");
            var proposal = componentFactory != null && (requiredInfrastructure.Count != 0 || KsProcgenLayeredFurnishingPlanner.Needed(protectedRegion))
                ? KsProcgenLayeredFurnishingPlanner.Plan(prototypeManager, componentFactory, protectedRegion, contentPartition, inspectedWalls,
                    request.Seed, Math.Min(4_096, budgets.MaximumFurnishingProbes - furnishingProbes), RemainingGraph(), budgets.MaximumContentRoomCells, infrastructure: requiredInfrastructure)
                : KsProcgenFurnishingPlanner.Plan(prototypeManager, protectedRegion, contentPartition, request.Seed,
                inspectedWalls: inspectedWalls, maxCandidateProbes: Math.Min(4_096, budgets.MaximumFurnishingProbes - furnishingProbes),
                maxRoomCells: budgets.MaximumContentRoomCells,
                maxRelationPathExpandedCells: Math.Min(65_536, RemainingGraph()), meterCleanPaths: true);
            furnishingProbes += proposal.CandidateProbes;
            Record(KsProcgenOwnedPlanStage.Furnishing, proposal.Status is KsProcgenFurnishingStatus.Proposed or KsProcgenFurnishingStatus.Sparse,
                proposal.Issue?.Code, proposal.RelationPathExpandedCells + proposal.CleanPathExpandedCells,
                probes: proposal.CandidateProbes, regionId: region.Id);
            if (proposal.Status is not (KsProcgenFurnishingStatus.Proposed or KsProcgenFurnishingStatus.Sparse))
                return Fail(proposal.Status == KsProcgenFurnishingStatus.BudgetExceeded ? KsProcgenOwnedPlanStatus.BudgetExceeded :
                    proposal.Status == KsProcgenFurnishingStatus.InvalidInput ? KsProcgenOwnedPlanStatus.InvalidInput :
                    KsProcgenOwnedPlanStatus.Rejected, proposal.Issue!.Code);
            if (proposal.Status == KsProcgenFurnishingStatus.Sparse)
            {
                if (!request.FallbackPolicy.AllowSparseFurnishing) return Deny(KsProcgenOwnedPlanStage.Furnishing, "SparseFurnishingDisallowed", region.Id);
                fallbacks.Add(new("SparseFurnishing", region.Id));
            }
            furnishings.Add(new(region.Id, proposal));
        }
        var lighting = KsProcgenGroupLightingPlanner.Plan(prototypeManager, themes, furnishings, request.Seed,
            maximumFixtures: budgets.MaximumLightingFixtures, maximumExpandedCells: RemainingGraph(),
            maximumRoomCells: budgets.MaximumContentRoomCells);
        fixtureCount = lighting.FixtureCount;
        Record(KsProcgenOwnedPlanStage.Lighting, lighting.Status is not (KsProcgenLightingPlanStatus.InvalidInput or KsProcgenLightingPlanStatus.BudgetExceeded),
            lighting.Issue?.Code, lighting.ExpandedCells);
        if (lighting.Status is KsProcgenLightingPlanStatus.InvalidInput or KsProcgenLightingPlanStatus.BudgetExceeded)
            return Fail(lighting.Status == KsProcgenLightingPlanStatus.BudgetExceeded ? KsProcgenOwnedPlanStatus.BudgetExceeded :
                KsProcgenOwnedPlanStatus.InvalidInput, lighting.Issue!.Code);
        foreach (var region in lighting.Regions.Where(region => region.Proposal.Status == KsProcgenLightingPlanStatus.Sparse))
        {
            if (!request.FallbackPolicy.AllowSparseLighting) return Deny(KsProcgenOwnedPlanStage.Lighting, "SparseLightingDisallowed", region.RegionId);
            fallbacks.Add(new("SparseLighting", region.RegionId));
        }
        var access = KsProcgenGroupInteriorAnalyzer.Analyze(request, partition, themes, furnishings, lighting.Regions,
            maximumExpandedCells: RemainingGraph(), structure: structure,
            maximumLandingProbes: Math.Min(65_536, budgets.MaximumFurnishingProbes - furnishingProbes));
        furnishingProbes += access.LandingProbes;
        Record(KsProcgenOwnedPlanStage.InteriorAudit, access.Status == KsProcgenEntranceSceneStatus.Candidate, access.Issue?.Code, access.ExpandedCells,
            probes: access.LandingProbes);
        if (access.Status != KsProcgenEntranceSceneStatus.Candidate) return Fail(SceneStatus(access.Status), access.Issue!.Code);
        KsProcgenElectricalPlan? electrical = null;
        if (electricalContext != null)
        {
            if (!KsProcgenRequestSnapshot.TryCapture(request, out var electricalSnapshot, out issue))
                return Fail(KsProcgenOwnedPlanStatus.InvalidInput, issue!.Code);
            var wired = electricalContext.TryPlan(electricalSnapshot!, shape!, themes, structure, furnishings, lighting, RemainingGraph(),
                out electrical, out issue, out var electricalWork);
            Record(KsProcgenOwnedPlanStage.ElectricalRouting, wired, issue?.Code, electricalWork);
            if (!wired) return Fail(issue!.Code.EndsWith("Budget", StringComparison.Ordinal) ? KsProcgenOwnedPlanStatus.BudgetExceeded :
                KsProcgenOwnedPlanStatus.Rejected, issue.Code);
        }
        KsProcgenGasBindingResult? gas = null;
        if (gasContext != null)
        {
            if (!KsProcgenRequestSnapshot.TryCapture(request, out var snapshot, out issue))
                return Fail(KsProcgenOwnedPlanStatus.InvalidInput, issue!.Code);
            gas = gasContext.Plan(snapshot!, content, partition, themes, structure, furnishings, lighting, RemainingGraph(),
                additionalPlacements: electrical?.Cables.Select(cable => new KsProcgenGasPlacement(cable.PrototypeId, cable.Cell, 0,
                    KsProcgenPlacementLayer.Floor)).ToArray());
            Record(KsProcgenOwnedPlanStage.GasBinding, gas.Status == KsProcgenGasBindingStatus.Bound, gas.Issue?.Code, gas.Work);
            if (gas.Status != KsProcgenGasBindingStatus.Bound)
                return Fail(gas.Status == KsProcgenGasBindingStatus.BudgetExceeded ? KsProcgenOwnedPlanStatus.BudgetExceeded :
                    gas.Status == KsProcgenGasBindingStatus.InvalidInput ? KsProcgenOwnedPlanStatus.InvalidInput :
                    KsProcgenOwnedPlanStatus.Rejected, gas.Issue!.Code, details: gas.Issue.Message);
        }
        var result = new KsProcgenOwnedInteriorResult
        {
            Status = KsProcgenOwnedPlanStatus.Candidate, Floor = floor, Partition = partition, Themes = themes, Structure = structure,
            Furnishings = KsProcgenReadOnly.Copy(furnishings), Lighting = lighting, Access = access, SizeMix = sizeMix, Gas = gas, Electrical = electrical,
            Stages = KsProcgenReadOnly.Copy(stages), Fallbacks = KsProcgenReadOnly.Copy(fallbacks),
            ExpandedCells = expanded, FurnishingProbes = furnishingProbes,
            OrderProbes = orderProbes, PartitionMerges = partitionMerges, PartitionSeamProbes = partitionSeamProbes,
            FixtureSelections = fixtureCount,
            InfrastructureHomeCount = infrastructureHomeCount, InfrastructureHomeOrdinal = infrastructureHomeOrdinal,
            UtilityRoomChoices = utilityChoices,
            SemanticHash = KsProcgenOwnedInteriorHash.Compute(request, themeId, floor, partition, themes, structure, furnishings, lighting, access, sizeMix, fallbacks,
                gas: gas, electrical: electrical),
        };
        return result;
    }
}
