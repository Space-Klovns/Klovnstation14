using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

public sealed record KsProcgenLvSquareProof(Vector2i Minimum, IReadOnlyList<Vector2i> MandatoryTerminals,
    ulong CandidateDomainHash, ulong TerminalHash);

/// <summary>
/// Transactional shortest cardinal branches for a fixed occupied wire set. Whole-layout/device
/// ordering remains heuristic; a failed join never certifies square necessity. The supported
/// exception is four fixed mandatory terminals.
/// </summary>
public sealed class KsProcgenLowVoltageRouter
{
    public const int AlgorithmVersion = 2;
    private static readonly Vector2i[] Cardinal = [new(0, -1), new(-1, 0), new(1, 0), new(0, 1)];
    private readonly HashSet<Vector2i> _allowed;
    private readonly HashSet<Vector2i> _terminals;
    private readonly HashSet<Vector2i> _wires;
    private readonly KsProcgenElectricalSquarePolicy _policy;
    private readonly Func<bool> _spend;
    public ulong CandidateDomainHash { get; }
    public ulong TerminalHash { get; }
    public IReadOnlyList<Vector2i> Cells => KsProcgenGeometry.SortCells(_wires);

    private KsProcgenLowVoltageRouter(IReadOnlySet<Vector2i> allowed, IReadOnlySet<Vector2i> terminals,
        KsProcgenElectricalSquarePolicy policy, Func<bool> spend)
    {
        _allowed = allowed.ToHashSet(); _terminals = terminals.ToHashSet(); _wires = terminals.ToHashSet();
        _policy = policy; _spend = spend;
        CandidateDomainHash = CellHash(_allowed); TerminalHash = CellHash(_terminals);
    }

    public static bool TryCreate(IReadOnlySet<Vector2i> allowed, IReadOnlySet<Vector2i> terminals,
        KsProcgenElectricalSquarePolicy policy, Func<bool> spend, out KsProcgenLowVoltageRouter? router, out string? issue)
    {
        router = null; issue = null;
        if (allowed == null || terminals == null || spend == null || allowed.Count is < 1 or > 65_536 ||
            terminals.Count is < 1 or > 256 || !terminals.IsSubsetOf(allowed) || !Enum.IsDefined(policy))
        { issue = "InvalidLowVoltageRoutingInput"; return false; }
        foreach (var cell in allowed)
        {
            if (!spend()) { issue = "ElectricalRoutingWorkBudget"; return false; }
            if (Math.Abs((long) cell.X) > 1_000_000 || Math.Abs((long) cell.Y) > 1_000_000)
            { issue = "InvalidLowVoltageRoutingInput"; return false; }
        }
        var proposed = new KsProcgenLowVoltageRouter(allowed, terminals, policy, spend);
        foreach (var minimum in SquareMinima(terminals))
        {
            if (!spend()) { issue = "ElectricalRoutingWorkBudget"; return false; }
            if (policy == KsProcgenElectricalSquarePolicy.ForbidNew && Square(minimum).All(terminals.Contains))
            { issue = "ElectricalLvSquareForbidden"; return false; }
        }
        router = proposed; return true;
    }

