using System.Linq;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Procedural;

public enum KsProcgenLightingPlanStatus : byte
{
    Proposed,
    Sparse,
    NotRequested,
    InvalidInput,
    BudgetExceeded,
}

public sealed record KsProcgenLightProposal(
    Vector2i Cell,
    string EntityId,
    string FixtureId,
    KsProcgenLightingSupply DeclaredSupply);

/// <summary>
/// Geometric light proposal. Coverage is a floor-graph estimate that ignores real light physics,
/// fixture startup, supply, and opaque entity occlusion. It cannot satisfy a hard coverage gate.
/// </summary>
public sealed class KsProcgenLightingPlanResult
{
    public KsProcgenLightingPlanStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public IReadOnlyList<KsProcgenLightProposal> Lights { get; init; } = [];
    public int EstimatedCoveredCells { get; init; }
    public int TotalFloorCells { get; init; }
    public float RequestedCoverage { get; init; }
    public bool WorkingCoverageVerified => false;
}

public static class KsProcgenLightingPlanner
{
    private static readonly Vector2i[] Cardinal = [new(1, 0), new(0, 1), new(-1, 0), new(0, -1)];

    public static KsProcgenLightingPlanResult Plan(
        IPrototypeManager prototypeManager,
        KsProcgenThemedRegion region,
        KsProcgenFurnishingResult furnishings,
        int seed,
        int maxFixtures = 64,
        int maxRoomCells = 512)
    {
        if (prototypeManager == null || region == null || furnishings == null ||
            furnishings.Status is not (KsProcgenFurnishingStatus.Proposed or KsProcgenFurnishingStatus.Sparse) ||
            maxFixtures <= 0 || maxFixtures > 4_096 || maxRoomCells <= 0 || maxRoomCells > 65_536)
            return Failure(KsProcgenLightingPlanStatus.InvalidInput, "InvalidLightingPlanInput");

        var floor = new HashSet<Vector2i>(region.FloorCells);
        if (floor.Count == 0 || floor.Count != region.FloorCells.Count ||
            furnishings.ProtectedPassageCells.Any(cell => !floor.Contains(cell)) ||
            furnishings.Entities.Any(entity => entity.OccupiedCells.Any(cell => !floor.Contains(cell))))
            return Failure(KsProcgenLightingPlanStatus.InvalidInput, "InvalidLightingFloor");

        if (region.Theme.LightingPackId == null || region.Theme.LightFixtureId == null)
            return new KsProcgenLightingPlanResult
            {
                Status = KsProcgenLightingPlanStatus.NotRequested,
                TotalFloorCells = floor.Count,
            };
        if (!prototypeManager.TryIndex<KsProcgenLightingPackPrototype>(region.Theme.LightingPackId,
                out var pack) ||
            !KsProcgenThemeValidator.TryResolve(prototypeManager, region.Theme.ThemeId,
                out var theme, out _) ||
            pack.Fixtures.FirstOrDefault(item => item.Id == region.Theme.LightFixtureId) is not { } fixture)
            return Failure(KsProcgenLightingPlanStatus.InvalidInput, "InvalidLightingFixture");

        var goal = theme!.Goals.LightingCoverage;
        if (floor.Count > maxRoomCells)
            return new KsProcgenLightingPlanResult
            {
                Status = KsProcgenLightingPlanStatus.Sparse,
                Issue = new KsProcgenIssue("LightingRoomCellBudget",
                    "The room is too large for bounded geometric fixture placement."),
                TotalFloorCells = floor.Count,
                RequestedCoverage = goal,
            };

        var unavailable = new HashSet<Vector2i>(furnishings.ProtectedPassageCells);
        unavailable.UnionWith(furnishings.Entities.SelectMany(entity => entity.OccupiedCells));
        unavailable.UnionWith(furnishings.Entities.Where(entity => entity.InteractionApproach.HasValue)
            .Select(entity => entity.InteractionApproach!.Value));
        // Conservatively treat fixture proposals as blocking, as the example floor post is solid.
        var blocked = new HashSet<Vector2i>(furnishings.Entities
            .Where(entity => entity.Movement != KsProcgenMovementClass.Clear)
            .SelectMany(entity => entity.OccupiedCells));
        var candidates = KsProcgenGeometry.SortCells(floor.Where(cell => !unavailable.Contains(cell)));
        var radius = Math.Max(1, fixture.PreferredSpacing / 2);
        var footprints = candidates.ToDictionary(cell => cell, cell => EstimateReach(cell, floor, radius));
        var selected = new List<KsProcgenLightProposal>();
        var covered = new HashSet<Vector2i>();
        var remaining = new HashSet<Vector2i>(candidates);
        while (remaining.Count > 0 && selected.Count < maxFixtures &&
               covered.Count < (int) Math.Ceiling(floor.Count * goal))
        {
            var ranked = remaining.Where(cell => PreservesConnectivity(cell, floor, blocked)).Select(cell =>
                    (Cell: cell, Gain: footprints[cell].Count(other => !covered.Contains(other)),
                     Rank: Rank(seed, region.Id, cell)))
                .OrderByDescending(candidate => candidate.Gain)
                .ThenBy(candidate => candidate.Rank)
                .ThenBy(candidate => candidate.Cell.Y)
                .ThenBy(candidate => candidate.Cell.X).ToList();
            if (ranked.Count == 0)
                break;
            var best = ranked[0];
            if (best.Gain == 0)
                break;
            selected.Add(new KsProcgenLightProposal(best.Cell, fixture.Entity,
                fixture.Id, fixture.Supply));
            covered.UnionWith(footprints[best.Cell]);
            remaining.Remove(best.Cell);
            blocked.Add(best.Cell);
        }

        var reachedEstimate = covered.Count >= (int) Math.Ceiling(floor.Count * goal);
        var hasFurtherSafeGain = remaining.Any(cell => PreservesConnectivity(cell, floor, blocked) &&
            footprints[cell].Any(other => !covered.Contains(other)));
        return new KsProcgenLightingPlanResult
        {
            Status = reachedEstimate ? KsProcgenLightingPlanStatus.Proposed :
                selected.Count >= maxFixtures && hasFurtherSafeGain
                    ? KsProcgenLightingPlanStatus.BudgetExceeded : KsProcgenLightingPlanStatus.Sparse,
            Issue = reachedEstimate ? null : new KsProcgenIssue(
                selected.Count >= maxFixtures && hasFurtherSafeGain
                    ? "LightingProposalBudget" : "LightingEstimateShortfall",
                "The geometric light estimate did not reach the requested coverage."),
            Lights = selected,
            EstimatedCoveredCells = covered.Count,
            TotalFloorCells = floor.Count,
            RequestedCoverage = goal,
        };
    }

