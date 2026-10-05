using System.Linq;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Procedural;

public enum KsProcgenGeometryPipelineStatus : byte
{
    GeometryPlanned,
    InvalidInput,
    NoGeometricCover,
    NoPreliminaryRoute,
    BudgetExceeded,
    ThemeRejected,
    ContentUnmet,
    WindowTargetUnmet,
    FallbackDisallowed,
}

public sealed record KsProcgenFurnishedRegion(string RegionId, KsProcgenFurnishingResult Proposal);
public sealed record KsProcgenLitRegion(string RegionId, KsProcgenLightingPlanResult Proposal);

/// <summary>
/// A planning result. GeometryPlanned does not mean a map was spawned or is playable/spaceproof.
/// </summary>
public sealed class KsProcgenGeometryPipelineResult
{
    public KsProcgenGeometryPipelineStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public KsProcgenPackingResult? Packing { get; init; }
    public KsProcgenPortNetworkResult? PortNetwork { get; init; }
    public bool HasRoomsWithoutDeclaredPorts => PortNetwork?.RoomsWithoutDeclaredPorts.Count > 0;
    public KsProcgenPureFillResult? PureFill { get; init; }
    public KsProcgenPartitionResult? Partition { get; init; }
    public KsProcgenThemeAssignmentResult? Themes { get; init; }
    public KsProcgenMaterialResult? Materials { get; init; }
    public KsProcgenHullBoundaryResult? HullBoundary { get; init; }
    public KsProcgenWindowPlanResult? Windows { get; init; }
    public IReadOnlyList<KsProcgenFurnishedRegion> Furnishings { get; init; } = [];
    public IReadOnlyList<KsProcgenLitRegion> Lighting { get; init; } = [];
    public IReadOnlyList<KsProcgenSizeMixOutcome> FinalSizeMixOutcomes { get; init; } = [];
    public bool HasSoftSizeMixShortfall => FinalSizeMixOutcomes.Any(item =>
        item.AchievedCount < item.RequestedCount);
    public bool HasSoftSizeMixDeviation => FinalSizeMixOutcomes.Any(item =>
        item.AchievedCount != item.RequestedCount);
    public ulong SemanticHash { get; init; }
    public ulong? ConstantContractHash { get; init; }
    public bool HasUnverifiedConstantRegions { get; init; }
    public bool HasUnverifiedHardWindowGoal { get; init; }
    public bool HasUnplacedRequiredEntityPacks { get; init; }
    public KsProcgenPlanningReport Summarize(KsProcgenRequest request, int maxEntries = 4_096) =>
        KsProcgenPlanningReportBuilder.Build(request, this, maxEntries);
}

