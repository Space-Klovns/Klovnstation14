using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

/// <summary>
/// Shared partition work across networks and failed merge attempts. Graph work counts dequeued
/// cells; seam probes count ownership cells, cardinal interface queries, interface edges, door
/// candidates and accepted door approach checks. Allocation and sorting are bounded separately.
/// </summary>
public sealed class KsProcgenPartitionWorkBudget
{
    private static readonly Vector2i[] Cardinal = [new(1, 0), new(0, 1), new(-1, 0), new(0, -1)];

    public int MaximumExpandedCells { get; }
    public int MaximumSeamProbes { get; }
    public int ExpandedCells { get; private set; }
    public int SeamProbes { get; private set; }
    public string? ExhaustedCode { get; private set; }

    public KsProcgenPartitionWorkBudget(int maximumExpandedCells = 262_144, int maximumSeamProbes = 262_144)
    {
        if (maximumExpandedCells is < 0 or > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(maximumExpandedCells));
        if (maximumSeamProbes is < 0 or > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(maximumSeamProbes));
        MaximumExpandedCells = maximumExpandedCells;
        MaximumSeamProbes = maximumSeamProbes;
    }

    internal bool Probe()
    {
        if (ExhaustedCode != null)
            return false;
        if (SeamProbes >= MaximumSeamProbes)
        {
            ExhaustedCode = "PartitionSeamProbeBudget";
            return false;
        }
        SeamProbes++;
        return true;
    }

    internal bool TryConnectedComponents(IReadOnlySet<Vector2i> cells,
        out IReadOnlyList<IReadOnlyList<Vector2i>> components)
    {
        components = [];
        if (ExhaustedCode != null)
            return false;
        var result = new List<IReadOnlyList<Vector2i>>();
        var visited = new HashSet<Vector2i>();
        foreach (var start in KsProcgenGeometry.SortCells(cells))
        {
            if (!visited.Add(start))
                continue;
            var component = new List<Vector2i>();
            var queue = new Queue<Vector2i>();
            queue.Enqueue(start);
            while (queue.TryDequeue(out var cell))
            {
                if (ExpandedCells >= MaximumExpandedCells)
                {
                    ExhaustedCode = "PartitionExpansionBudget";
                    return false;
                }
                ExpandedCells++;
                component.Add(cell);
                foreach (var offset in Cardinal)
                {
                    var next = cell + offset;
                    if (cells.Contains(next) && visited.Add(next))
                        queue.Enqueue(next);
                }
            }
            result.Add(KsProcgenGeometry.SortCells(component));
        }
        components = KsProcgenReadOnly.Copy(result);
        return true;
    }
}
