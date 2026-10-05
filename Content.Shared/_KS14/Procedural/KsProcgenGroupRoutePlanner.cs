using System.Collections.Frozen;
using System.Globalization;
using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

/// <summary>Caller-declared inspected context. These records do not inspect live doors, actors or constants.</summary>
public sealed record KsProcgenGroupRouteContext(IReadOnlySet<Vector2i> ExistingCleanDomainCells,
    IReadOnlySet<Vector2i> CleanHostCells, IReadOnlySet<Vector2i> ExistingBlockedThresholds,
    bool HostContextComplete, bool PreservedContextComplete);

public sealed record KsProcgenGroupRoute(string GroupId, IReadOnlyList<Vector2i> NewCells);

public sealed class KsProcgenGroupRouteSelection
{
    public KsProcgenEntranceSceneStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public KsProcgenGroupRouteResult? Accepted { get; init; }
    public int CandidateProbes { get; init; }
    public int ExpandedCells { get; init; }
    public ulong SelectionHash { get; init; }
}

public sealed class KsProcgenGroupRouteResult
{
    public KsProcgenEntranceSceneStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public IReadOnlyList<KsProcgenGroupRoute> Routes { get; init; } = [];
    public IReadOnlyList<Vector2i> FloorCells { get; init; } = [];
    public IReadOnlyList<Vector2i> ProposedWallCells { get; init; } = [];
    public KsProcgenEntranceScene? Scene { get; init; }
    public KsProcgenEntranceSceneResult? Analysis { get; init; }
    public int RoutedGroups { get; init; }
    public int ExpandedCells { get; init; }
    public ulong RouteHash { get; init; }
    public bool EngineAccessVerified => false;
}

/// <summary>
/// Greedy group-scoped cardinal trees with separating-cell reservations. Not a room packing,
/// structural wall materializer or exhaustive feasibility solver. Every failed proposal is atomic.
/// </summary>
public static class KsProcgenGroupRoutePlanner
{
    private static readonly Vector2i[] Steps = [new(1, 0), new(0, 1), new(-1, 0), new(0, -1)];

    public static KsProcgenGroupRouteSelection Select(KsProcgenRequest request, KsProcgenGroupRouteContext context,
        int maximumCandidateProbes = 64, int maximumExpandedCells = 131_072)
    {
        var probes = 0;
        var expanded = 0;
        if (!KsProcgenGeometry.TryNormalize(request, out var shape, out var issue))
            return new() { Status = KsProcgenEntranceSceneStatus.InvalidInput, Issue = issue };
        if (shape!.EntranceContract == null || maximumCandidateProbes is < 0 or > 64 || maximumExpandedCells is < 0 or > 1_000_000)
            return new() { Status = KsProcgenEntranceSceneStatus.InvalidInput, Issue = new("InvalidGroupRouteSelection", "A bounded entrance contract is required.") };
        foreach (var configuration in KsProcgenEntranceConfigurationOrder.Order(shape.EntranceContract.Configurations, request.Seed, request.RequestId))
        {
            if (probes >= maximumCandidateProbes)
                return new() { Status = KsProcgenEntranceSceneStatus.BudgetExceeded,
                    Issue = new("GroupRouteProbeBudget", "Complete route proposals exhausted their probe allowance."),
                    CandidateProbes = probes, ExpandedCells = expanded };
            probes++;
            var proposal = Plan(request, configuration.Id, context, maximumExpandedCells: maximumExpandedCells - expanded);
            expanded += proposal.ExpandedCells;
            if (proposal.Status == KsProcgenEntranceSceneStatus.Rejected)
                continue;
            if (proposal.Status != KsProcgenEntranceSceneStatus.Candidate)
                return new() { Status = proposal.Status, Issue = proposal.Issue, CandidateProbes = probes, ExpandedCells = expanded };
            var hash = KsProcgenStableHash.Create();
            hash.AddString("ks-procgen-group-route-selection-v1");
            hash.AddInt(request.Seed);
            hash.AddString(request.RequestId);
            hash.AddString(proposal.RouteHash.ToString(CultureInfo.InvariantCulture));
            return new() { Status = KsProcgenEntranceSceneStatus.Candidate, Accepted = proposal,
                CandidateProbes = probes, ExpandedCells = expanded, SelectionHash = hash.Value };
        }
        return new() { Status = KsProcgenEntranceSceneStatus.Rejected, Issue = new("NoGroupRouteProposal", "No attempted greedy route proposal validated."),
            CandidateProbes = probes, ExpandedCells = expanded };
    }

