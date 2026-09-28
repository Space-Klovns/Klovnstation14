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
    public KsProcgenPureFillResult? PureFill { get; init; }
    public KsProcgenPartitionResult? Partition { get; init; }
    public KsProcgenThemeAssignmentResult? Themes { get; init; }
    public KsProcgenMaterialResult? Materials { get; init; }
    public IReadOnlyList<KsProcgenFurnishedRegion> Furnishings { get; init; } = [];
    public IReadOnlyList<KsProcgenLitRegion> Lighting { get; init; } = [];
    public ulong SemanticHash { get; init; }
    public bool HasUnplacedRequiredEntityPacks { get; init; }
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
        int maxLightingFixtures = 4_096)
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
            maxProceduralCells: maxPureFillCells);
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

        var furnishings = new List<KsProcgenFurnishedRegion>();
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
                    Furnishings = furnishings,
                    HasUnplacedRequiredEntityPacks = themes.Regions.Any(item =>
                        item.UnplacedRequiredPacks.Count > 0),
                };

            var remaining = Math.Max(1, maxFurnishingProbes - usedFurnishingProbes);
            var proposed = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                region, partition, request.Seed, maxCandidateProbes: remaining);
            furnishings.Add(new KsProcgenFurnishedRegion(region.Id, proposed));
            usedFurnishingProbes += proposed.CandidateProbes;
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
            PureFill = fill,
            Partition = partition,
            Themes = themes,
            Materials = materials,
            Furnishings = furnishings,
            Lighting = lighting,
            HasUnplacedRequiredEntityPacks = themes.Regions.Any(region => region.UnplacedRequiredPacks.Count > 0),
            SemanticHash = HashPlan(request, themeId, packing, partition, themes, materials, furnishings, lighting),
        };
    }

    private static ulong HashPlan(
        KsProcgenRequest request,
        string themeId,
        KsProcgenPackingResult packing,
        KsProcgenPartitionResult partition,
        KsProcgenThemeAssignmentResult themes,
        KsProcgenMaterialResult materials,
        IReadOnlyList<KsProcgenFurnishedRegion> furnishings,
        IReadOnlyList<KsProcgenLitRegion> lighting)
    {
        var hash = KsProcgenStableHash.Create();
        hash.AddString("ks-procgen-geometry-plan-v3");
        hash.AddString(request.RequestId);
        hash.AddInt(request.Seed);
        hash.AddInt((int) request.Mode);
        hash.AddInt((int) request.GeometryMode);
        hash.AddInt((int) request.ConnectivityPolicy);
        hash.AddString(themeId);
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
                hash.AddString(entity.EntityId);
                hash.AddInt(entity.Cell.X);
                hash.AddInt(entity.Cell.Y);
                hash.AddInt(entity.QuarterTurns);
                hash.AddInt((int) entity.Movement);
                hash.AddInt(entity.InteractionApproach.HasValue ? 1 : 0);
                if (entity.InteractionApproach.HasValue)
                {
                    hash.AddInt(entity.InteractionApproach.Value.X);
                    hash.AddInt(entity.InteractionApproach.Value.Y);
                }
            }
            hash.AddInt(furnished.Proposal.Omissions.Count);
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
