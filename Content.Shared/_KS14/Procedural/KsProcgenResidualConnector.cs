using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

public enum KsProcgenResidualStatus : byte
{
    PreliminaryReady,
    InvalidInput,
    PortLandingUnavailable,
    NoRoute,
    BudgetExceeded,
}

/// <summary>
/// Preliminary connection geometry. Direct port pairs still require real door and actor validation.
/// </summary>
public sealed class KsProcgenResidualResult
{
    public KsProcgenResidualStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public IReadOnlySet<Vector2i> ReservedPassageCells { get; init; } = new HashSet<Vector2i>();
    public IReadOnlyList<(string First, string Second)> DirectPortPairs { get; init; } = [];
    public IReadOnlyList<string> UnreachablePortIds { get; init; } = [];
    public int ExpandedCells { get; init; }
}

/// <summary>
/// Binds selected room ports to procedural residual claims without excavating prefab or preserved cells.
/// Explicit existing passages must have been inspected by the caller. This is not final access validation.
/// </summary>
public static class KsProcgenResidualConnector
{
    public static KsProcgenResidualResult Connect(
        KsProcgenNormalizedShape shape,
        KsProcgenPackingResult packing,
        IReadOnlyList<Vector2i> roots,
        IReadOnlySet<Vector2i> inspectedExistingPassages,
        int maxExpandedCells = 100_000,
        bool requireAllProceduralConnected = false)
    {
        if (shape.EntranceContract != null)
            return Invalid("UnsupportedEntranceAwareResidual", "Legacy residual routing cannot consume declared entrance groups.");
        if (packing.Status != KsProcgenPackingStatus.GeometryReady ||
            maxExpandedCells <= 0 || maxExpandedCells > 1_000_000 ||
            roots.Any(root => !shape.ContainsTarget(root)) ||
            inspectedExistingPassages.Any(cell => !shape.ContainsTarget(cell)) ||
            packing.CellClaims.Count != shape.TargetCells.Count)
            return Invalid("InvalidResidualInput", "The shape, packing result, roots, or passage mask is invalid.");

        var claims = new Dictionary<Vector2i, KsProcgenCellClaim>();
        var writable = new HashSet<Vector2i>();
        foreach (var (cell, claim) in packing.CellClaims)
        {
            if (!shape.ContainsTarget(cell) || !claims.TryAdd(cell, claim))
                return Invalid("InvalidResidualClaims", "The packing result has missing or duplicate cell claims.");

            if (claim.Disposition == KsProcgenCellDisposition.ProceduralFloor)
                writable.Add(cell);
        }

        var ports = packing.Placements.SelectMany(placement => placement.Ports)
            .Concat(shape.ConstantRegions.SelectMany(region => region.Ports))
            .OrderBy(port => port.Id, StringComparer.Ordinal).ToArray();
        var portIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var port in ports)
        {
            if (!portIds.Add(port.Id) ||
                !claims.TryGetValue(port.Threshold, out var thresholdClaim) ||
                !claims.TryGetValue(port.InsideApproach, out var insideClaim) ||
                thresholdClaim.OwnerId != port.RoomId || insideClaim.OwnerId != port.RoomId)
                return Invalid("InvalidPlacedPort", $"Placed port {port.Id} does not belong to its room claims.");
        }

