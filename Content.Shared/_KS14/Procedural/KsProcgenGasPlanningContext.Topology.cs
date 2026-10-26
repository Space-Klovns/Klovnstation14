using System.Globalization;
using System.Linq;
using Content.Shared.Atmos;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

public sealed record KsProcgenGasPlacement(string PrototypeId, Vector2i Cell, int QuarterTurns, KsProcgenPlacementLayer Layer);

/// <summary>Proposed initial components, never an engine atmosphere or an exterior closure certificate.</summary>
public sealed class KsProcgenGasTopologyResult
{
    internal KsProcgenGasTopologyResult(KsProcgenGasBindingStatus status, KsProcgenIssue? issue,
        IEnumerable<KsProcgenGasDomainTarget> domains, int work)
    {
        Status = status; Issue = issue; Work = work;
        Domains = domains.Select(domain => new KsProcgenGasDomainTarget(domain.Id, KsProcgenGeometry.SortCells(domain.Cells))).ToReadOnlyList();
    }
    public KsProcgenGasBindingStatus Status { get; }
    public KsProcgenIssue? Issue { get; }
    public IReadOnlyList<KsProcgenGasDomainTarget> Domains { get; }
    public int Work { get; }
}

public sealed partial class KsProcgenGasPlanningContext
{
    public KsProcgenGasTopologyResult PlanTopology(IReadOnlyList<Vector2i> terrainCells,
        IReadOnlyList<KsProcgenGasPlacement> placements, int maximumWork = 262_144)
    {
        var work = 0;
        KsProcgenGasTopologyResult Fail(KsProcgenGasBindingStatus status, string code) =>
            new(status, new(code, "Proposed gas topology could not be compiled."), [], work);
        if (terrainCells == null || placements == null || terrainCells.Count > 16_384 || placements.Count > 8_192 ||
            maximumWork is < 0 or > 1_000_000 || placements.Any(placement => placement == null) ||
            terrainCells.Any(cell => cell.X is < -1_000_000 or > 1_000_000 || cell.Y is < -1_000_000 or > 1_000_000))
            return Fail(KsProcgenGasBindingStatus.InvalidInput, "InvalidGasTopologyInput");
        bool Spend()
        {
            if (work >= maximumWork) return false;
            work++;
            return true;
        }
        var cells = terrainCells.ToHashSet();
        if (cells.Count != terrainCells.Count) return Fail(KsProcgenGasBindingStatus.InvalidInput, "DuplicateGasTerrainCell");
        var directions = new Dictionary<Vector2i, AtmosDirection>();
        var noAir = new HashSet<Vector2i>();
        string? Nominate(KsProcgenGasPlacement placement)
        {
            if (!Spend()) return "GasBindingWorkBudget";
            var (prototypeId, cell, quarterTurns, layer) = placement;
            if (!Enum.IsDefined(layer)) return "InvalidGasPlacement";
            if (string.IsNullOrWhiteSpace(prototypeId) || quarterTurns is < 0 or > 3) return "InvalidGasPlacement";
            if (!_facts.TryGetValue(prototypeId, out var fact)) return "UncapturedGasPrototype";
            if (layer is not (KsProcgenPlacementLayer.Floor or KsProcgenPlacementLayer.Wall)) return null;
            if (!cells.Contains(cell)) return "InvalidGasPlacement";
            if (fact.NoLocalRotation && quarterTurns != 0) return "GasPlacementRotationForbidden";
            if (!fact.Anchored) return null;
            var rotated = AtmosDirection.Invalid;
            foreach (var (_, direction) in Cardinal)
            {
                if ((fact.BlockedDirections & direction) == 0) continue;
                rotated |= (direction.ToAngle() + KsProcgenRotation.ToEngineAngle(quarterTurns)).ToAtmosDirectionCardinal();
            }
            directions[cell] = directions.GetValueOrDefault(cell) | rotated;
            if (fact.NoAirWhenFullyBlocked) noAir.Add(cell);
            return null;
        }
        KsProcgenGasTopologyResult PlacementFailure(string code) =>
            Fail(code == "GasBindingWorkBudget" ? KsProcgenGasBindingStatus.BudgetExceeded : KsProcgenGasBindingStatus.Rejected, code);
        foreach (var placement in placements)
            if (Nominate(placement) is { } code) return PlacementFailure(code);
        var remaining = new HashSet<Vector2i>();
        foreach (var cell in cells)
        {
            if (!Spend()) return PlacementFailure("GasBindingWorkBudget");
            if (directions.GetValueOrDefault(cell) != AtmosDirection.All || !noAir.Contains(cell)) remaining.Add(cell);
        }
        var domains = new List<KsProcgenGasDomainTarget>();
        foreach (var root in KsProcgenGeometry.SortCells(cells))
        {
            if (!Spend()) return PlacementFailure("GasBindingWorkBudget");
            if (!remaining.Remove(root)) continue;
            var domainCells = new List<Vector2i>(); var pending = new Queue<Vector2i>(); pending.Enqueue(root);
            while (pending.TryDequeue(out var cell))
            {
                domainCells.Add(cell);
                foreach (var (offset, direction) in Cardinal)
                {
                    if (!Spend()) return PlacementFailure("GasBindingWorkBudget");
                    var neighbor = cell + offset;
                    if ((directions.GetValueOrDefault(cell) & direction) != 0 ||
                        (directions.GetValueOrDefault(neighbor) & direction.GetOpposite()) != 0 || !remaining.Remove(neighbor)) continue;
                    pending.Enqueue(neighbor);
                }
            }
            var id = "Gas/" + root.X.ToString(CultureInfo.InvariantCulture) + "/" + root.Y.ToString(CultureInfo.InvariantCulture);
            domains.Add(new(id, domainCells));
        }
        return new(KsProcgenGasBindingStatus.Bound, null, domains, work);
    }
}
