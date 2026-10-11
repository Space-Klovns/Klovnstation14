using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

/// <summary>Value-comparable cardinal path, ordered from the subject landing to the target landing.</summary>
public readonly struct KsProcgenRelationPath : IEquatable<KsProcgenRelationPath>
{
    private readonly IReadOnlyList<Vector2i>? _cells;
    public IReadOnlyList<Vector2i> Cells => _cells ?? [];

    public KsProcgenRelationPath(IEnumerable<Vector2i> cells)
    {
        _cells = KsProcgenReadOnly.Copy(cells);
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

/// <summary>
/// Shared node expansion budget across a room's relation evaluations and candidate branches.
/// Opt-in clean passage accounting also charges facing, chair access and walkway reservations.
/// </summary>
public sealed class KsProcgenRelationPathBudget
{
    public int MaximumExpandedCells { get; }
    public int ExpandedCells { get; private set; }
    public bool Truncated { get; private set; }
    public bool MeterCleanPaths { get; }
    public int CleanPathExpandedCells { get; private set; }
    public int RelationExpandedCells => ExpandedCells - CleanPathExpandedCells;
    public bool CleanPathSearchTruncated { get; private set; }
    public bool RelationPathSearchTruncated { get; private set; }

    public KsProcgenRelationPathBudget(int maximumExpandedCells = 4096, bool meterCleanPaths = false)
    {
        if (maximumExpandedCells is < 0 or > 65_536)
            throw new ArgumentOutOfRangeException(nameof(maximumExpandedCells));
        MaximumExpandedCells = maximumExpandedCells;
        MeterCleanPaths = meterCleanPaths;
    }

    public bool Expand()
    {
        if (ExpandCore())
            return true;
        RelationPathSearchTruncated = true;
        return false;
    }

    private bool ExpandCore()
    {
        if (ExpandedCells >= MaximumExpandedCells)
        {
            Truncated = true;
            return false;
        }
        ExpandedCells++;
        return true;
    }

    public bool ExpandCleanPath()
    {
        if (!MeterCleanPaths)
            return true;
        if (!ExpandCore())
        {
            CleanPathSearchTruncated = true;
            return false;
        }
        CleanPathExpandedCells++;
        return true;
    }
}