        var pairs = new List<(string First, string Second)>();
        var pairedIds = new HashSet<string>(StringComparer.Ordinal);
        var terminals = new List<Vector2i>(roots);
        foreach (var port in ports)
        {
            if (pairedIds.Contains(port.Id))
                continue;

            var matches = ports.Where(other =>
                other.Id != port.Id && !pairedIds.Contains(other.Id) &&
                other.Threshold == port.OutsideApproach &&
                other.OutsideApproach == port.Threshold &&
                other.OutwardNormal == -port.OutwardNormal).ToArray();
            if (matches.Length > 1)
            {
                return new KsProcgenResidualResult
                {
                    Status = KsProcgenResidualStatus.PortLandingUnavailable,
                    Issue = new KsProcgenIssue("AmbiguousPortMatch",
                        $"Port {port.Id} has more than one opposite direct partner."),
                    UnreachablePortIds = [port.Id],
                };
            }

            if (matches.Length == 1)
            {
                var match = matches[0];
                pairedIds.Add(port.Id);
                pairedIds.Add(match.Id);
                pairs.Add((port.Id, match.Id));
                continue;
            }

            if (writable.Contains(port.OutsideApproach) ||
                inspectedExistingPassages.Contains(port.OutsideApproach))
            {
                terminals.Add(port.OutsideApproach);
                continue;
            }

            return new KsProcgenResidualResult
            {
                Status = KsProcgenResidualStatus.PortLandingUnavailable,
                Issue = new KsProcgenIssue("PortLandingUnavailable",
                    $"Port {port.Id} faces a nonprocedural cell without exactly one opposite matched port."),
                UnreachablePortIds = [port.Id],
            };
        }

        if (terminals.Any(cell => !writable.Contains(cell) && !inspectedExistingPassages.Contains(cell)))
            return Invalid("RootPassageUnavailable", "A required root is not in a procedural or inspected passage cell.");

        if (requireAllProceduralConnected)
        {
            var allowed = new HashSet<Vector2i>(writable);
            allowed.UnionWith(inspectedExistingPassages);
            if (KsProcgenGeometry.ConnectedComponents(allowed).Count > 1)
            {
                return new KsProcgenResidualResult
                {
                    Status = KsProcgenResidualStatus.NoRoute,
                    Issue = new KsProcgenIssue("DisconnectedProceduralFloor",
                        "Pure procedural floor cannot form one cardinal network."),
                };
            }
        }

        if (terminals.Count == 0)
        {
            return new KsProcgenResidualResult
            {
                Status = KsProcgenResidualStatus.PreliminaryReady,
                DirectPortPairs = pairs,
            };
        }

        var route = new KsProcgenRouteRequest { MaxExpandedCells = maxExpandedCells };
        route.WritableCells.UnionWith(writable);
        route.ExistingPassageCells.UnionWith(inspectedExistingPassages);
        route.Terminals.AddRange(terminals);
        var routed = KsProcgenRoutePlanner.Connect(route);
        if (routed.Status == KsProcgenRouteStatus.Connected)
        {
            return new KsProcgenResidualResult
            {
                Status = KsProcgenResidualStatus.PreliminaryReady,
                ReservedPassageCells = routed.ReservedCells,
                DirectPortPairs = pairs,
                ExpandedCells = routed.ExpandedCells,
            };
        }

        var unreachable = new List<string>();
        if (routed.Status == KsProcgenRouteStatus.NoRoute)
        {
            var allowed = new HashSet<Vector2i>(writable);
            allowed.UnionWith(inspectedExistingPassages);
            var components = KsProcgenGeometry.ConnectedComponents(allowed);
            var rootComponent = components.FirstOrDefault(component => component.Contains(terminals[0]));
            foreach (var port in ports)
            {
                if (!pairedIds.Contains(port.Id) &&
                    (rootComponent == null || !rootComponent.Contains(port.OutsideApproach)))
                    unreachable.Add(port.Id);
            }
        }

        return new KsProcgenResidualResult
        {
            Status = routed.Status == KsProcgenRouteStatus.BudgetExceeded
                ? KsProcgenResidualStatus.BudgetExceeded
                : KsProcgenResidualStatus.NoRoute,
            Issue = routed.Issue,
            DirectPortPairs = pairs,
            UnreachablePortIds = unreachable,
            ExpandedCells = routed.ExpandedCells,
        };
    }

    private static KsProcgenResidualResult Invalid(string code, string message) => new()
    {
        Status = KsProcgenResidualStatus.InvalidInput,
        Issue = new KsProcgenIssue(code, message),
    };
}