/// <summary>
/// Composes the side-effect-free stages for pure and hybrid layouts. Engine inspection,
/// instancing, airtight closure, operational lighting/furniture, and publication remain separate gates.
/// </summary>
public static class KsProcgenGeometryPipeline
{
    public static KsProcgenGeometryPipelineResult Plan(
        IPrototypeManager prototypeManager,
        KsProcgenRequest request,
        IReadOnlyList<KsProcgenLayoutFamily> families,
        string themeId,
        KsProcgenPackingBudgets? packingBudgets = null,
        IReadOnlySet<Vector2i>? inspectedExistingPassages = null,
        int maxPureFillCells = 65_536,
        int maxPartitionMerges = 256,
        int maxFurnishingProbes = 4_096,
        int maxLightingFixtures = 4_096,
        IReadOnlyList<KsProcgenWindowBoundaryCell>? inspectedWindowBoundary = null)
    {
        if (prototypeManager == null || request == null || families == null || string.IsNullOrWhiteSpace(themeId) ||
            maxFurnishingProbes <= 0 || maxFurnishingProbes > 4_096 ||
            maxLightingFixtures <= 0 || maxLightingFixtures > 4_096)
            return new KsProcgenGeometryPipelineResult
            {
                Status = KsProcgenGeometryPipelineStatus.InvalidInput,
                Issue = new KsProcgenIssue("InvalidPipelineInput", "A request, library, manager, and theme are required."),
            };

        var packing = KsProcgenPackingPlanner.Plan(request, families, packingBudgets, inspectedExistingPassages);
        if (packing.Status != KsProcgenPackingStatus.GeometryReady)
            return new KsProcgenGeometryPipelineResult
            {
                Status = packing.Status switch
                {
                    KsProcgenPackingStatus.NoGeometricCover => KsProcgenGeometryPipelineStatus.NoGeometricCover,
                    KsProcgenPackingStatus.NoPreliminaryRoute => KsProcgenGeometryPipelineStatus.NoPreliminaryRoute,
                    KsProcgenPackingStatus.BudgetExceeded => KsProcgenGeometryPipelineStatus.BudgetExceeded,
                    _ => KsProcgenGeometryPipelineStatus.InvalidInput,
                },
                Issue = packing.Issue,
                Packing = packing,
            };

        if (!KsProcgenGeometry.TryNormalize(request, out var shape, out var issue))
            return new KsProcgenGeometryPipelineResult
            {
                Status = KsProcgenGeometryPipelineStatus.InvalidInput,
                Issue = issue,
                Packing = packing,
            };

        var fill = KsProcgenPureFillPlanner.Plan(shape!, packing, request.Seed,
            maxProceduralCells: maxPureFillCells, sizeMix: request.SizeMix);
        if (fill.Status != KsProcgenPureFillStatus.Proposed)
            return new KsProcgenGeometryPipelineResult
            {
                Status = fill.Status == KsProcgenPureFillStatus.BudgetExceeded
                    ? KsProcgenGeometryPipelineStatus.BudgetExceeded : KsProcgenGeometryPipelineStatus.InvalidInput,
                Issue = fill.Issue,
                Packing = packing,
                PureFill = fill,
            };

        var reserved = packing.ResidualRouting?.ReservedPassageCells ?? new HashSet<Vector2i>();
        var partition = KsProcgenPartitionPlanner.Plan(fill, reserved, maxPartitionMerges);
        if (partition.Status == KsProcgenPartitionStatus.InvalidInput)
            return new KsProcgenGeometryPipelineResult
            {
                Status = KsProcgenGeometryPipelineStatus.InvalidInput,
                Issue = partition.Issue,
                Packing = packing,
                PureFill = fill,
                Partition = partition,
            };

        var themes = KsProcgenThemeAssignmentPlanner.Plan(prototypeManager, themeId,
            request.Seed, fill, partition);
        if (themes.Status != KsProcgenThemeAssignmentStatus.Selected)
            return new KsProcgenGeometryPipelineResult
            {
                Status = themes.Status == KsProcgenThemeAssignmentStatus.ThemeRejected
                    ? KsProcgenGeometryPipelineStatus.ThemeRejected : KsProcgenGeometryPipelineStatus.InvalidInput,
                Issue = themes.Issue,
                Packing = packing,
                PureFill = fill,
                Partition = partition,
                Themes = themes,
            };

        var materials = KsProcgenMaterialPlanner.Plan(prototypeManager, themes, partition, request.Seed);
        if (materials.Status != KsProcgenMaterialStatus.Planned)
            return new KsProcgenGeometryPipelineResult
            {
                Status = materials.Status == KsProcgenMaterialStatus.NoCompatibleDoor
                    ? KsProcgenGeometryPipelineStatus.ThemeRejected : KsProcgenGeometryPipelineStatus.InvalidInput,
                Issue = materials.Issue,
                Packing = packing,
                PureFill = fill,
                Partition = partition,
                Themes = themes,
                Materials = materials,
            };
        if (!request.FallbackPolicy.AllowMergedPartition &&
            partition.Status is KsProcgenPartitionStatus.MergedFallback or KsProcgenPartitionStatus.OpenFallback)
            return new KsProcgenGeometryPipelineResult
            {
                Status = KsProcgenGeometryPipelineStatus.FallbackDisallowed,
                Issue = new KsProcgenIssue("PartitionFallbackDisallowed",
                    "The proposed partition requires merging or open-floor fallback."),
                Packing = packing,
                PureFill = fill,
                Partition = partition,
            };

        var portNetwork = shape!.TargetCells.Count == 0 ? null : KsProcgenPortNetworkAnalyzer.AnalyzePartitioned(
            shape, packing, partition, inspectedExistingPassages ?? new HashSet<Vector2i>(), request.RootCells);
        if (portNetwork?.Status is KsProcgenPortNetworkStatus.InvalidInput or
            KsProcgenPortNetworkStatus.BudgetExceeded ||
            portNetwork?.Status == KsProcgenPortNetworkStatus.Disconnected &&
            request.ConnectivityPolicy == KsProcgenConnectivityPolicy.SingleNetwork)
            return new KsProcgenGeometryPipelineResult
            {
                Status = portNetwork.Status switch
                {
                    KsProcgenPortNetworkStatus.BudgetExceeded => KsProcgenGeometryPipelineStatus.BudgetExceeded,
                    KsProcgenPortNetworkStatus.Disconnected => KsProcgenGeometryPipelineStatus.NoPreliminaryRoute,
                    _ => KsProcgenGeometryPipelineStatus.InvalidInput,
                },
                Issue = portNetwork.Issue,
                Packing = packing,
                PortNetwork = portNetwork,
            };

        // Prefab floors need engine inspection before a complete boundary inventory is possible.
        var hullBoundary = packing.Placements.Count == 0
            ? KsProcgenHullBoundaryPlanner.Classify(shape!, request.GeometryMode,
                partition.FloorCells, partition.WallCells)
            : null;
        if (hullBoundary?.Status == KsProcgenHullBoundaryStatus.InvalidInput)
            return new KsProcgenGeometryPipelineResult
            {
                Status = KsProcgenGeometryPipelineStatus.InvalidInput,
                Issue = hullBoundary.Issue,
                Packing = packing,
                PureFill = fill,
                Partition = partition,
                Themes = themes,
                Materials = materials,
                HullBoundary = hullBoundary,
            };

        var windows = inspectedWindowBoundary == null ? null : KsProcgenWindowPlanner.PlanForShape(
            shape!, inspectedWindowBoundary, request.Seed, request.WindowGoal);
        if (windows?.Status is KsProcgenWindowPlanStatus.InvalidInput or
            KsProcgenWindowPlanStatus.HardTargetUnmet)
            return new KsProcgenGeometryPipelineResult
            {
                Status = windows.Status == KsProcgenWindowPlanStatus.HardTargetUnmet
                    ? KsProcgenGeometryPipelineStatus.WindowTargetUnmet
                    : KsProcgenGeometryPipelineStatus.InvalidInput,
                Issue = windows.Issue,
                Packing = packing,
                PortNetwork = portNetwork,
                PureFill = fill,
                Partition = partition,
                Themes = themes,
                Materials = materials,
                HullBoundary = hullBoundary,
                Windows = windows,
            };

        var furnishings = new List<KsProcgenFurnishedRegion>();
        var finalSizeMix = KsProcgenSizeMixAnalyzer.Analyze(request.SizeMix, themes.Regions);
        if (!request.FallbackPolicy.AllowRoomSizeShortfall &&
            finalSizeMix.Any(item => item.AchievedCount < item.RequestedCount))
            return new KsProcgenGeometryPipelineResult
            {
                Status = KsProcgenGeometryPipelineStatus.FallbackDisallowed,
                Issue = new KsProcgenIssue("RoomSizeShortfallDisallowed",
                    "The final partition cannot meet every requested room-size count."),
                Packing = packing,
                PortNetwork = portNetwork,
                PureFill = fill,
                Partition = partition,
                Themes = themes,
                Materials = materials,
                HullBoundary = hullBoundary,
                Windows = windows,
                FinalSizeMixOutcomes = finalSizeMix,
            };
        var usedFurnishingProbes = 0;
        foreach (var region in themes.Regions)
        {
            if (usedFurnishingProbes >= maxFurnishingProbes &&
                region.Theme.DominantEntityPackId != null)
                return new KsProcgenGeometryPipelineResult
                {
                    Status = KsProcgenGeometryPipelineStatus.BudgetExceeded,
                    Issue = new KsProcgenIssue("FurnishingCandidateBudget",
                        "The request-wide furnishing proposal budget is exhausted."),
                    Packing = packing,
                    PureFill = fill,
                    Partition = partition,
                    Themes = themes,
                    Materials = materials,
                    HullBoundary = hullBoundary,
                    Windows = windows,
                    Furnishings = furnishings,
                    HasUnplacedRequiredEntityPacks = themes.Regions.Any(item =>
                        item.UnplacedRequiredPacks.Count > 0),
                };

            var remaining = Math.Max(1, maxFurnishingProbes - usedFurnishingProbes);
            var proposed = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                region, partition, request.Seed, maxCandidateProbes: remaining);
            furnishings.Add(new KsProcgenFurnishedRegion(region.Id, proposed));
            usedFurnishingProbes += proposed.CandidateProbes;
            if (proposed.Status == KsProcgenFurnishingStatus.Sparse &&
                !request.FallbackPolicy.AllowSparseFurnishing)
                return new KsProcgenGeometryPipelineResult
                {
                    Status = KsProcgenGeometryPipelineStatus.FallbackDisallowed,
                    Issue = new KsProcgenIssue("SparseFurnishingDisallowed",
                        $"Room {region.Id} needs an optional furnishing omission."),
                    Packing = packing,
                    PortNetwork = portNetwork,
                    PureFill = fill,
                    Partition = partition,
                    Themes = themes,
                    Materials = materials,
                    HullBoundary = hullBoundary,
                    Windows = windows,
                    Furnishings = furnishings,
                    FinalSizeMixOutcomes = finalSizeMix,
                };
            if (proposed.Status is KsProcgenFurnishingStatus.Proposed or KsProcgenFurnishingStatus.Sparse)
                continue;
            return new KsProcgenGeometryPipelineResult
            {
                Status = proposed.Status switch
                {
                    KsProcgenFurnishingStatus.MandatoryUnmet => KsProcgenGeometryPipelineStatus.ContentUnmet,
                    KsProcgenFurnishingStatus.BudgetExceeded => KsProcgenGeometryPipelineStatus.BudgetExceeded,
                    _ => KsProcgenGeometryPipelineStatus.InvalidInput,
                },
                Issue = proposed.Issue,
                Packing = packing,
                PureFill = fill,
                Partition = partition,
                Themes = themes,
                Materials = materials,
                HullBoundary = hullBoundary,
                Windows = windows,
                Furnishings = furnishings,
                HasUnplacedRequiredEntityPacks = themes.Regions.Any(item =>
                    item.UnplacedRequiredPacks.Count > 0),
            };
        }

