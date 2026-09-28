using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

public sealed record KsProcgenConstantRegion(
    string Id,
    string SourceId,
    string ContentFingerprint,
    Vector2i Origin,
    int QuarterTurns,
    IReadOnlyList<Vector2i> Cells,
    IReadOnlyList<KsProcgenPortGeometry> Ports);

/// <summary>
/// Exact normalized ownership masks. Public cell lists are sorted and detached from the request.
/// </summary>
public sealed class KsProcgenNormalizedShape
{
    private readonly HashSet<Vector2i> _targetSet;
    private readonly HashSet<Vector2i> _envelopeSet;
    private readonly HashSet<Vector2i> _preservedSet;
    private readonly HashSet<Vector2i> _voidSet;
    private readonly Dictionary<Vector2i, string> _constantOwnerByCell;

    public IReadOnlyList<Vector2i> TargetCells { get; }
    public IReadOnlyList<Vector2i> EnvelopeCells { get; }
    public IReadOnlyList<Vector2i> PreservedCells { get; }
    public IReadOnlyList<Vector2i> VoidCells { get; }
    public IReadOnlyList<KsProcgenConstantRegion> ConstantRegions { get; }
    public ulong ConstantContractHash { get; }

    internal KsProcgenNormalizedShape(
        HashSet<Vector2i> targetSet,
        HashSet<Vector2i> envelopeSet,
        HashSet<Vector2i> preservedSet,
        HashSet<Vector2i> voidSet,
        IReadOnlyList<KsProcgenConstantRegion> constantRegions)
    {
        _targetSet = new HashSet<Vector2i>(targetSet);
        _envelopeSet = new HashSet<Vector2i>(envelopeSet);
        _preservedSet = new HashSet<Vector2i>(preservedSet);
        _voidSet = new HashSet<Vector2i>(voidSet);
        TargetCells = KsProcgenGeometry.SortCells(_targetSet);
        EnvelopeCells = KsProcgenGeometry.SortCells(_envelopeSet);
        PreservedCells = KsProcgenGeometry.SortCells(_preservedSet);
        VoidCells = KsProcgenGeometry.SortCells(_voidSet);
        ConstantRegions = Array.AsReadOnly(constantRegions.OrderBy(region => region.Id,
            StringComparer.Ordinal).ToArray());
        _constantOwnerByCell = new Dictionary<Vector2i, string>();
        foreach (var region in ConstantRegions)
        foreach (var cell in region.Cells)
            _constantOwnerByCell.Add(cell, region.Id);

        var hash = KsProcgenStableHash.Create();
        hash.AddString("ks-procgen-constant-contract-v1");
        hash.AddInt(ConstantRegions.Count);
        foreach (var region in ConstantRegions)
        {
            hash.AddString(region.Id);
            hash.AddString(region.SourceId);
            hash.AddString(region.ContentFingerprint);
            hash.AddInt(region.Origin.X);
            hash.AddInt(region.Origin.Y);
            hash.AddInt(region.QuarterTurns);
            hash.AddInt(region.Cells.Count);
            foreach (var cell in region.Cells)
            {
                hash.AddInt(cell.X);
                hash.AddInt(cell.Y);
            }
            hash.AddInt(region.Ports.Count);
            foreach (var port in region.Ports)
            {
                hash.AddString(port.Id);
                hash.AddInt(port.Threshold.X);
                hash.AddInt(port.Threshold.Y);
                hash.AddInt(port.OutwardNormal.X);
                hash.AddInt(port.OutwardNormal.Y);
            }
        }
        ConstantContractHash = hash.Value;
    }

