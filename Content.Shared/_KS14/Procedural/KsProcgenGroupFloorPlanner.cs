using System.Collections.Frozen;
using System.Globalization;
using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

/// <summary>A connected floor component with its complete permitted entrance-group membership.</summary>
public sealed record KsProcgenGroupFloorRegion(string Id, IReadOnlyList<string> GroupIds,
    IReadOnlyList<Vector2i> FloorCells, IReadOnlyList<Vector2i> ProtectedCells);

public sealed class KsProcgenGroupFloorResult
{
    public KsProcgenEntranceSceneStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public KsProcgenGroupRouteResult? Routing { get; init; }
    public KsProcgenEntranceScene? Scene { get; init; }
    public KsProcgenEntranceSceneResult? Analysis { get; init; }
    public IReadOnlyList<KsProcgenGroupFloorRegion> Regions { get; init; } = [];
    public IReadOnlyList<Vector2i> FloorCells { get; init; } = [];
    public IReadOnlyList<Vector2i> WallCells { get; init; } = [];
    public IReadOnlySet<Vector2i> ProtectedCells { get; init; } = FrozenSet<Vector2i>.Empty;
    public int OrderProbes { get; init; }
    public int GrowthProbes { get; init; }
    public int ExpandedCells { get; init; }
    public ulong FloorHash { get; init; }
    public bool EngineAccessVerified => false;
}

public sealed class KsProcgenGroupFloorSelection
{
    public KsProcgenEntranceSceneStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public KsProcgenGroupFloorResult? Accepted { get; init; }
    public int CandidateProbes { get; init; }
    public int OrderProbes { get; init; }
    public int ExpandedCells { get; init; }
    public ulong SelectionHash { get; init; }
}

/// <summary>
/// Grow writable floor from protected entrance trees while maintaining required separation. Cell
/// ownership and walls are proposals; no actor, door, material, atmosphere or entity is inspected here.
/// </summary>
public static class KsProcgenGroupFloorPlanner
{
    private static readonly Vector2i[] Cardinal = [new(1, 0), new(0, 1), new(-1, 0), new(0, -1)];

    public static KsProcgenGroupFloorSelection Select(KsProcgenRequest request, KsProcgenGroupRouteContext context,
        int maximumCandidateProbes = 64, int maximumOrderProbes = 64, int maximumExpandedCells = 262_144)
    {
        var probes = 0;
        var orders = 0;
        var expanded = 0;
        KsProcgenGroupFloorSelection Fail(KsProcgenEntranceSceneStatus status, KsProcgenIssue? issue) => new()
            { Status = status, Issue = issue, CandidateProbes = probes, OrderProbes = orders, ExpandedCells = expanded };
        if (!KsProcgenGeometry.TryNormalize(request, out var shape, out var issue))
            return Fail(KsProcgenEntranceSceneStatus.InvalidInput, issue);
        if (shape!.EntranceContract == null || maximumCandidateProbes is < 0 or > 64 ||
            maximumOrderProbes is < 0 or > 64 || maximumExpandedCells is < 0 or > 1_000_000)
            return Fail(KsProcgenEntranceSceneStatus.InvalidInput, new("InvalidGroupFloorSelection", "A bounded entrance contract is required."));
        foreach (var configuration in KsProcgenEntranceConfigurationOrder.Order(shape.EntranceContract.Configurations, request.Seed, request.RequestId))
        {
            if (probes >= maximumCandidateProbes)
                return Fail(KsProcgenEntranceSceneStatus.BudgetExceeded, new("GroupFloorCandidateBudget", "Complete floor proposals exhausted their allowance."));
            probes++;
            var proposal = Plan(request, configuration.Id, context, maximumOrderProbes: maximumOrderProbes - orders,
                maximumExpandedCells: maximumExpandedCells - expanded);
            orders += proposal.OrderProbes;
            expanded += proposal.ExpandedCells;
            if (proposal.Status == KsProcgenEntranceSceneStatus.Rejected)
                continue;
            if (proposal.Status != KsProcgenEntranceSceneStatus.Candidate)
                return Fail(proposal.Status, proposal.Issue);
            return new() { Status = proposal.Status, Accepted = proposal, CandidateProbes = probes,
                OrderProbes = orders, ExpandedCells = expanded, SelectionHash = SelectionHash(request, proposal) };
        }
        return Fail(KsProcgenEntranceSceneStatus.Rejected, new("NoGroupFloorProposal", "No attempted complete floor proposal validated."));
    }