        var lighting = new List<KsProcgenLitRegion>();
        var usedLightingFixtures = 0;
        foreach (var region in themes.Regions)
        {
            if (usedLightingFixtures >= maxLightingFixtures && region.Theme.LightingPackId != null)
                return new KsProcgenGeometryPipelineResult
                {
                    Status = KsProcgenGeometryPipelineStatus.BudgetExceeded,
                    Issue = new KsProcgenIssue("LightingProposalBudget",
                        "The request-wide lighting fixture proposal budget is exhausted."),
                    Packing = packing,
                    PureFill = fill,
                    Partition = partition,
                    Themes = themes,
                    Materials = materials,
                    HullBoundary = hullBoundary,
                    Windows = windows,
                    Furnishings = furnishings,
                    Lighting = lighting,
                    HasUnplacedRequiredEntityPacks = themes.Regions.Any(item =>
                        item.UnplacedRequiredPacks.Count > 0),
                };

            var furnished = furnishings.Single(item => item.RegionId == region.Id);
            var remaining = Math.Max(1, maxLightingFixtures - usedLightingFixtures);
            var proposed = KsProcgenLightingPlanner.Plan(prototypeManager, region,
                furnished.Proposal, request.Seed, maxFixtures: remaining);
            lighting.Add(new KsProcgenLitRegion(region.Id, proposed));
            usedLightingFixtures += proposed.Lights.Count;
            if (proposed.Status == KsProcgenLightingPlanStatus.Sparse &&
                !request.FallbackPolicy.AllowSparseLighting)
                return new KsProcgenGeometryPipelineResult
                {
                    Status = KsProcgenGeometryPipelineStatus.FallbackDisallowed,
                    Issue = new KsProcgenIssue("SparseLightingDisallowed",
                        $"Region {region.Id} needs sparse provisional lighting."),
                    Packing = packing,
                    PortNetwork = portNetwork,
                    PureFill = fill,
                    Partition = partition,
                    Themes = themes,
                    Materials = materials,
                    HullBoundary = hullBoundary,
                    Windows = windows,
                    Furnishings = furnishings,
                    Lighting = lighting,
                    FinalSizeMixOutcomes = finalSizeMix,
                };
            if (proposed.Status is KsProcgenLightingPlanStatus.Proposed or
                KsProcgenLightingPlanStatus.Sparse or KsProcgenLightingPlanStatus.NotRequested)
                continue;
            return new KsProcgenGeometryPipelineResult
            {
                Status = proposed.Status == KsProcgenLightingPlanStatus.BudgetExceeded
                    ? KsProcgenGeometryPipelineStatus.BudgetExceeded
                    : KsProcgenGeometryPipelineStatus.InvalidInput,
                Issue = proposed.Issue,
                Packing = packing,
                PureFill = fill,
                Partition = partition,
                Themes = themes,
                Materials = materials,
                HullBoundary = hullBoundary,
                Windows = windows,
                Furnishings = furnishings,
                Lighting = lighting,
                HasUnplacedRequiredEntityPacks = themes.Regions.Any(item =>
                    item.UnplacedRequiredPacks.Count > 0),
            };
        }

