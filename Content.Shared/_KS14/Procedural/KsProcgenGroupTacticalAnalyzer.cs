using System.Collections.Frozen;
using System.Globalization;
using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

public sealed record KsProcgenOwnedTacticalNetwork(int Component, IReadOnlyList<string> GroupIds,
    IReadOnlyList<Vector2i> RequiredTerminals, KsProcgenTacticalResult Movement,
    KsProcgenAlternateRouteResult AlternateRoutes, KsProcgenRouteRedundancyResult? Redundancy);

public sealed class KsProcgenGroupTacticalResult
{
    public KsProcgenTacticalStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public IReadOnlyList<KsProcgenOwnedTacticalNetwork> Networks { get; init; } = [];
    public int ExpandedCells { get; init; }
    public int Searches { get; init; }
    public int ExaminedFlowEdges { get; init; }
    public ulong MeasurementHash { get; init; }
    // Host paths are checked for contract isolation, but are never tactical detours.
    public KsProcgenEntranceIsolationScope MeasurementScope => KsProcgenEntranceIsolationScope.DomainLocal;
    public bool EngineTraversalVerified => false;
}

/// <summary>
/// Optional movement measurements on independently revalidated post-interior local networks.
/// Every query stays inside one permitted component, including when outside host paths connect
/// otherwise isolated local groups. This does not measure vision, projectile cover or live actors.
/// </summary>
public static class KsProcgenGroupTacticalAnalyzer
{
    public static KsProcgenGroupTacticalResult Analyze(KsProcgenRequest request,
        KsProcgenGroupInteriorResult interior, IReadOnlyList<KsProcgenRoutePair> pairs,
        IReadOnlySet<Vector2i> nominatedChokes, bool measureRedundancy = false,
        int maximumDetailedChokes = 128, int maximumSearches = 4_096,
        int maximumExpandedCells = 1_000_000, int maximumExaminedFlowEdges = 1_000_000,
        int maximumIndependentRoutes = 4)
    {
        var expanded = 0;
        var searches = 0;
        var examined = 0;
        KsProcgenGroupTacticalResult Fail(KsProcgenTacticalStatus status, string code) => new()
        {
            Status = status, Issue = new(code, "Owned tactical measurements did not complete."),
            ExpandedCells = expanded, Searches = searches, ExaminedFlowEdges = examined,
        };
        if (!KsProcgenGeometry.TryNormalize(request, out var shape, out var issue))
            return new() { Status = KsProcgenTacticalStatus.InvalidInput, Issue = issue };
        if (shape!.EntranceContract == null || interior == null ||
            interior.Status != KsProcgenEntranceSceneStatus.Candidate || interior.Scene == null ||
            interior.Analysis?.Status != KsProcgenEntranceSceneStatus.Candidate || interior.AccessHash == 0 ||
            interior.Scene.CleanDomainCells.Count is < 1 or > 4_096 || pairs == null || nominatedChokes == null ||
            pairs.Count > 64 || nominatedChokes.Count > 64 || maximumDetailedChokes is < 0 or > 512 ||
            maximumSearches is < 0 or > 65_536 || maximumExpandedCells is < 0 or > 1_000_000 ||
            maximumExaminedFlowEdges is < 0 or > 8_000_000 || maximumIndependentRoutes is < 1 or > 8)
            return Fail(KsProcgenTacticalStatus.InvalidInput, "InvalidGroupTacticalInput");

        var analysis = KsProcgenEntranceSceneAnalyzer.AnalyzeNormalized(shape, request.ConnectivityPolicy,
            request.RootCells, interior.Analysis.ConfigurationId!, interior.Scene, Math.Min(131_072, maximumExpandedCells));
        expanded += analysis.ExpandedCells;
        if (analysis.Status != KsProcgenEntranceSceneStatus.Candidate)
            return Fail(analysis.Status == KsProcgenEntranceSceneStatus.BudgetExceeded
                ? KsProcgenTacticalStatus.BudgetExceeded : KsProcgenTacticalStatus.InvalidInput, analysis.Issue!.Code);
        if (analysis.SceneHash != interior.Analysis.SceneHash)
            return Fail(KsProcgenTacticalStatus.InvalidInput, "StaleGroupTacticalSource");
        if (analysis.Components.Count > 64)
            return Fail(KsProcgenTacticalStatus.InvalidInput, "GroupTacticalNetworkLimit");

        var ownerByCell = new Dictionary<Vector2i, int>();
        foreach (var component in analysis.Components)
        foreach (var cell in component.Cells)
            ownerByCell.Add(cell, component.Component);
        if (pairs.Any(pair => pair == null || string.IsNullOrWhiteSpace(pair.Id) || pair.First == pair.Second ||
            !ownerByCell.ContainsKey(pair.First) || !ownerByCell.ContainsKey(pair.Second)) ||
            pairs.Select(pair => pair.Id).Distinct(StringComparer.Ordinal).Count() != pairs.Count ||
            nominatedChokes.Any(cell => !ownerByCell.ContainsKey(cell)))
            return Fail(KsProcgenTacticalStatus.InvalidInput, "InvalidOwnedTacticalQuery");
        if (pairs.Any(pair => ownerByCell[pair.First] != ownerByCell[pair.Second]))
            return Fail(KsProcgenTacticalStatus.InvalidInput, "CrossNetworkTacticalPair");

        var configuration = shape.EntranceContract.Configurations.Single(candidate => candidate.Id == analysis.ConfigurationId);
        var operationalPorts = configuration.Groups.SelectMany(group => group.Ports).ToHashSet(StringComparer.Ordinal);
        // Side counts name entrance approaches and declared roots; they do not count every floor tile
        // reserved by furnishing, nor claim to enumerate all engine interaction targets.
        var terminals = shape.Entrances.Where(port => operationalPorts.Contains(port.PortId))
            .SelectMany(port => port.InsideApproach).Concat(configuration.Groups.SelectMany(group => group.RootCells))
            .Concat(request.RootCells).ToHashSet();
        var networks = new List<KsProcgenOwnedTacticalNetwork>();
        var details = 0;
        var truncated = false;
        foreach (var component in analysis.Components.OrderBy(component => component.Component))
        {
            var cells = component.Cells.ToFrozenSet();
            var localTerminals = terminals.Where(cells.Contains).ToFrozenSet();
            var movement = KsProcgenTacticalAnalyzer.Analyze(cells, FrozenSet<Vector2i>.Empty, localTerminals,
                maxCells: 4_096, maxDetailedChokes: maximumDetailedChokes - details,
                maxExpandedCells: maximumExpandedCells - expanded);
            expanded += movement.ExpandedCells;
            if (movement.Status is KsProcgenTacticalStatus.InvalidInput or KsProcgenTacticalStatus.BudgetExceeded)
                return Fail(movement.Status, movement.Issue!.Code);
            details += movement.ChokeDetails.Count;
            truncated |= movement.Status == KsProcgenTacticalStatus.DetailsTruncated;
            var localPairs = pairs.Where(pair => ownerByCell[pair.First] == component.Component)
                .OrderBy(pair => pair.Id, StringComparer.Ordinal).ToArray();
            var chokes = nominatedChokes.Where(cells.Contains).ToFrozenSet();
            var alternate = KsProcgenAlternateRouteAnalyzer.Analyze(cells, FrozenSet<Vector2i>.Empty, localPairs, chokes,
                maxCells: 4_096, maxSearches: maximumSearches - searches,
                maxExpandedCells: maximumExpandedCells - expanded);
            expanded += alternate.ExpandedCells;
            searches += alternate.Searches;
            if (alternate.Status != KsProcgenAlternateRouteStatus.Complete)
                return Fail(alternate.Status == KsProcgenAlternateRouteStatus.BudgetExceeded
                    ? KsProcgenTacticalStatus.BudgetExceeded : KsProcgenTacticalStatus.InvalidInput, alternate.Issue!.Code);
            KsProcgenRouteRedundancyResult? redundancy = null;
            if (measureRedundancy)
            {
                redundancy = KsProcgenRouteRedundancyAnalyzer.Analyze(cells, FrozenSet<Vector2i>.Empty, localPairs,
                    maxCells: 4_096, maxPairs: 64, maxIndependentRoutes: maximumIndependentRoutes,
                    maxExaminedFlowEdges: maximumExaminedFlowEdges - examined);
                examined += redundancy.ExaminedFlowEdges;
                if (redundancy.Status != KsProcgenRouteRedundancyStatus.Complete)
                    return Fail(redundancy.Status == KsProcgenRouteRedundancyStatus.BudgetExceeded
                        ? KsProcgenTacticalStatus.BudgetExceeded : KsProcgenTacticalStatus.InvalidInput, redundancy.Issue!.Code);
            }
            networks.Add(new(component.Component, KsProcgenReadOnly.Copy(component.GroupIds),
                KsProcgenGeometry.SortCells(localTerminals).ToReadOnlyList(), movement, alternate, redundancy));
        }

        var hash = KsProcgenStableHash.Create();
        hash.AddString("ks-procgen-group-tactical-v1");
        hash.AddString(analysis.SceneHash.ToString(CultureInfo.InvariantCulture));
        hash.AddInt(measureRedundancy ? 1 : 0);
        hash.AddInt(measureRedundancy ? maximumIndependentRoutes : 0);
        // Actual selected detail cells encode truncation without hashing unused budget capacity.
        foreach (var network in networks)
        {
            hash.AddInt(network.Component);
            hash.AddInt(network.Movement.ChokeDetails.Count);
            foreach (var detail in network.Movement.ChokeDetails) AddCell(detail.Cell);
        }
        hash.AddInt(pairs.Count);
        foreach (var pair in pairs.OrderBy(pair => pair.Id, StringComparer.Ordinal))
        {
            hash.AddString(pair.Id); AddCell(pair.First); AddCell(pair.Second);
        }
        hash.AddInt(nominatedChokes.Count);
        foreach (var choke in KsProcgenGeometry.SortCells(nominatedChokes)) AddCell(choke);
        void AddCell(Vector2i cell) { hash.AddInt(cell.X); hash.AddInt(cell.Y); }
        return new()
        {
            Status = truncated ? KsProcgenTacticalStatus.DetailsTruncated : KsProcgenTacticalStatus.Complete,
            Issue = truncated ? new("TacticalDetailsTruncated", "All local chokes were measured; side details reached the shared cap.") : null,
            Networks = KsProcgenReadOnly.Copy(networks), ExpandedCells = expanded, Searches = searches,
            ExaminedFlowEdges = examined, MeasurementHash = hash.Value,
        };
    }
}
