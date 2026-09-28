using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

/// <summary>
/// Exact normalized ownership masks. Public cell lists are sorted and detached from the request.
/// </summary>
public sealed class KsProcgenNormalizedShape
{
    private readonly HashSet<Vector2i> _targetSet;
    private readonly HashSet<Vector2i> _envelopeSet;
    private readonly HashSet<Vector2i> _preservedSet;
    private readonly HashSet<Vector2i> _voidSet;

    public IReadOnlyList<Vector2i> TargetCells { get; }
    public IReadOnlyList<Vector2i> EnvelopeCells { get; }
    public IReadOnlyList<Vector2i> PreservedCells { get; }
    public IReadOnlyList<Vector2i> VoidCells { get; }

    internal KsProcgenNormalizedShape(
        HashSet<Vector2i> targetSet,
        HashSet<Vector2i> envelopeSet,
        HashSet<Vector2i> preservedSet,
        HashSet<Vector2i> voidSet)
    {
        _targetSet = new HashSet<Vector2i>(targetSet);
        _envelopeSet = new HashSet<Vector2i>(envelopeSet);
        _preservedSet = new HashSet<Vector2i>(preservedSet);
        _voidSet = new HashSet<Vector2i>(voidSet);
        TargetCells = KsProcgenGeometry.SortCells(_targetSet);
        EnvelopeCells = KsProcgenGeometry.SortCells(_envelopeSet);
        PreservedCells = KsProcgenGeometry.SortCells(_preservedSet);
        VoidCells = KsProcgenGeometry.SortCells(_voidSet);
    }

    public bool ContainsTarget(Vector2i cell) => _targetSet.Contains(cell);
    public bool ContainsEnvelope(Vector2i cell) => _envelopeSet.Contains(cell);
    public bool ContainsPreserved(Vector2i cell) => _preservedSet.Contains(cell);
    public bool ContainsVoid(Vector2i cell) => _voidSet.Contains(cell);
    public bool CanWrite(Vector2i cell) =>
        (_targetSet.Contains(cell) || _envelopeSet.Contains(cell)) && !_preservedSet.Contains(cell);

    public IReadOnlyList<IReadOnlyList<Vector2i>> TargetComponents() =>
        KsProcgenGeometry.ConnectedComponents(_targetSet);
}

public static class KsProcgenGeometry
{
    private static readonly Vector2i[] CardinalOffsets =
    [
        new(1, 0),
        new(0, 1),
        new(-1, 0),
        new(0, -1),
    ];