        return new KsProcgenGeometryPipelineResult
        {
            Status = KsProcgenGeometryPipelineStatus.GeometryPlanned,
            Issue = partition.Issue,
            Packing = packing,
            PortNetwork = portNetwork,
            PureFill = fill,
            Partition = partition,
            Themes = themes,
            Materials = materials,
            HullBoundary = hullBoundary,
            Windows = windows,
            Furnishings = furnishings,
            Lighting = lighting,
            FinalSizeMixOutcomes = finalSizeMix,
            ConstantContractHash = shape!.ConstantContractHash,
            HasUnverifiedConstantRegions = shape.ConstantRegions.Count > 0,
            HasUnverifiedHardWindowGoal = request.WindowGoal.HardFraction ||
                request.WindowGoal.MinimumCount > 0 || request.WindowGoal.MaximumCount.HasValue,
            HasUnplacedRequiredEntityPacks = themes.Regions.Any(region => region.UnplacedRequiredPacks.Count > 0),
            SemanticHash = HashPlan(request, shape!, themeId, packing, fill, partition, themes, materials,
                hullBoundary, portNetwork, inspectedExistingPassages, inspectedWindowBoundary, windows,
                furnishings, lighting, finalSizeMix),
        };
    }

    private static ulong HashPlan(
        KsProcgenRequest request,
        KsProcgenNormalizedShape shape,
        string themeId,
        KsProcgenPackingResult packing,
        KsProcgenPureFillResult fill,
        KsProcgenPartitionResult partition,
        KsProcgenThemeAssignmentResult themes,
        KsProcgenMaterialResult materials,
        KsProcgenHullBoundaryResult? hullBoundary,
        KsProcgenPortNetworkResult? portNetwork,
        IReadOnlySet<Vector2i>? inspectedExistingPassages,
        IReadOnlyList<KsProcgenWindowBoundaryCell>? inspectedWindowBoundary,
        KsProcgenWindowPlanResult? windows,
        IReadOnlyList<KsProcgenFurnishedRegion> furnishings,
        IReadOnlyList<KsProcgenLitRegion> lighting,
        IReadOnlyList<KsProcgenSizeMixOutcome> finalSizeMix)
    {
        var hash = KsProcgenStableHash.Create();
        hash.AddString("ks-procgen-geometry-plan-v20");
        hash.AddString(request.RequestId);
        hash.AddInt(request.Seed);
        hash.AddInt((int) request.Mode);
        hash.AddInt((int) request.GeometryMode);
        hash.AddInt((int) request.ConnectivityPolicy);
        hash.AddInt(request.FallbackPolicy.AllowMergedPartition ? 1 : 0);
        hash.AddInt(request.FallbackPolicy.AllowRoomSizeShortfall ? 1 : 0);
        hash.AddInt(request.FallbackPolicy.AllowSparseFurnishing ? 1 : 0);
        hash.AddInt(request.FallbackPolicy.AllowSparseLighting ? 1 : 0);
        hash.AddInt(request.RootCells.Count);
        foreach (var root in request.RootCells.OrderBy(cell => cell.Y).ThenBy(cell => cell.X))
        {
            hash.AddInt(root.X);
            hash.AddInt(root.Y);
        }
        hash.AddInt(inspectedExistingPassages?.Count ?? 0);
        if (inspectedExistingPassages != null)
        foreach (var cell in inspectedExistingPassages.OrderBy(cell => cell.Y).ThenBy(cell => cell.X))
        {
            hash.AddInt(cell.X);
            hash.AddInt(cell.Y);
        }
        hash.AddInt(portNetwork == null ? 0 : 1);
        if (portNetwork != null)
        {
            hash.AddInt((int) portNetwork.Status);
            hash.AddInt(portNetwork.Groups.Count);
            foreach (var group in portNetwork.Groups)
            {
                hash.AddInt(group.RoomIds.Count);
                foreach (var roomId in group.RoomIds)
                    hash.AddString(roomId);
                hash.AddInt(group.PassageCells);
                hash.AddInt(group.RootCells);
            }
            hash.AddInt(portNetwork.RoomsWithoutDeclaredPorts.Count);
            foreach (var roomId in portNetwork.RoomsWithoutDeclaredPorts)
                hash.AddString(roomId);
        }
        hash.AddString(themeId);
        hash.AddInt(BitConverter.SingleToInt32Bits(request.WindowGoal.ExteriorWindowFraction));
        hash.AddInt(request.WindowGoal.HardFraction ? 1 : 0);
        hash.AddInt(request.WindowGoal.ToleranceCells);
        hash.AddInt(request.WindowGoal.MinimumCount);
        hash.AddInt(request.WindowGoal.MaximumCount.HasValue ? 1 : 0);
        if (request.WindowGoal.MaximumCount.HasValue)
            hash.AddInt(request.WindowGoal.MaximumCount.Value);
        hash.AddInt(request.SizeMix.Count);
        foreach (var goal in request.SizeMix)
        {
            hash.AddString(goal.Id);
            hash.AddInt(goal.MinCells);
            hash.AddInt(goal.MaxCells);
            hash.AddInt(goal.TargetCount);
        }
        hash.AddInt(shape.ConstantRegions.Count);
        foreach (var constant in shape.ConstantRegions)
        {
            hash.AddString(constant.Id);
            hash.AddString(constant.SourceId);
            hash.AddString(constant.ContentFingerprint);
            hash.AddInt(constant.Origin.X);
            hash.AddInt(constant.Origin.Y);
            hash.AddInt(constant.QuarterTurns);
            hash.AddInt(constant.Cells.Count);
            foreach (var cell in constant.Cells)
            {
                hash.AddInt(cell.X);
                hash.AddInt(cell.Y);
            }
            hash.AddInt(constant.Ports.Count);
            foreach (var port in constant.Ports)
            {
                hash.AddString(port.Id);
                hash.AddInt(port.Threshold.X);
                hash.AddInt(port.Threshold.Y);
                hash.AddInt(port.OutwardNormal.X);
                hash.AddInt(port.OutwardNormal.Y);
            }
        }
        hash.AddInt(packing.Placements.Count);
        foreach (var placement in packing.Placements.OrderBy(placement => placement.FamilyId, StringComparer.Ordinal)
                     .ThenBy(placement => placement.OptionId, StringComparer.Ordinal)
                     .ThenBy(placement => placement.Origin.Y).ThenBy(placement => placement.Origin.X)
                     .ThenBy(placement => placement.QuarterTurns))
        {
            hash.AddString(placement.FamilyId);
            hash.AddString(placement.OptionId);
            hash.AddInt(placement.Origin.X);
            hash.AddInt(placement.Origin.Y);
            hash.AddInt(placement.QuarterTurns);
        }

        hash.AddInt(packing.CellClaims.Count);
        foreach (var (cell, claim) in packing.CellClaims.OrderBy(entry => entry.Cell.Y)
                     .ThenBy(entry => entry.Cell.X))
        {
            hash.AddInt(cell.X);
            hash.AddInt(cell.Y);
            hash.AddString(claim.OwnerId);
            hash.AddInt((int) claim.Disposition);
        }
        hash.AddInt(packing.ResidualRouting?.ReservedPassageCells.Count ?? 0);
        hash.AddInt(packing.ResidualRouting?.DirectPortPairs.Count ?? 0);
        if (packing.ResidualRouting != null)
        {
            foreach (var cell in packing.ResidualRouting.ReservedPassageCells
                         .OrderBy(cell => cell.Y).ThenBy(cell => cell.X))
            {
                hash.AddInt(cell.X);
                hash.AddInt(cell.Y);
            }
            foreach (var pair in packing.ResidualRouting.DirectPortPairs
                         .OrderBy(pair => pair.First, StringComparer.Ordinal)
                         .ThenBy(pair => pair.Second, StringComparer.Ordinal))
            {
                hash.AddString(pair.First);
                hash.AddString(pair.Second);
            }
        }

        hash.AddInt(fill.Zones.Count);
        foreach (var zone in fill.Zones.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            hash.AddString(zone.Id);
            hash.AddInt((int) zone.Kind);
            hash.AddString(zone.SizeGoalId ?? "");
            hash.AddInt(zone.Cells.Count);
            foreach (var cell in zone.Cells)
            {
                hash.AddInt(cell.X);
                hash.AddInt(cell.Y);
            }
        }
        hash.AddInt(fill.SizeMixOutcomes.Count);
        foreach (var outcome in fill.SizeMixOutcomes.OrderBy(item => item.GoalId, StringComparer.Ordinal))
        {
            hash.AddString(outcome.GoalId);
            hash.AddInt(outcome.RequestedCount);
            hash.AddInt(outcome.AchievedCount);
        }

        hash.AddInt(partition.WallCells.Count);
        foreach (var wall in partition.WallCells.OrderBy(cell => cell.Y).ThenBy(cell => cell.X))
        {
            hash.AddInt(wall.X);
            hash.AddInt(wall.Y);
        }
        hash.AddInt(partition.DoorOpenings.Count);
        foreach (var door in partition.DoorOpenings.OrderBy(door => door.Threshold.Y)
                     .ThenBy(door => door.Threshold.X))
        {
            hash.AddString(door.FirstZoneId);
            hash.AddString(door.SecondZoneId);
            hash.AddInt(door.Threshold.X);
            hash.AddInt(door.Threshold.Y);
            hash.AddInt(door.InsideApproach.X);
            hash.AddInt(door.InsideApproach.Y);
            hash.AddInt(door.OutsideApproach.X);
            hash.AddInt(door.OutsideApproach.Y);
        }

        hash.AddInt(themes.Regions.Count);
        foreach (var region in themes.Regions.OrderBy(region => region.Id, StringComparer.Ordinal))
        {
            hash.AddString(region.Id);
            hash.AddInt((int) region.Kind);
            hash.AddString(region.Theme.TilePackId);
            hash.AddString(region.Theme.TilePaletteId);
            hash.AddString(region.Theme.WallPackId);
            hash.AddString(region.Theme.WallFamilyId);
            hash.AddString(region.Theme.LightingPackId ?? "");
            hash.AddString(region.Theme.LightFixtureId ?? "");
            hash.AddString(region.Theme.DominantEntityPackId ?? "");
            hash.AddInt(BitConverter.SingleToInt32Bits(region.Theme.FurnishingDensity));
            hash.AddInt(region.Theme.SupportingEntityPackIds.Count);
            foreach (var support in region.Theme.SupportingEntityPackIds.OrderBy(id => id, StringComparer.Ordinal))
                hash.AddString(support);
            hash.AddInt(region.UnplacedRequiredPacks.Count);
            foreach (var minimum in region.UnplacedRequiredPacks.OrderBy(pack => pack.PackId, StringComparer.Ordinal))
            {
                hash.AddString(minimum.PackId);
                hash.AddInt(minimum.MinimumCount);
            }
        }

        hash.AddInt(finalSizeMix.Count);
        foreach (var outcome in finalSizeMix.OrderBy(item => item.GoalId, StringComparer.Ordinal))
        {
            hash.AddString(outcome.GoalId);
            hash.AddInt(outcome.RequestedCount);
            hash.AddInt(outcome.AchievedCount);
        }

        hash.AddInt(materials.Tiles.Count);
        foreach (var tile in materials.Tiles.OrderBy(tile => tile.Cell.Y).ThenBy(tile => tile.Cell.X))
        {
            hash.AddInt(tile.Cell.X);
            hash.AddInt(tile.Cell.Y);
            hash.AddString(tile.TileId);
            hash.AddString(tile.RegionId);
            hash.AddInt(tile.Accent ? 1 : 0);
        }
        hash.AddInt(materials.InteriorWalls.Count);
        foreach (var wall in materials.InteriorWalls.OrderBy(wall => wall.Cell.Y).ThenBy(wall => wall.Cell.X))
        {
            hash.AddInt(wall.Cell.X);
            hash.AddInt(wall.Cell.Y);
            hash.AddString(wall.EntityId);
            hash.AddString(wall.RegionId);
        }
        hash.AddInt(materials.InteriorDoors.Count);
        foreach (var door in materials.InteriorDoors.OrderBy(door => door.Cell.Y).ThenBy(door => door.Cell.X))
        {
            hash.AddInt(door.Cell.X);
            hash.AddInt(door.Cell.Y);
            hash.AddString(door.EntityId);
            hash.AddString(door.RegionId);
        }

        hash.AddInt(hullBoundary == null ? 0 : 1);
        if (hullBoundary != null)
        {
            hash.AddInt((int) hullBoundary.Status);
            hash.AddInt(hullBoundary.Edges.Count);
            foreach (var edge in hullBoundary.Edges)
            {
                hash.AddInt(edge.InteriorCell.X);
                hash.AddInt(edge.InteriorCell.Y);
                hash.AddInt(edge.BoundaryCell.X);
                hash.AddInt(edge.BoundaryCell.Y);
                hash.AddInt((int) edge.Kind);
            }
        }

        hash.AddInt(inspectedWindowBoundary == null ? 0 : 1);
        if (inspectedWindowBoundary != null)
        {
            hash.AddInt(inspectedWindowBoundary.Count);
            foreach (var cell in inspectedWindowBoundary.OrderBy(item => item.Cell.Y)
                         .ThenBy(item => item.Cell.X))
            {
                hash.AddInt(cell.Cell.X);
                hash.AddInt(cell.Cell.Y);
                hash.AddInt(cell.FacesInterior ? 1 : 0);
                hash.AddInt(cell.FacesExterior ? 1 : 0);
                hash.AddInt(cell.SupportsAirtightWindow ? 1 : 0);
                hash.AddInt(cell.Fixed ? 1 : 0);
                hash.AddInt(cell.IsWindow ? 1 : 0);
                hash.AddInt(cell.IsCorner ? 1 : 0);
                hash.AddInt(cell.IsDoorway ? 1 : 0);
                hash.AddInt(cell.RequiredStructure ? 1 : 0);
                hash.AddInt(cell.WindowsDisabled ? 1 : 0);
            }
        }
        hash.AddInt(windows == null ? 0 : 1);
        if (windows != null)
        {
            hash.AddInt((int) windows.Status);
            hash.AddInt(windows.EligibleCells);
            hash.AddInt(windows.RequestedWindowCells);
            hash.AddInt(windows.AchievedWindowCells);
            hash.AddInt(windows.ChosenWindowCells.Count);
            foreach (var cell in windows.ChosenWindowCells)
            {
                hash.AddInt(cell.X);
                hash.AddInt(cell.Y);
            }
        }

        hash.AddInt(furnishings.Count);
        foreach (var furnished in furnishings.OrderBy(item => item.RegionId, StringComparer.Ordinal))
        {
            hash.AddString(furnished.RegionId);
            hash.AddInt((int) furnished.Proposal.Status);
            hash.AddInt(furnished.Proposal.ProtectedPassageCells.Count);
            foreach (var cell in furnished.Proposal.ProtectedPassageCells
                         .OrderBy(cell => cell.Y).ThenBy(cell => cell.X))
            {
                hash.AddInt(cell.X);
                hash.AddInt(cell.Y);
            }
            hash.AddInt(furnished.Proposal.Entities.Count);
            foreach (var entity in furnished.Proposal.Entities.OrderBy(entity => entity.PackId, StringComparer.Ordinal)
                         .ThenBy(entity => entity.EntryId, StringComparer.Ordinal)
                         .ThenBy(entity => entity.Cell.Y).ThenBy(entity => entity.Cell.X))
            {
                hash.AddString(entity.PackId);
                hash.AddString(entity.EntryId);
                hash.AddString(entity.CoreId ?? string.Empty);
                hash.AddString(entity.AssemblyId ?? string.Empty);
                hash.AddString(entity.VariantId ?? string.Empty);
                hash.AddString(entity.EntityId);
                hash.AddInt(entity.Cell.X);
                hash.AddInt(entity.Cell.Y);
                hash.AddInt(entity.OccupiedCells.Count);
                foreach (var occupiedCell in entity.OccupiedCells)
                {
                    hash.AddInt(occupiedCell.X);
                    hash.AddInt(occupiedCell.Y);
                }
                hash.AddInt(entity.QuarterTurns);
                hash.AddInt(entity.ClusterIndex);
                hash.AddInt(entity.DeclaredApproachLanding.HasValue ? 1 : 0);
                if (entity.DeclaredApproachLanding.HasValue)
                {
                    hash.AddInt(entity.DeclaredApproachLanding.Value.X);
                    hash.AddInt(entity.DeclaredApproachLanding.Value.Y);
                }
                hash.AddInt((int) entity.Movement);
                hash.AddInt(entity.InteractionApproach.HasValue ? 1 : 0);
                if (entity.InteractionApproach.HasValue)
                {
                    hash.AddInt(entity.InteractionApproach.Value.X);
                    hash.AddInt(entity.InteractionApproach.Value.Y);
                }
            }
            hash.AddInt(furnished.Proposal.Omissions.Count);
            hash.AddInt(furnished.Proposal.PreferenceSearchTruncated ? 1 : 0);
            hash.AddInt(furnished.Proposal.RelationPathSearchTruncated ? 1 : 0);
            hash.AddInt(furnished.Proposal.RelationWitnesses.Count);
            foreach (var witness in furnished.Proposal.RelationWitnesses
                         .OrderBy(item => item.PackId, StringComparer.Ordinal)
                         .ThenBy(item => item.CoreId, StringComparer.Ordinal)
                         .ThenBy(item => item.ClusterIndex)
                         .ThenBy(item => item.RelationId, StringComparer.Ordinal))
            {
                hash.AddString(witness.PackId);
                hash.AddString(witness.CoreId);
                hash.AddString(witness.AssemblyId);
                hash.AddString(witness.VariantId);
                hash.AddInt(witness.ClusterIndex);
                hash.AddString(witness.RelationId);
                hash.AddString(witness.SubjectId);
                hash.AddString(witness.TargetId ?? string.Empty);
                hash.AddInt((int) witness.Kind);
                hash.AddInt((int) witness.Severity);
                hash.AddInt((int) witness.State);
                hash.AddString(witness.ReasonCode ?? string.Empty);
                foreach (var distance in new[] { witness.MinimumDistance, witness.MaximumDistance, witness.PathDistance })
                {
                    hash.AddInt(distance.HasValue ? 1 : 0);
                    if (distance.HasValue)
                        hash.AddInt(distance.Value);
                }
                hash.AddInt(witness.CleanPath.Cells.Count);
                foreach (var cell in witness.CleanPath.Cells)
                {
                    hash.AddInt(cell.X);
                    hash.AddInt(cell.Y);
                }
                hash.AddInt(witness.SubjectCell.HasValue ? 1 : 0);
                if (witness.SubjectCell.HasValue)
                {
                    hash.AddInt(witness.SubjectCell.Value.X);
                    hash.AddInt(witness.SubjectCell.Value.Y);
                }
                hash.AddInt(witness.TargetCell.HasValue ? 1 : 0);
                if (witness.TargetCell.HasValue)
                {
                    hash.AddInt(witness.TargetCell.Value.X);
                    hash.AddInt(witness.TargetCell.Value.Y);
                }
                foreach (var cell in new[] { witness.FirstBackingWall, witness.SecondBackingWall, witness.InteractionApproach })
                {
                    hash.AddInt(cell.HasValue ? 1 : 0);
                    if (!cell.HasValue)
                        continue;
                    hash.AddInt(cell.Value.X);
                    hash.AddInt(cell.Value.Y);
                }
            }
            foreach (var omission in furnished.Proposal.Omissions.OrderBy(item => item.PackId, StringComparer.Ordinal))
            {
                hash.AddString(omission.PackId);
                hash.AddString(omission.Reason);
            }
        }

        hash.AddInt(lighting.Count);
        foreach (var lit in lighting.OrderBy(item => item.RegionId, StringComparer.Ordinal))
        {
            hash.AddString(lit.RegionId);
            hash.AddInt((int) lit.Proposal.Status);
            hash.AddInt(lit.Proposal.EstimatedCoveredCells);
            hash.AddInt(lit.Proposal.TotalFloorCells);
            hash.AddInt(lit.Proposal.Lights.Count);
            foreach (var light in lit.Proposal.Lights.OrderBy(item => item.Cell.Y)
                         .ThenBy(item => item.Cell.X))
            {
                hash.AddInt(light.Cell.X);
                hash.AddInt(light.Cell.Y);
                hash.AddString(light.EntityId);
                hash.AddString(light.FixtureId);
                hash.AddInt((int) light.DeclaredSupply);
            }
        }

        return hash.Value;
    }
}