    private static bool PreservesConnectivity(Vector2i candidate, IReadOnlySet<Vector2i> floor,
        IReadOnlySet<Vector2i> blocked)
    {
        var neighbors = Cardinal.Select(offset => candidate + offset)
            .Where(cell => floor.Contains(cell) && !blocked.Contains(cell)).ToArray();
        if (neighbors.Length == 0)
            return false;
        if (neighbors.Length == 1)
            return true;

        var reached = new HashSet<Vector2i> { neighbors[0] };
        var queue = new Queue<Vector2i>();
        queue.Enqueue(neighbors[0]);
        while (queue.TryDequeue(out var current))
        {
            foreach (var offset in Cardinal)
            {
                var next = current + offset;
                if (next != candidate && floor.Contains(next) && !blocked.Contains(next) && reached.Add(next))
                    queue.Enqueue(next);
            }
        }

        return neighbors.All(reached.Contains);
    }

    private static IReadOnlySet<Vector2i> EstimateReach(Vector2i origin, IReadOnlySet<Vector2i> floor, int radius)
    {
        var reached = new HashSet<Vector2i> { origin };
        var queue = new Queue<(Vector2i Cell, int Distance)>();
        queue.Enqueue((origin, 0));
        while (queue.TryDequeue(out var current))
        {
            if (current.Distance >= radius)
                continue;
            foreach (var offset in Cardinal)
            {
                var next = current.Cell + offset;
                if (floor.Contains(next) && reached.Add(next))
                    queue.Enqueue((next, current.Distance + 1));
            }
        }

        return reached;
    }

    private static ulong Rank(int seed, string regionId, Vector2i cell)
    {
        var hash = KsProcgenStableHash.Create();
        hash.AddInt(seed);
        hash.AddString("lighting-position");
        hash.AddString(regionId);
        hash.AddInt(cell.X);
        hash.AddInt(cell.Y);
        return hash.Value;
    }

    private static KsProcgenLightingPlanResult Failure(KsProcgenLightingPlanStatus status, string code) => new()
    {
        Status = status,
        Issue = new KsProcgenIssue(code, "Lighting proposal input or budget could not be satisfied."),
    };
}