    public static KsProcgenGroupRouteResult Plan(KsProcgenRequest request, string configurationId,
        KsProcgenGroupRouteContext context, int maximumExpandedCells = 131_072)
    {
        var expanded = 0;
        var routed = 0;
        KsProcgenGroupRouteResult Fail(KsProcgenEntranceSceneStatus status, string code) => new()
        {
            Status = status, Issue = new(code, $"No atomic group-route proposal for configuration '{configurationId}'."),
            ExpandedCells = expanded, RoutedGroups = routed,
        };
        if (!KsProcgenGeometry.TryNormalize(request, out var shape, out var issue))
            return new() { Status = KsProcgenEntranceSceneStatus.InvalidInput, Issue = issue };
        var configuration = shape!.EntranceContract?.Configurations.SingleOrDefault(configuration => configuration.Id == configurationId);
        if (configuration == null || configuration.Weight <= 0f || context == null ||
            context.ExistingCleanDomainCells == null || context.CleanHostCells == null || context.ExistingBlockedThresholds == null ||
            maximumExpandedCells is < 0 or > 1_000_000 || context.ExistingCleanDomainCells.Count > 65_536 ||
            context.CleanHostCells.Count > 65_536 || context.ExistingBlockedThresholds.Count > 65_536 ||
            context.ExistingCleanDomainCells.Any(cell => !shape.ContainsPreserved(cell)) ||
            context.CleanHostCells.Any(cell => shape.ContainsTarget(cell) || !ValidCell(cell)) ||
            context.ExistingBlockedThresholds.Any(cell => !ValidCell(cell) || context.CleanHostCells.Contains(cell) ||
                context.ExistingCleanDomainCells.Contains(cell)))
            return Fail(KsProcgenEntranceSceneStatus.InvalidInput, "InvalidGroupRouteContext");
        if (request.Mode != KsProcgenMode.Procedural || request.GeometryMode != KsProcgenGeometryMode.Footprint)
            return Fail(KsProcgenEntranceSceneStatus.InvalidInput, "UnsupportedGroupRouteGeometry");
        if (shape.PreservedCells.Count != 0 && !context.PreservedContextComplete)
            return Fail(KsProcgenEntranceSceneStatus.Rejected, "IncompleteGroupRoutePreservedContext");
        if (configuration.Groups.Count > 64)
            return Fail(KsProcgenEntranceSceneStatus.BudgetExceeded, "GroupRouteGroupBudget");
        var ports = shape.Entrances.ToDictionary(entrance => entrance.PortId, StringComparer.Ordinal);
        var terminals = new Dictionary<string, HashSet<Vector2i>>(StringComparer.Ordinal);
        foreach (var group in configuration.Groups)
        {
            var cells = group.Ports.SelectMany(port => ports[port].InsideApproach)
                .Concat(group.Ports.SelectMany(port => ports[port].ThresholdCells).Where(shape.ContainsTarget))
                .Concat(group.RootCells).ToHashSet();
            if (cells.Count > 1000)
                return Fail(KsProcgenEntranceSceneStatus.BudgetExceeded, "GroupRouteTerminalBudget");
            terminals.Add(group.Id, cells);
        }
        if (request.RootCells.Any(cell => !terminals.Values.Any(cells => cells.Contains(cell))))
            return Fail(KsProcgenEntranceSceneStatus.Rejected, "UnassignedGroupRouteRoot");
        var exact = configuration.Mode == KsProcgenEntranceConnectionMode.ExactComponents;
        var terminalOwner = new Dictionary<Vector2i, string>();
        if (exact)
        {
            foreach (var (groupId, cells) in terminals)
            foreach (var cell in cells)
            {
                if (terminalOwner.TryGetValue(cell, out var owner) && owner != groupId ||
                    Steps.Any(step => terminalOwner.TryGetValue(cell + step, out var adjacent) && adjacent != groupId))
                    return Fail(KsProcgenEntranceSceneStatus.Rejected, "InseparableGroupRouteTerminals");
                terminalOwner[cell] = groupId;
            }
        }
        var existing = context.ExistingCleanDomainCells.ToHashSet();
        if (existing.Count > maximumExpandedCells)
            return Fail(KsProcgenEntranceSceneStatus.BudgetExceeded, "GroupRouteExpansionBudget");
        expanded += existing.Count;
        var existingOwner = new Dictionary<Vector2i, string>();
        if (exact)
        {
            foreach (var component in KsProcgenGeometry.ConnectedComponents(existing))
            {
                var owners = terminals.Where(pair => component.Any(pair.Value.Contains)).Select(pair => pair.Key).ToArray();
                if (owners.Length != 1)
                    return Fail(KsProcgenEntranceSceneStatus.Rejected, owners.Length == 0 ?
                        "UnassignedExistingGroupPassage" : "PreservedGroupPassageJoinsNetworks");
                foreach (var cell in component)
                    existingOwner.Add(cell, owners[0]);
            }
        }
        var blocked = context.ExistingBlockedThresholds.ToHashSet();
        foreach (var portId in configuration.SealedPorts)
        foreach (var threshold in ports[portId].ThresholdCells)
        {
            if (blocked.Contains(threshold))
                continue;
            if (!shape.ContainsTarget(threshold) || !shape.CanWrite(threshold))
                return Fail(KsProcgenEntranceSceneStatus.Rejected, "GroupRouteSealingOutsideWriteBounds");
            blocked.Add(threshold);
        }
        if (terminals.Values.Any(cells => cells.Overlaps(blocked)))
            return Fail(KsProcgenEntranceSceneStatus.Rejected, "BlockedGroupRouteTerminal");
        var writable = shape.TargetCells.Where(shape.CanWrite).Where(cell => !blocked.Contains(cell)).ToHashSet();
        var floor = existing.ToHashSet();
        var claims = new Dictionary<Vector2i, string>();
        var routes = new List<KsProcgenGroupRoute>();
        foreach (var group in configuration.Groups)
        {
            var forbidden = new HashSet<Vector2i>();
            if (exact)
            {
                foreach (var cell in terminalOwner.Where(pair => pair.Value != group.Id).Select(pair => pair.Key)
                             .Concat(existingOwner.Where(pair => pair.Value != group.Id).Select(pair => pair.Key))
                             .Concat(claims.Where(pair => pair.Value != group.Id).Select(pair => pair.Key)))
                {
                    forbidden.Add(cell);
                    foreach (var step in Steps)
                        forbidden.Add(cell + step);
                }
            }
            var routeRequest = new KsProcgenRouteRequest();
            routeRequest.WritableCells.UnionWith(writable.Where(cell => !forbidden.Contains(cell)));
            routeRequest.ExistingPassageCells.UnionWith(exact ?
                existing.Where(cell => existingOwner[cell] == group.Id && !forbidden.Contains(cell)) : floor);
            routeRequest.Terminals.AddRange(KsProcgenGeometry.SortCells(terminals[group.Id]));
            if (routeRequest.Terminals.Any(cell => !routeRequest.WritableCells.Contains(cell) && !routeRequest.ExistingPassageCells.Contains(cell)))
                return Fail(KsProcgenEntranceSceneStatus.Rejected, "ForbiddenGroupRouteTerminal");
            if (routeRequest.Terminals.Count > 1 && expanded >= maximumExpandedCells)
                return Fail(KsProcgenEntranceSceneStatus.BudgetExceeded, "GroupRouteExpansionBudget");
            routeRequest.MaxExpandedCells = Math.Max(1, maximumExpandedCells - expanded);
            var route = KsProcgenRoutePlanner.Connect(routeRequest);
            expanded += route.ExpandedCells;
            if (route.Status != KsProcgenRouteStatus.Connected)
                return Fail(route.Status == KsProcgenRouteStatus.BudgetExceeded ? KsProcgenEntranceSceneStatus.BudgetExceeded :
                    KsProcgenEntranceSceneStatus.Rejected, route.Status == KsProcgenRouteStatus.BudgetExceeded ?
                    "GroupRouteExpansionBudget" : "NoGroupRouteProposal");
            var newCells = route.ReservedCells.Where(cell => !floor.Contains(cell)).ToHashSet();
            foreach (var cell in newCells)
                claims.Add(cell, group.Id);
            floor.UnionWith(route.ReservedCells);
            routes.Add(new(group.Id, KsProcgenGeometry.SortCells(newCells).ToList().AsReadOnly()));
            routed++;
        }
        var scene = new KsProcgenEntranceScene(floor.ToFrozenSet(), context.CleanHostCells.ToFrozenSet(), blocked.ToFrozenSet(),
            context.HostContextComplete);
        var analysis = KsProcgenEntranceSceneAnalyzer.AnalyzeNormalized(shape, request.ConnectivityPolicy, request.RootCells,
            configurationId, scene, Math.Min(131_072, maximumExpandedCells - expanded));
        expanded += analysis.ExpandedCells;
        if (analysis.Status != KsProcgenEntranceSceneStatus.Candidate)
            return new() { Status = analysis.Status, Issue = analysis.Issue, ExpandedCells = expanded, RoutedGroups = routed };
        var walls = shape.TargetCells.Where(cell => shape.CanWrite(cell) && !floor.Contains(cell)).ToList();
        var hash = KsProcgenStableHash.Create();
        hash.AddString("ks-procgen-group-route-v1");
        hash.AddString(analysis.SceneHash.ToString(CultureInfo.InvariantCulture));
        hash.AddInt(context.PreservedContextComplete ? 1 : 0);
        foreach (var route in routes)
        {
            hash.AddString(route.GroupId);
            hash.AddInt(route.NewCells.Count);
            foreach (var cell in route.NewCells)
            {
                hash.AddInt(cell.X);
                hash.AddInt(cell.Y);
            }
        }
        return new()
        {
            Status = KsProcgenEntranceSceneStatus.Candidate, Scene = scene, Analysis = analysis, Routes = routes.AsReadOnly(),
            FloorCells = KsProcgenGeometry.SortCells(floor).ToList().AsReadOnly(),
            ProposedWallCells = KsProcgenGeometry.SortCells(walls).ToList().AsReadOnly(),
            ExpandedCells = expanded, RoutedGroups = routed, RouteHash = hash.Value,
        };
    }

    private static bool ValidCell(Vector2i cell) => Math.Abs((long) cell.X) <= 1_000_000 && Math.Abs((long) cell.Y) <= 1_000_000;
}