    internal static ulong SelectionHash(KsProcgenRequest request, KsProcgenGroupFloorResult proposal)
    {
        var hash = KsProcgenStableHash.Create();
        hash.AddString("ks-procgen-group-floor-selection-v1");
        hash.AddString(request.RequestId);
        hash.AddInt(request.Seed);
        hash.AddString(proposal.FloorHash.ToString(CultureInfo.InvariantCulture));
        return hash.Value;
    }

    public static KsProcgenGroupFloorResult Plan(KsProcgenRequest request, string configurationId,
        KsProcgenGroupRouteContext context, int maximumOrderProbes = 64, int maximumExpandedCells = 262_144)
    {
        var expanded = 0;
        var orders = 0;
        var growth = 0;
        KsProcgenGroupFloorResult Fail(KsProcgenEntranceSceneStatus status, KsProcgenIssue? issue) => new()
            { Status = status, Issue = issue, OrderProbes = orders, GrowthProbes = growth, ExpandedCells = expanded };
        if (!KsProcgenGeometry.TryNormalize(request, out var shape, out var issue))
            return Fail(KsProcgenEntranceSceneStatus.InvalidInput, issue);
        var search = KsProcgenGroupRoutePlanner.Search(request, configurationId, context,
            maximumOrderProbes: maximumOrderProbes, maximumExpandedCells: maximumExpandedCells);
        orders = search.OrderProbes;
        expanded = search.ExpandedCells;
        if (search.Status != KsProcgenEntranceSceneStatus.Candidate)
            return Fail(search.Status, search.Issue);
        var routing = search.Accepted!;
        var configuration = shape!.EntranceContract!.Configurations.Single(configuration => configuration.Id == configurationId);
        var exact = configuration.Mode == KsProcgenEntranceConnectionMode.ExactComponents;
        var protectedCells = routing.FloorCells.ToFrozenSet();
        var floorOwner = new Dictionary<Vector2i, int>();
        foreach (var component in routing.Analysis!.Components)
        foreach (var cell in component.Cells)
            floorOwner.Add(cell, component.Component);
        var hostComponentByCell = new Dictionary<Vector2i, int>();
        var hostOwner = new Dictionary<int, int>();
        if (exact && configuration.IsolationScope == KsProcgenEntranceIsolationScope.RequestNetwork)
        {
            // Ownership of read-only host components is a reservation, never an exterior write.
            // A component not attached to a seed may be claimed by the first floor reaching it.
            for (var index = 0; index < routing.Analysis.HostComponents.Count; index++)
            foreach (var cell in routing.Analysis.HostComponents[index])
            {
                if (expanded >= maximumExpandedCells)
                    return Fail(KsProcgenEntranceSceneStatus.BudgetExceeded, new("GroupFloorExpansionBudget", "Host ownership inspection exhausted the allowance."));
                expanded++;
                if (floorOwner.TryGetValue(cell, out var owner))
                    hostOwner[index] = owner;
                else
                    hostComponentByCell.Add(cell, index);
            }
        }
        var writable = shape.TargetCells.Where(shape.CanWrite).Where(cell => !routing.Scene!.BlockedThresholdCells.Contains(cell)).ToHashSet();
        var frontier = new Queue<(int Owner, Vector2i Cell, bool Seed)>();
        var queued = new HashSet<(int Owner, Vector2i Cell)>();
        var components = routing.Analysis.Components.OrderBy(component =>
                KsProcgenRandom.ForStage(request.Seed, "entrance/floor-growth-order", request.RequestId + "/" + component.GroupIds[0]).NextUInt64())
            .ThenBy(component => component.GroupIds[0], StringComparer.Ordinal);
        foreach (var component in components)
        foreach (var cell in component.Cells)
        {
            frontier.Enqueue((component.Component, cell, true));
            queued.Add((component.Component, cell));
        }
        while (frontier.TryDequeue(out var entry))
        {
            if (expanded >= maximumExpandedCells)
                return Fail(KsProcgenEntranceSceneStatus.BudgetExceeded, new("GroupFloorExpansionBudget", "Floor growth exhausted the shared allowance."));
            expanded++;
            growth++;
            if (!entry.Seed)
            {
                if (floorOwner.ContainsKey(entry.Cell))
                    continue;
                if (exact && Cardinal.Any(step => floorOwner.TryGetValue(entry.Cell + step, out var foreign) && foreign != entry.Owner))
                    continue;
                var adjacentHost = Cardinal.Select(step => entry.Cell + step).Where(hostComponentByCell.ContainsKey)
                    .Select(cell => hostComponentByCell[cell]).Distinct().ToArray();
                if (exact && adjacentHost.Any(component => hostOwner.TryGetValue(component, out var foreign) && foreign != entry.Owner))
                    continue;
                floorOwner.Add(entry.Cell, entry.Owner);
                foreach (var component in adjacentHost)
                    hostOwner[component] = entry.Owner;
            }
            foreach (var step in Cardinal)
            {
                var cell = entry.Cell + step;
                if (writable.Contains(cell) && !floorOwner.ContainsKey(cell) && queued.Add((entry.Owner, cell)))
                    frontier.Enqueue((entry.Owner, cell, false));
            }
        }
        var scene = new KsProcgenEntranceScene(floorOwner.Keys.ToFrozenSet(), routing.Scene!.CleanHostCells,
            routing.Scene.BlockedThresholdCells, routing.Scene.HostContextComplete);
        var analysis = KsProcgenEntranceSceneAnalyzer.AnalyzeNormalized(shape, request.ConnectivityPolicy, request.RootCells,
            configurationId, scene, Math.Min(131_072, maximumExpandedCells - expanded));
        expanded += analysis.ExpandedCells;
        if (analysis.Status != KsProcgenEntranceSceneStatus.Candidate)
            return Fail(analysis.Status, analysis.Issue);
        var regions = analysis.Components.Select(component => new KsProcgenGroupFloorRegion(
            $"{request.RequestId}/EntranceNetwork/{component.Component}", component.GroupIds, component.Cells,
            component.Cells.Where(protectedCells.Contains).ToReadOnlyList())).ToReadOnlyList();
        var hash = KsProcgenStableHash.Create();
        hash.AddString("ks-procgen-group-floor-v1");
        hash.AddString(request.RequestId);
        hash.AddInt(request.Seed);
        hash.AddString(routing.RouteHash.ToString(CultureInfo.InvariantCulture));
        hash.AddString(analysis.SceneHash.ToString(CultureInfo.InvariantCulture));
        return new()
        {
            Status = KsProcgenEntranceSceneStatus.Candidate, Routing = routing, Scene = scene, Analysis = analysis,
            Regions = regions, FloorCells = KsProcgenGeometry.SortCells(floorOwner.Keys), ProtectedCells = protectedCells,
            WallCells = KsProcgenGeometry.SortCells(shape.TargetCells.Where(cell => shape.CanWrite(cell) && !floorOwner.ContainsKey(cell))),
            OrderProbes = orders, GrowthProbes = growth, ExpandedCells = expanded, FloorHash = hash.Value,
        };
    }
}
