using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

/// <summary>Value-comparable cardinal path, ordered from the subject landing to the target landing.</summary>
public readonly struct KsProcgenRelationPath : IEquatable<KsProcgenRelationPath>
{
    private readonly Vector2i[]? _cells;
    public IReadOnlyList<Vector2i> Cells => _cells ?? [];

    public KsProcgenRelationPath(IEnumerable<Vector2i> cells)
    {
        _cells = cells.ToArray();
    }

    public bool Equals(KsProcgenRelationPath other) => Cells.SequenceEqual(other.Cells);
    public override bool Equals(object? obj) => obj is KsProcgenRelationPath other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var cell in Cells)
            hash.Add(cell);
        return hash.ToHashCode();
    }
}

/// <summary>Shared node expansion budget across a room's relation evaluations and candidate branches.</summary>
public sealed class KsProcgenRelationPathBudget
{
    public int MaximumExpandedCells { get; }
    public int ExpandedCells { get; private set; }
    public bool Truncated { get; private set; }

    public KsProcgenRelationPathBudget(int maximumExpandedCells = 4096)
    {
        if (maximumExpandedCells is < 0 or > 65_536)
            throw new ArgumentOutOfRangeException(nameof(maximumExpandedCells));
        MaximumExpandedCells = maximumExpandedCells;
    }

    public bool Expand()
    {
        if (ExpandedCells >= MaximumExpandedCells)
        {
            Truncated = true;
            return false;
        }
        ExpandedCells++;
        return true;
    }
}
