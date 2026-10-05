using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

/// <summary>
/// Bounds only the geometry search. Route and engine validators have separate mandatory budgets.
/// </summary>
[DataDefinition]
public sealed partial class KsProcgenPackingBudgets
{
    [DataField] public int MaxSearchNodes = 50_000;
    [DataField] public int MaxCandidateProbes = 2_000_000;
    [DataField] public int MaxSearchDepth = 512;
    [DataField] public int MaxRouteExpandedCells = 1_000_000;
    [DataField] public int PreferredPrefabCoveragePercent = 70;
}

public enum KsProcgenPackingStatus : byte
{
    GeometryReady,
    InvalidInput,
    NoGeometricCover,
    NoPreliminaryRoute,
    BudgetExceeded,
}

/// <summary>
/// Complete cell ownership only. This never implies that ports, hull, or spawned entities validate.
/// </summary>
public sealed class KsProcgenPackingResult
{
    public KsProcgenPackingStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public bool SearchComplete { get; init; }
    public int SearchNodes { get; init; }
    public int CandidateProbes { get; init; }
    public int RouteExpandedCells { get; init; }
    public int MatchedExteriorEdges { get; init; }
    public int PrefabCells { get; init; }
    public IReadOnlyList<KsProcgenPlacementCandidate> Placements { get; init; } = [];
    public IReadOnlyList<(Vector2i Cell, KsProcgenCellClaim Claim)> CellClaims { get; init; } = [];
    public KsProcgenResidualResult? ResidualRouting { get; init; }
}

/// <summary>
/// Search over exact cell reservations, room choice, rotations, and procedural residuals.
/// This stage produces geometry for later connectivity, hull, and engine checks.
/// </summary>
public static class KsProcgenPackingPlanner
{
    public static KsProcgenPackingResult Plan(
        KsProcgenRequest request,
        IReadOnlyList<KsProcgenLayoutFamily> families,
        KsProcgenPackingBudgets? budgets = null,
        IReadOnlySet<Vector2i>? inspectedExistingPassages = null)
    {
        budgets ??= new KsProcgenPackingBudgets();
        inspectedExistingPassages ??= new HashSet<Vector2i>();
        if (families == null || budgets.MaxSearchNodes <= 0 || budgets.MaxSearchNodes > 50_000 ||
            budgets.MaxCandidateProbes <= 0 || budgets.MaxCandidateProbes > 2_000_000 ||
            budgets.MaxSearchDepth <= 0 || budgets.MaxSearchDepth > 512 ||
            budgets.MaxRouteExpandedCells <= 0 || budgets.MaxRouteExpandedCells > 1_000_000 ||
            budgets.PreferredPrefabCoveragePercent < 0 || budgets.PreferredPrefabCoveragePercent > 100)
        {
            return new KsProcgenPackingResult
            {
                Status = KsProcgenPackingStatus.InvalidInput,
                Issue = new KsProcgenIssue("InvalidPackingBudgets", "Packing budgets or coverage target are invalid."),
            };
        }

        if (!KsProcgenGeometry.TryNormalize(request, out var shape, out var issue))
            return new KsProcgenPackingResult { Status = KsProcgenPackingStatus.InvalidInput, Issue = issue };

        if (shape!.EntranceContract != null || request.ConnectivityPolicy == KsProcgenConnectivityPolicy.DeclaredNetworks)
            return new()
            {
                Status = KsProcgenPackingStatus.InvalidInput,
                Issue = new("UnsupportedEntranceAwarePacking", "Group-aware carving/partitioning must replace legacy routing before generation."),
            };

        if (request.RootCells.Any(root => !shape!.ContainsTarget(root)) ||
            inspectedExistingPassages.Any(cell => !shape!.ContainsTarget(cell)))
        {
            return new KsProcgenPackingResult
            {
                Status = KsProcgenPackingStatus.InvalidInput,
                Issue = new KsProcgenIssue("InvalidRouteContext", "Roots and inspected passages must lie in the target."),
            };
        }

        var familyIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var family in families)
        {
            if (family == null || !family.TryValidate(out issue) || !familyIds.Add(family.Id))
            {
                return new KsProcgenPackingResult
                {
                    Status = KsProcgenPackingStatus.InvalidInput,
                    Issue = issue ?? new KsProcgenIssue("DuplicateFamily", "Room family IDs must be unique."),
                };
            }
        }

        if (shape!.TargetCells.Count == 0)
        {
            return new KsProcgenPackingResult
            {
                Status = KsProcgenPackingStatus.GeometryReady,
                SearchComplete = true,
            };
        }

        if (request.Mode == KsProcgenMode.Prefabs && families.Count == 0 &&
            shape.TargetCells.Count != shape.PreservedCells.Count)
        {
            return new KsProcgenPackingResult
            {
                Status = KsProcgenPackingStatus.NoGeometricCover,
                SearchComplete = true,
                Issue = new KsProcgenIssue("NoRoomLibrary", "Prefab mode needs eligible room layouts for uncovered cells."),
            };
        }