    /// <summary>
    /// Normalizes all supported shape inputs without allocating their bounding rectangle.
    /// Every failure has a stable code and leaves no partial shape in the output.
    /// </summary>
    public static bool TryNormalize(
        KsProcgenRequest request,
        out KsProcgenNormalizedShape? shape,
        out KsProcgenIssue? issue)
    {
        shape = null;
        issue = null;

        if (request == null || string.IsNullOrWhiteSpace(request.RequestId) || request.Shape == null || request.Limits == null)
        {
            issue = new KsProcgenIssue("InvalidRequest", "A request ID, shape, and limits are required.");
            return false;
        }

        var limits = request.Limits;
        if (limits.MaxCells <= 0 || limits.MaxCells > 65_536 ||
            limits.MaxAbsoluteCoordinate <= 0 || limits.MaxAbsoluteCoordinate > 1_000_000)
        {
            issue = new KsProcgenIssue("InvalidLimits", "Limits must be positive and within the supported cap.");
            return false;
        }

        if (!Enum.IsDefined(request.Mode) ||
            !Enum.IsDefined(request.GeometryMode) ||
            !Enum.IsDefined(request.ConnectivityPolicy))
        {
            issue = new KsProcgenIssue("InvalidPolicy", "The request contains an unknown mode or geometry policy.");
            return false;
        }

        var spec = request.Shape;
        if (spec.Cells == null || spec.AddRectangles == null || spec.SubtractRectangles == null ||
            spec.EnvelopeCells == null || spec.PreservedCells == null || spec.VoidCells == null ||
            request.RootCells == null)
        {
            issue = new KsProcgenIssue("InvalidShape", "Shape and root cell lists cannot be null.");
            return false;
        }

        var targetSet = new HashSet<Vector2i>();
        var envelopeSet = new HashSet<Vector2i>();
        var preservedSet = new HashSet<Vector2i>();
        var voidSet = new HashSet<Vector2i>();

        foreach (var cell in spec.Cells)
        {
            if (!TryAdd(targetSet, cell, limits, out issue))
                return false;
        }

        foreach (var rect in spec.AddRectangles)
        {
            if (!TryValidateRect(rect, limits, out issue))
                return false;

            for (var y = rect.Min.Y; y < rect.Max.Y; y++)
            for (var x = rect.Min.X; x < rect.Max.X; x++)
            {
                if (!TryAdd(targetSet, new Vector2i(x, y), limits, out issue))
                    return false;
            }
        }

        if (spec.TextMask != null && !TryParseText(spec, limits, targetSet, envelopeSet,
                preservedSet, voidSet, out issue))
            return false;

        foreach (var rect in spec.SubtractRectangles)
        {
            if (!TryValidateRect(rect, limits, out issue))
                return false;

            targetSet.RemoveWhere(cell => cell.X >= rect.Min.X && cell.X < rect.Max.X &&
                                          cell.Y >= rect.Min.Y && cell.Y < rect.Max.Y);
        }

        foreach (var cell in spec.EnvelopeCells)
        {
            if (!TryAdd(envelopeSet, cell, limits, out issue))
                return false;
        }

        foreach (var cell in spec.PreservedCells)
        {
            if (!TryAdd(preservedSet, cell, limits, out issue))
                return false;
        }

        foreach (var cell in spec.VoidCells)
        {
            if (!TryAdd(voidSet, cell, limits, out issue))
                return false;
        }

        foreach (var rootCell in request.RootCells)
        {
            if (!WithinLimit(rootCell, limits.MaxAbsoluteCoordinate))
            {
                issue = new KsProcgenIssue("CoordinateOutOfRange", $"Root {rootCell} exceeds the coordinate limit.");
                return false;
            }
        }

        if (targetSet.Overlaps(envelopeSet) || targetSet.Overlaps(voidSet) || envelopeSet.Overlaps(voidSet))
        {
            issue = new KsProcgenIssue("ConflictingMasks", "Target, envelope, and void must be disjoint.");
            return false;
        }

        if (!preservedSet.IsSubsetOf(targetSet))
        {
            issue = new KsProcgenIssue("PreservedOutsideTarget", "Preserved cells must belong to the target.");
            return false;
        }

        var allSet = new HashSet<Vector2i>(targetSet);
        allSet.UnionWith(envelopeSet);
        allSet.UnionWith(voidSet);
        if (allSet.Count > limits.MaxCells)
        {
            issue = new KsProcgenIssue("CellBudgetExceeded", "The normalized shape exceeds its cell budget.");
            return false;
        }

        if (targetSet.Count == 0 && request.RootCells.Count != 0)
        {
            issue = new KsProcgenIssue("EmptyTargetWithRoots", "An empty target cannot have required roots.");
            return false;
        }

        shape = new KsProcgenNormalizedShape(targetSet, envelopeSet, preservedSet, voidSet);
        return true;
    }

    /// <summary>
    /// Clockwise quarter turns in the authoring convention, about a named cell anchor.
    /// </summary>
    public static Vector2i TransformCell(Vector2i cell, Vector2i sourceAnchor, Vector2i targetAnchor, int quarterTurns)
    {
        var relativeX = (long) cell.X - sourceAnchor.X;
        var relativeY = (long) cell.Y - sourceAnchor.Y;
        var turns = ((quarterTurns % 4) + 4) % 4;
        var transformed = turns switch
        {
            0 => (X: relativeX, Y: relativeY),
            1 => (X: relativeY, Y: -relativeX),
            2 => (X: -relativeX, Y: -relativeY),
            _ => (X: -relativeY, Y: relativeX),
        };

        return new Vector2i(
            checked((int) (targetAnchor.X + transformed.X)),
            checked((int) (targetAnchor.Y + transformed.Y)));
    }

    public static IReadOnlyList<IReadOnlyList<Vector2i>> ConnectedComponents(IReadOnlySet<Vector2i> cells)
    {
        var unvisitedSet = new HashSet<Vector2i>(cells);
        var components = new List<IReadOnlyList<Vector2i>>();
        foreach (var startCell in SortCells(cells))
        {
            if (!unvisitedSet.Remove(startCell))
                continue;

            var queue = new Queue<Vector2i>();
            var component = new HashSet<Vector2i> { startCell };
            queue.Enqueue(startCell);
            while (queue.TryDequeue(out var cell))
            {
                foreach (var offset in CardinalOffsets)
                {
                    var neighbor = cell + offset;
                    if (!unvisitedSet.Remove(neighbor))
                        continue;

                    component.Add(neighbor);
                    queue.Enqueue(neighbor);
                }
            }

            components.Add(SortCells(component));
        }

        return components;
    }

