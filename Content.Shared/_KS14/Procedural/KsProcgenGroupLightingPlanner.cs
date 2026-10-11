using System.Linq;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Procedural;

public sealed class KsProcgenGroupLightingResult
{
    public KsProcgenLightingPlanStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public IReadOnlyList<KsProcgenLitRegion> Regions { get; init; } = [];
    public int ExpandedCells { get; init; }
    public int FixtureCount { get; init; }
    public bool WorkingCoverageVerified => false;
}

/// <summary>Atomic room-scoped lighting with shared fixture and graph work allowances.</summary>
public static class KsProcgenGroupLightingPlanner
{
    public static KsProcgenGroupLightingResult Plan(IPrototypeManager prototypeManager, KsProcgenThemeAssignmentResult themes,
        IReadOnlyList<KsProcgenFurnishedRegion> furnishings, int seed, int maximumFixtures = 64,
        int maximumExpandedCells = 1_000_000, int maximumRoomCells = 512)
    {
        var expanded = 0;
        var fixtureCount = 0;
        KsProcgenGroupLightingResult Fail(KsProcgenLightingPlanStatus status, string code) => new()
        {
            Status = status, Issue = new(code, "Complete owned lighting selection did not validate."),
            ExpandedCells = expanded, FixtureCount = fixtureCount,
        };
        if (prototypeManager == null || themes?.Status != KsProcgenThemeAssignmentStatus.Selected || furnishings == null ||
            themes.Regions.Count is < 1 or > 256 || furnishings.Count != themes.Regions.Count ||
            maximumFixtures is < 0 or > 4_096 || maximumExpandedCells is < 0 or > 1_000_000 ||
            maximumRoomCells is < 1 or > 65_536)
            return Fail(KsProcgenLightingPlanStatus.InvalidInput, "InvalidGroupLightingInput");
        var byId = new Dictionary<string, KsProcgenFurnishingResult>(StringComparer.Ordinal);
        foreach (var region in furnishings)
            if (region == null || region.Proposal == null || string.IsNullOrWhiteSpace(region.RegionId) || !byId.TryAdd(region.RegionId, region.Proposal))
                return Fail(KsProcgenLightingPlanStatus.InvalidInput, "InvalidGroupLightingRegions");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var regions = new List<KsProcgenLitRegion>();
        foreach (var region in themes.Regions.OrderBy(region => region.Id, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(region.Id) || !ids.Add(region.Id) || !byId.TryGetValue(region.Id, out var furnished))
                return Fail(KsProcgenLightingPlanStatus.InvalidInput, "InvalidGroupLightingRegions");
            var remainingFixtures = maximumFixtures - fixtureCount;
            var requested = region.Theme.LightingPackId != null && region.Theme.LightFixtureId != null;
            if (requested && remainingFixtures == 0)
                return Fail(KsProcgenLightingPlanStatus.BudgetExceeded, "GroupLightingFixtureBudget");
            var proposal = KsProcgenLightingPlanner.Plan(prototypeManager, region, furnished, seed,
                maxFixtures: Math.Max(1, remainingFixtures), maxRoomCells: maximumRoomCells,
                maxExpandedCells: maximumExpandedCells - expanded);
            expanded += proposal.ExpandedCells;
            fixtureCount += proposal.Lights.Count;
            if (proposal.Status is KsProcgenLightingPlanStatus.InvalidInput or KsProcgenLightingPlanStatus.BudgetExceeded)
                return Fail(proposal.Status, proposal.Issue!.Code);
            regions.Add(new(region.Id, proposal));
        }
        return new()
        {
            Status = regions.All(region => region.Proposal.Status == KsProcgenLightingPlanStatus.NotRequested)
                ? KsProcgenLightingPlanStatus.NotRequested : regions.Any(region => region.Proposal.Status == KsProcgenLightingPlanStatus.Sparse)
                    ? KsProcgenLightingPlanStatus.Sparse : KsProcgenLightingPlanStatus.Proposed,
            Regions = regions.ToReadOnlyList(), ExpandedCells = expanded, FixtureCount = fixtureCount,
        };
    }
}