        var plan = new KsProcgenPlan(shape);
        var selected = new List<KsProcgenPlacementCandidate>();
        var scores = new Dictionary<Vector2i, int>();
        foreach (var cell in shape.TargetCells)
        {
            var score = 0;
            if (!shape.ContainsTarget(cell + new Vector2i(1, 0))) score++;
            if (!shape.ContainsTarget(cell + new Vector2i(-1, 0))) score++;
            if (!shape.ContainsTarget(cell + new Vector2i(0, 1))) score++;
            if (!shape.ContainsTarget(cell + new Vector2i(0, -1))) score++;
            scores.Add(cell, score);
        }

        var orderedCells = new SortedSet<Vector2i>(Comparer<Vector2i>.Create((left, right) =>
        {
            var compare = scores[right].CompareTo(scores[left]);
            if (compare != 0) return compare;
            compare = left.Y.CompareTo(right.Y);
            return compare != 0 ? compare : left.X.CompareTo(right.X);
        }));
        foreach (var cell in shape.TargetCells)
        {
            if (!shape.ContainsPreserved(cell))
                orderedCells.Add(cell);
        }

        var sortedFamilies = families.OrderBy(family => family.Id, StringComparer.Ordinal).ToArray();
        var desiredPrefabCells = (int) Math.Floor(
            (shape.TargetCells.Count - shape.PreservedCells.Count) *
            budgets.PreferredPrefabCoveragePercent / 100.0 + 0.5);

        var searchComplete = true;
        var searchNodes = 0;
        var candidateProbes = 0;
        var routeExpandedCells = 0;
        var sawGeometricCover = false;
        KsProcgenIssue? lastRouteIssue = null;
        var bestExterior = -1;
        var bestCoverageDistance = int.MaxValue;
        var bestPrefabCells = -1;
        IReadOnlyList<KsProcgenPlacementCandidate> bestPlacements = [];
        IReadOnlyList<(Vector2i Cell, KsProcgenCellClaim Claim)> bestClaims = [];
        KsProcgenResidualResult? bestRouting = null;

        void Evaluate(int matchedExterior, int prefabCells)
        {
            if (orderedCells.Count != 0)
                return;

            sawGeometricCover = true;
            var remainingRouteBudget = budgets.MaxRouteExpandedCells - routeExpandedCells;
            if (remainingRouteBudget <= 0)
            {
                searchComplete = false;
                return;
            }

            var completeClaims = plan.SnapshotTargetClaims();
            var preliminary = KsProcgenResidualConnector.Connect(shape!,
                new KsProcgenPackingResult
                {
                    Status = KsProcgenPackingStatus.GeometryReady,
                    Placements = selected.ToArray(),
                    CellClaims = completeClaims,
                },
                request.RootCells, inspectedExistingPassages,
                maxExpandedCells: Math.Min(remainingRouteBudget, 1_000_000),
                requireAllProceduralConnected: request.Mode == KsProcgenMode.Procedural &&
                    request.ConnectivityPolicy == KsProcgenConnectivityPolicy.SingleNetwork);
            routeExpandedCells += preliminary.ExpandedCells;
            if (preliminary.Status != KsProcgenResidualStatus.PreliminaryReady)
            {
                lastRouteIssue = preliminary.Issue;
                if (preliminary.Status == KsProcgenResidualStatus.BudgetExceeded)
                    searchComplete = false;
                return;
            }

            var coverageDistance = Math.Abs(prefabCells - desiredPrefabCells);
            if (matchedExterior < bestExterior ||
                (matchedExterior == bestExterior && coverageDistance > bestCoverageDistance) ||
                (matchedExterior == bestExterior && coverageDistance == bestCoverageDistance &&
                 prefabCells <= bestPrefabCells))
                return;

            bestExterior = matchedExterior;
            bestCoverageDistance = coverageDistance;
            bestPrefabCells = prefabCells;
            bestPlacements = selected.ToArray();
            bestClaims = completeClaims;
            bestRouting = preliminary;
        }