    public bool TryJoin(IReadOnlySet<Vector2i> targets, out string? issue)
    {
        issue = null;
        if (targets == null || targets.Count > 65_536)
        { issue = "InvalidLowVoltageRoutingInput"; return false; }
        if (targets.Count == 0) { issue = "ElectricalLvRouteCandidateUnavailable"; return false; }
        var previous = new Dictionary<Vector2i, Vector2i>(); var queue = new Queue<Vector2i>();
        foreach (var cell in KsProcgenGeometry.SortCells(_wires))
        {
            if (!_spend()) { issue = "ElectricalRoutingWorkBudget"; return false; }
            previous.Add(cell, cell); queue.Enqueue(cell);
        }
        while (queue.TryDequeue(out var cell))
        {
            if (!_spend()) { issue = "ElectricalRoutingWorkBudget"; return false; }
            if (targets.Contains(cell))
            {
                var path = new List<Vector2i>(); var proposed = new HashSet<Vector2i>(); var current = cell;
                while (true)
                {
                    if (!_spend()) { issue = "ElectricalRoutingWorkBudget"; return false; }
                    path.Add(current); proposed.Add(current);
                    var parent = previous[current]; if (parent == current) break; current = parent;
                }
                // A shortest path has no cycle/chord, so four new corners cannot form a square.
                // With one or two old corners, every new corner touching one is already a BFS
                // seed neighbor; a shortest branch cannot visit multiple such distance-one cells.
                // Three old corners are rejected below. Recheck the whole branch independently
                // before committing; this invariant never permits a heuristic square exception.
                foreach (var added in path)
                {
                    if (_wires.Contains(added)) continue;
                    foreach (var minimum in SquareMinima(new[] { added }))
                    {
                        var complete = true;
                        foreach (var point in Square(minimum))
                        {
                            if (!_spend()) { issue = "ElectricalRoutingWorkBudget"; return false; }
                            if (!_wires.Contains(point) && !proposed.Contains(point)) { complete = false; break; }
                        }
                        if (complete) { issue = "ElectricalLvRouteCandidateUnavailable"; return false; }
                    }
                }
                // All fallible work completes before publishing any part of the branch.
                foreach (var point in path) _wires.Add(point);
                return true;
            }
            foreach (var offset in Cardinal)
            {
                if (!_spend()) { issue = "ElectricalRoutingWorkBudget"; return false; }
                var neighbor = cell + offset;
                if (!_allowed.Contains(neighbor) || previous.ContainsKey(neighbor)) continue;
                var legal = true;
                foreach (var minimum in SquareMinima(new[] { neighbor }))
                {
                    var complete = true;
                    foreach (var point in Square(minimum))
                    {
                        if (point == neighbor) continue;
                        if (!_spend()) { issue = "ElectricalRoutingWorkBudget"; return false; }
                        if (!_wires.Contains(point)) { complete = false; break; }
                    }
                    if (complete) { legal = false; break; }
                }
                if (!legal) continue;
                previous.Add(neighbor, cell); queue.Enqueue(neighbor);
            }
        }
        issue = "ElectricalLvRouteCandidateUnavailable"; return false;
    }

    public bool TrySquareProofs(out IReadOnlyList<KsProcgenLvSquareProof> proofs, out string? issue)
    {
        proofs = []; issue = null; var result = new List<KsProcgenLvSquareProof>();
        foreach (var minimum in SquareMinima(_wires))
        {
            if (!_spend()) { issue = "ElectricalRoutingWorkBudget"; return false; }
            var block = Square(minimum); if (!block.All(_wires.Contains)) continue;
            if (_policy == KsProcgenElectricalSquarePolicy.ForbidNew || !block.All(_terminals.Contains))
            { issue = "ElectricalLvSquareProofRequired"; return false; }
            result.Add(new(minimum, KsProcgenGeometry.SortCells(block), CandidateDomainHash, TerminalHash));
        }
        proofs = KsProcgenReadOnly.Copy(result.OrderBy(proof => proof.Minimum.Y).ThenBy(proof => proof.Minimum.X));
        return true;
    }

    internal static IEnumerable<Vector2i> SquareMinima(IEnumerable<Vector2i> cells) => cells.SelectMany(cell =>
        new[] { cell, cell + new Vector2i(-1, 0), cell + new Vector2i(0, -1), cell + new Vector2i(-1, -1) }).Distinct();
    internal static Vector2i[] Square(Vector2i minimum) => [minimum, minimum + new Vector2i(1, 0), minimum + new Vector2i(0, 1), minimum + Vector2i.One];
    internal static ulong CellHash(IEnumerable<Vector2i> cells)
    {
        var hash = KsProcgenStableHash.Create(); hash.AddString("ks-electrical-cell-domain-v1");
        var ordered = KsProcgenGeometry.SortCells(cells); hash.AddInt(ordered.Count);
        foreach (var cell in ordered) { hash.AddInt(cell.X); hash.AddInt(cell.Y); }
        return hash.Value;
    }
}