    internal static IReadOnlyList<Vector2i> SortCells(IEnumerable<Vector2i> cells) =>
        Array.AsReadOnly(cells.OrderBy(cell => cell.Y).ThenBy(cell => cell.X).ToArray());

    private static bool TryParseText(
        KsProcgenShapeSpec spec,
        KsProcgenLimits limits,
        HashSet<Vector2i> targetSet,
        HashSet<Vector2i> envelopeSet,
        HashSet<Vector2i> preservedSet,
        HashSet<Vector2i> voidSet,
        out KsProcgenIssue? issue)
    {
        issue = null;
        var lines = spec.TextMask!.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        var width = lines[0].Length;
        if (width == 0 || lines.Any(line => line.Length != width) || (long) width * lines.Length > limits.MaxCells)
        {
            issue = new KsProcgenIssue("InvalidTextMask", "Text rows must have one nonzero width within the cell budget.");
            return false;
        }

        for (var row = 0; row < lines.Length; row++)
        for (var x = 0; x < width; x++)
        {
            var marker = lines[row][x];
            if (marker == ' ')
                continue;

            var positionX = (long) spec.TextOrigin.X + x;
            var positionY = (long) spec.TextOrigin.Y + lines.Length - row - 1;
            if (positionX < -limits.MaxAbsoluteCoordinate || positionX > limits.MaxAbsoluteCoordinate ||
                positionY < -limits.MaxAbsoluteCoordinate || positionY > limits.MaxAbsoluteCoordinate)
            {
                issue = new KsProcgenIssue("CoordinateOutOfRange", "A text-mask cell exceeds the coordinate limit.");
                return false;
            }

            var cell = new Vector2i((int) positionX, (int) positionY);
            switch (marker)
            {
                case '#':
                    targetSet.Add(cell);
                    break;
                case 'P':
                    targetSet.Add(cell);
                    preservedSet.Add(cell);
                    break;
                case 'E':
                    envelopeSet.Add(cell);
                    break;
                case 'V':
                    voidSet.Add(cell);
                    break;
                default:
                    issue = new KsProcgenIssue("InvalidTextMask", $"Unknown text-mask marker '{marker}' at row {row}, column {x}.");
                    return false;
            }
        }

        return true;
    }

    private static bool TryValidateRect(KsProcgenTileRect? rect, KsProcgenLimits limits, out KsProcgenIssue? issue)
    {
        issue = null;
        if (rect == null || rect.Min.X >= rect.Max.X || rect.Min.Y >= rect.Max.Y ||
            !WithinLimit(rect.Min, limits.MaxAbsoluteCoordinate) ||
            !WithinLimit(rect.Max, limits.MaxAbsoluteCoordinate))
        {
            issue = new KsProcgenIssue("InvalidRectangle", "A rectangle must have positive size and bounded coordinates.");
            return false;
        }

        if ((long) (rect.Max.X - rect.Min.X) * (rect.Max.Y - rect.Min.Y) > limits.MaxCells)
        {
            issue = new KsProcgenIssue("CellBudgetExceeded", "A rectangle exceeds the cell budget.");
            return false;
        }

        return true;
    }

    private static bool TryAdd(HashSet<Vector2i> cells, Vector2i cell, KsProcgenLimits limits, out KsProcgenIssue? issue)
    {
        issue = null;
        if (!WithinLimit(cell, limits.MaxAbsoluteCoordinate))
        {
            issue = new KsProcgenIssue("CoordinateOutOfRange", $"Cell {cell} exceeds the coordinate limit.");
            return false;
        }

        cells.Add(cell);
        if (cells.Count <= limits.MaxCells)
            return true;

        issue = new KsProcgenIssue("CellBudgetExceeded", "The shape exceeds its cell budget.");
        return false;
    }

    private static bool WithinLimit(Vector2i cell, int limit) =>
        cell.X >= -limit && cell.X <= limit && cell.Y >= -limit && cell.Y <= limit;
}