        void Search(int depth, int matchedExterior, int prefabCells)
        {
            if (searchNodes >= budgets.MaxSearchNodes)
            {
                searchComplete = false;
                return;
            }

            searchNodes++;
            if (orderedCells.Count == 0)
            {
                Evaluate(matchedExterior, prefabCells);
                return;
            }

            if (depth >= budgets.MaxSearchDepth)
            {
                searchComplete = false;
                if (request.Mode != KsProcgenMode.Prefabs)
                    FillRemainderAndEvaluate(matchedExterior, prefabCells);
                return;
            }

            if (request.Mode != KsProcgenMode.Procedural)
            {
                var pivot = orderedCells.Min;
                var candidates = new List<KsProcgenPlacementCandidate>();
                foreach (var family in sortedFamilies)
                {
                    var remainingProbes = budgets.MaxCandidateProbes - candidateProbes;
                    if (remainingProbes <= 0)
                    {
                        searchComplete = false;
                        break;
                    }

                    var proposed = KsProcgenLayoutGeometry.FindCandidatesCovering(family, shape, pivot,
                        remainingProbes, out var complete, out var probesUsed);
                    candidateProbes += probesUsed;
                    if (!complete)
                        searchComplete = false;

                    foreach (var candidate in proposed)
                    {
                        if (candidate.Claims.All(entry => orderedCells.Contains(entry.Cell)))
                            candidates.Add(candidate);
                    }
                }

                foreach (var candidate in candidates
                             .OrderByDescending(candidate => candidate.MatchedExteriorEdges)
                             .ThenBy(candidate => SeededRank(request.Seed, candidate)))
                {
                    var checkpoint = plan.Checkpoint();
                    if (!KsProcgenLayoutGeometry.TryClaimCandidate(plan, candidate))
                        continue;

                    foreach (var (cell, _) in candidate.Claims)
                        orderedCells.Remove(cell);

                    selected.Add(candidate);
                    var placedPrefabCells = candidate.Claims.Count(entry =>
                        entry.Claim.Disposition == KsProcgenCellDisposition.Prefab);
                    Search(depth + 1, matchedExterior + candidate.MatchedExteriorEdges,
                        prefabCells + placedPrefabCells);
                    selected.RemoveAt(selected.Count - 1);

                    plan.Rollback(checkpoint);
                    foreach (var (cell, _) in candidate.Claims)
                        orderedCells.Add(cell);

                    if (searchNodes >= budgets.MaxSearchNodes)
                        break;
                }
            }

            if (request.Mode == KsProcgenMode.Prefabs)
                return;

            var residual = orderedCells.Min;
            var residualCheckpoint = plan.Checkpoint();
            if (!plan.TryClaim(residual,
                    new KsProcgenCellClaim("<procedural-residual>", KsProcgenCellDisposition.ProceduralFloor)))
                return;

            orderedCells.Remove(residual);
            Search(depth + 1, matchedExterior, prefabCells);
            plan.Rollback(residualCheckpoint);
            orderedCells.Add(residual);
        }

        void FillRemainderAndEvaluate(int matchedExterior, int prefabCells)
        {
            var checkpoint = plan.Checkpoint();
            var remaining = orderedCells.ToArray();
            foreach (var cell in remaining)
            {
                if (!plan.TryClaim(cell, new KsProcgenCellClaim("<procedural-residual>",
                        KsProcgenCellDisposition.ProceduralFloor)))
                {
                    plan.Rollback(checkpoint);
                    foreach (var restoredCell in remaining)
                        orderedCells.Add(restoredCell);
                    return;
                }

                orderedCells.Remove(cell);
            }

            Evaluate(matchedExterior, prefabCells);
            plan.Rollback(checkpoint);
            foreach (var cell in remaining)
                orderedCells.Add(cell);
        }

        if (request.Mode == KsProcgenMode.Procedural)
            FillRemainderAndEvaluate(0, 0);
        else
        {
            if (request.Mode == KsProcgenMode.Hybrid)
                FillRemainderAndEvaluate(0, 0);

            Search(0, 0, 0);
        }

        if (bestExterior < 0)
        {
            var status = !searchComplete
                ? KsProcgenPackingStatus.BudgetExceeded
                : sawGeometricCover
                    ? KsProcgenPackingStatus.NoPreliminaryRoute
                    : KsProcgenPackingStatus.NoGeometricCover;
            return new KsProcgenPackingResult
            {
                Status = status,
                SearchComplete = searchComplete,
                SearchNodes = searchNodes,
                CandidateProbes = candidateProbes,
                RouteExpandedCells = routeExpandedCells,
                Issue = status == KsProcgenPackingStatus.NoPreliminaryRoute
                    ? lastRouteIssue ?? new KsProcgenIssue("NoPreliminaryRoute", "No geometric cover passed preliminary routing.")
                    : new KsProcgenIssue(status == KsProcgenPackingStatus.NoGeometricCover
                            ? "NoGeometricCover" : "SearchBudgetExceeded",
                        status == KsProcgenPackingStatus.NoGeometricCover
                            ? "No exact cell cover was found." : "Search stopped before a complete routed cover was found."),
            };
        }

        return new KsProcgenPackingResult
        {
            Status = KsProcgenPackingStatus.GeometryReady,
            SearchComplete = searchComplete,
            SearchNodes = searchNodes,
            CandidateProbes = candidateProbes,
            RouteExpandedCells = routeExpandedCells,
            MatchedExteriorEdges = bestExterior,
            PrefabCells = bestPrefabCells,
            Placements = bestPlacements,
            CellClaims = bestClaims,
            ResidualRouting = bestRouting,
        };
    }

    private static ulong SeededRank(int rootSeed, KsProcgenPlacementCandidate candidate)
    {
        var hash = KsProcgenStableHash.Create();
        hash.AddInt(rootSeed);
        hash.AddString("selection");
        hash.AddString(candidate.FamilyId);
        hash.AddString(candidate.OptionId);
        hash.AddInt(candidate.Origin.X);
        hash.AddInt(candidate.Origin.Y);
        hash.AddInt(candidate.QuarterTurns);
        return hash.Value;
    }
}