    public bool ContainsTarget(Vector2i cell) => _targetSet.Contains(cell);
    public bool ContainsEnvelope(Vector2i cell) => _envelopeSet.Contains(cell);
    public bool ContainsPreserved(Vector2i cell) => _preservedSet.Contains(cell);
    public bool ContainsVoid(Vector2i cell) => _voidSet.Contains(cell);
    public bool TryGetConstantOwner(Vector2i cell, out string ownerId) =>
        _constantOwnerByCell.TryGetValue(cell, out ownerId!);
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
            request.RootCells == null || request.ConstantRegions == null)
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

        if (request.ConstantRegions.Count > 512)
        {
            issue = new KsProcgenIssue("ConstantRegionBudget", "At most 512 constant regions are supported.");
            return false;
        }

        if (request.SizeMix == null)
        {
            issue = new KsProcgenIssue("InvalidSizeMix", "Size goals cannot be null.");
            return false;
        }
        if (request.WindowGoal == null ||
            !float.IsFinite(request.WindowGoal.ExteriorWindowFraction) ||
            request.WindowGoal.ExteriorWindowFraction is < 0f or > 1f ||
            request.WindowGoal.ToleranceCells is < 0 or > 65_536 ||
            request.WindowGoal.MinimumCount is < 0 or > 65_536 ||
            request.WindowGoal.MaximumCount is < 0 or > 65_536 ||
            request.WindowGoal.MaximumCount.HasValue &&
            request.WindowGoal.MaximumCount.Value < request.WindowGoal.MinimumCount)
        {
            issue = new KsProcgenIssue("InvalidWindowGoal",
                "Window fraction, tolerance, and count bounds must be finite and supported.");
            return false;
        }
        var sizeIds = new HashSet<string>(StringComparer.Ordinal);
        var requestedRoomCount = 0L;
        foreach (var goal in request.SizeMix)
        {
            if (goal == null || string.IsNullOrWhiteSpace(goal.Id) || !sizeIds.Add(goal.Id) ||
                goal.MinCells <= 0 || goal.MaxCells < goal.MinCells ||
                goal.MaxCells > 65_536 || goal.TargetCount <= 0)
            {
                issue = new KsProcgenIssue("InvalidSizeMix",
                    "Size goals need unique IDs, positive ordered area bounds, and positive counts.");
                return false;
            }
            requestedRoomCount += goal.TargetCount;
            if (requestedRoomCount > 4_096)
            {
                issue = new KsProcgenIssue("SizeMixBudget",
                    "At most 4096 preliminary room zones may be requested.");
                return false;
            }
        }
        var orderedSizes = request.SizeMix.OrderBy(goal => goal.MinCells).ToArray();
        for (var index = 1; index < orderedSizes.Length; index++)
        {
            if (orderedSizes[index - 1].MaxCells < orderedSizes[index].MinCells)
                continue;
            issue = new KsProcgenIssue("OverlappingSizeMix",
                "Size bands must not overlap so each final room has one size class.");
            return false;
        }

        var constantIds = new HashSet<string>(StringComparer.Ordinal);
        var constantRegions = new List<KsProcgenConstantRegion>();
        foreach (var constant in request.ConstantRegions)
        {
            if (constant == null || string.IsNullOrWhiteSpace(constant.Id) ||
                string.IsNullOrWhiteSpace(constant.SourceId) ||
                string.IsNullOrWhiteSpace(constant.ContentFingerprint) ||
                constant.LocalCells == null || constant.LocalCells.Count == 0 ||
                constant.Ports == null || constant.Ports.Count > 256 ||
                constant.LocalCells.Count > limits.MaxCells ||
                constant.QuarterTurns is < 0 or > 3 ||
                !WithinLimit(constant.Origin, limits.MaxAbsoluteCoordinate) ||
                !constantIds.Add(constant.Id))
            {
                issue = new KsProcgenIssue("InvalidConstantRegion",
                    "Each constant needs a unique ID, source, fingerprint, local mask, and supported transform.");
                return false;
            }

            var localCells = new HashSet<Vector2i>();
            var transformed = new HashSet<Vector2i>();
            foreach (var local in constant.LocalCells)
            {
                if (!localCells.Add(local))
                {
                    issue = new KsProcgenIssue("DuplicateConstantCell",
                        "A constant mask may not repeat a local cell.");
                    return false;
                }

                Vector2i world;
                try
                {
                    world = TransformCell(local, Vector2i.Zero, constant.Origin, constant.QuarterTurns);
                }
                catch (OverflowException)
                {
                    issue = new KsProcgenIssue("ConstantTransformOutOfRange",
                        "A constant transform exceeds the supported coordinate range.");
                    return false;
                }

                if (!WithinLimit(world, limits.MaxAbsoluteCoordinate))
                {
                    issue = new KsProcgenIssue("ConstantTransformOutOfRange",
                        "A transformed constant cell exceeds the coordinate limit.");
                    return false;
                }
                if (!targetSet.Contains(world))
                {
                    issue = new KsProcgenIssue("ConstantOutsideTarget",
                        "Every constant cell must belong to the requested target mask.");
                    return false;
                }
                if (preservedSet.Contains(world))
                {
                    issue = new KsProcgenIssue("ConstantOverlap",
                        "Constants cannot overlap one another or anonymous preserved cells.");
                    return false;
                }

                transformed.Add(world);
            }

            var ports = new List<KsProcgenPortGeometry>();
            var portIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var port in constant.Ports)
            {
                if (port == null || string.IsNullOrWhiteSpace(port.Id) || !portIds.Add(port.Id) ||
                    Math.Abs(port.OutwardNormal.X) + Math.Abs(port.OutwardNormal.Y) != 1 ||
                    !localCells.Contains(port.Threshold) ||
                    !localCells.Contains(port.Threshold - port.OutwardNormal) ||
                    localCells.Contains(port.Threshold + port.OutwardNormal))
                {
                    issue = new KsProcgenIssue("InvalidConstantPort",
                        "A constant port needs a unique ID, boundary threshold, cardinal normal, and interior landing.");
                    return false;
                }

                var threshold = TransformCell(port.Threshold, Vector2i.Zero, constant.Origin,
                    constant.QuarterTurns);
                var normal = TransformCell(port.OutwardNormal, Vector2i.Zero, Vector2i.Zero,
                    constant.QuarterTurns);
                var outside = threshold + normal;
                if (!targetSet.Contains(outside))
                {
                    issue = new KsProcgenIssue("ConstantPortOutsideTarget",
                        "A constant port must face another target cell until exterior access is supported.");
                    return false;
                }
                var owner = $"constant:{constant.Id}";
                ports.Add(new KsProcgenPortGeometry($"{owner}/{port.Id}", owner,
                    threshold, normal, threshold - normal, outside));
            }

            preservedSet.UnionWith(transformed);
            constantRegions.Add(new KsProcgenConstantRegion(constant.Id, constant.SourceId,
                constant.ContentFingerprint, constant.Origin, constant.QuarterTurns,
                SortCells(transformed), Array.AsReadOnly(ports.OrderBy(port => port.Id,
                    StringComparer.Ordinal).ToArray())));
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

        shape = new KsProcgenNormalizedShape(targetSet, envelopeSet, preservedSet, voidSet,
            constantRegions);
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
