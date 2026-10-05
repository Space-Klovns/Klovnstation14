using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

public enum KsProcgenInteractionStatus : byte
{
    Ready,
    NoCleanApproach,
    InvalidInput,
}

public sealed record KsProcgenMachineFacing(
    int QuarterTurns,
    Vector2i Facing,
    Vector2i Approach,
    bool BackedByWall,
    IReadOnlyList<Vector2i> CleanPath);

public sealed class KsProcgenMachineFacingResult
{
    public KsProcgenInteractionStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public KsProcgenMachineFacing? Facing { get; init; }
    public IReadOnlyList<KsProcgenMachineFacing> Options { get; init; } = [];
}

public sealed class KsProcgenChairAccessResult
{
    public KsProcgenInteractionStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public Vector2i? Approach { get; init; }
    public IReadOnlyList<Vector2i> CleanPath { get; init; } = [];
    public IReadOnlyList<Vector2i> Approaches { get; init; } = [];
}

/// <summary>
/// Declared machine footprint and one-cell seat approach geometry. The engine adapter must confirm actual collision,
/// interaction direction, sprite rotation, and whether a seat tile permits ordinary traversal.
/// </summary>
public static class KsProcgenInteractionFacingPlanner
{
    // Authoring convention: unrotated is down/south; 90 degrees faces left/west.
    private static readonly Vector2i[] Faces = [new(0, -1), new(-1, 0), new(0, 1), new(1, 0)];

    public static KsProcgenMachineFacingResult ChooseMachineFacing(
        Vector2i machineCell,
        IReadOnlySet<Vector2i> roomFloor,
        IReadOnlySet<Vector2i> wallCells,
        IReadOnlySet<Vector2i> blockingCells,
        IReadOnlySet<Vector2i> traversibleChairCells,
        IReadOnlySet<Vector2i> networkCells,
        IReadOnlyList<int> allowedQuarterTurns,
        int seed,
        string instanceId,
        Vector2i? associatedChair = null,
        IReadOnlyList<Vector2i>? localFootprint = null)
    {
        if (roomFloor == null || wallCells == null || blockingCells == null ||
            traversibleChairCells == null || networkCells == null || allowedQuarterTurns == null ||
            string.IsNullOrWhiteSpace(instanceId) || !roomFloor.Contains(machineCell) ||
            allowedQuarterTurns.Count == 0 || allowedQuarterTurns.Any(turn => turn is < 0 or > 3) ||
            networkCells.Count == 0 || networkCells.Any(cell => !roomFloor.Contains(cell) ||
                                                     blockingCells.Contains(cell) || cell == machineCell) ||
            localFootprint is { Count: 0 } || localFootprint is { Count: > 16 } ||
            localFootprint != null && (!localFootprint.Contains(new Vector2i(0, 0)) ||
                                       localFootprint.Distinct().Count() != localFootprint.Count ||
                                       localFootprint.Any(cell => Math.Abs(cell.X) > 8 ||
                                                                  Math.Abs(cell.Y) > 8)))
            return MachineFailure(KsProcgenInteractionStatus.InvalidInput, "InvalidMachineFacingInput");

        localFootprint ??= [new Vector2i(0, 0)];
        var options = new List<(KsProcgenMachineFacing Facing, int Score, ulong Tie)>();
        foreach (var turn in allowedQuarterTurns.Distinct().OrderBy(turn => turn))
        {
            var face = Faces[turn];
            var footprint = localFootprint.Select(offset =>
                    machineCell + RotateFootprintOffset(offset, turn))
                .ToHashSet();
            if (footprint.Any(cell => !roomFloor.Contains(cell) || networkCells.Contains(cell) ||
                                      blockingCells.Contains(cell)))
                continue;
            var clean = new HashSet<Vector2i>(roomFloor);
            clean.ExceptWith(blockingCells);
            clean.ExceptWith(footprint);
            var backed = footprint.Any(cell => wallCells.Contains(cell - face));
            foreach (var edge in footprint.OrderBy(cell => cell.Y).ThenBy(cell => cell.X))
            {
                var approach = edge + face;
                if (footprint.Contains(approach) || !clean.Contains(approach) ||
                    wallCells.Contains(approach))
                    continue;
                var path = FindPathFromNetwork(approach, clean, networkCells);
                if (path.Count == 0)
                    continue;
                var chair = traversibleChairCells.Contains(approach);
                var associated = associatedChair.HasValue && associatedChair.Value == approach && chair;
                var score = (associated ? 1_000 : 0) + (chair ? 50 : 100) + (backed ? 10 : 0);
                var rank = KsProcgenRandom.ForStage(seed, "machine-facing",
                    $"{instanceId}/{turn}/{approach.X}/{approach.Y}").NextUInt64();
                options.Add((new KsProcgenMachineFacing(turn, face, approach, backed, path), score, rank));
            }
        }

        if (options.Count == 0)
            return MachineFailure(KsProcgenInteractionStatus.NoCleanApproach, "MachineApproachUnavailable");
        var ranked = options.OrderByDescending(option => option.Score)
            .ThenBy(option => option.Facing.CleanPath.Count)
            .ThenBy(option => option.Tie)
            .ThenBy(option => option.Facing.Approach.Y)
            .ThenBy(option => option.Facing.Approach.X)
            .ThenBy(option => option.Facing.QuarterTurns).Select(option => option.Facing).ToArray();
        return new KsProcgenMachineFacingResult
        {
            Status = KsProcgenInteractionStatus.Ready,
            Facing = ranked[0],
            Options = ranked,
        };
    }

    internal static Vector2i RotateFootprintOffset(Vector2i offset, int turns) => turns switch
    {
        1 => new Vector2i(offset.Y, -offset.X),
        2 => new Vector2i(-offset.X, -offset.Y),
        3 => new Vector2i(-offset.Y, offset.X),
        _ => offset,
    };

    public static KsProcgenChairAccessResult FindChairApproach(
        Vector2i chairCell,
        IReadOnlySet<Vector2i> roomFloor,
        IReadOnlySet<Vector2i> blockingCells,
        IReadOnlySet<Vector2i> networkCells)
    {
        if (roomFloor == null || blockingCells == null || networkCells == null ||
            !roomFloor.Contains(chairCell) || networkCells.Count == 0 ||
            networkCells.Any(cell => !roomFloor.Contains(cell) || blockingCells.Contains(cell)))
            return ChairFailure(KsProcgenInteractionStatus.InvalidInput, "InvalidChairApproachInput");

        var clean = new HashSet<Vector2i>(roomFloor);
        clean.ExceptWith(blockingCells);
        clean.Remove(chairCell);
        var paths = new List<IReadOnlyList<Vector2i>>();
        foreach (var face in Faces)
        {
            var approach = chairCell + face;
            if (!clean.Contains(approach))
                continue;
            var path = FindPathFromNetwork(approach, clean, networkCells);
            if (path.Count > 0)
                paths.Add(path);
        }

        if (paths.Count == 0)
            return ChairFailure(KsProcgenInteractionStatus.NoCleanApproach, "ChairApproachUnavailable");
        var ranked = paths.OrderBy(path => path.Count).ThenBy(path => path[^1].Y)
            .ThenBy(path => path[^1].X).ToArray();
        var selected = ranked[0];
        return new KsProcgenChairAccessResult
        {
            Status = KsProcgenInteractionStatus.Ready,
            Approach = selected[^1],
            CleanPath = selected,
            Approaches = ranked.Select(path => path[^1]).Distinct().ToArray(),
        };
    }

    private static KsProcgenMachineFacingResult MachineFailure(KsProcgenInteractionStatus status, string code) => new()
    {
        Status = status,
        Issue = new KsProcgenIssue(code, "The machine has no valid clean interaction facing."),
    };

    private static KsProcgenChairAccessResult ChairFailure(KsProcgenInteractionStatus status, string code) => new()
    {
        Status = status,
        Issue = new KsProcgenIssue(code, "The chair has no clean reachable approach."),
    };

    private static IReadOnlyList<Vector2i> FindPathFromNetwork(
        Vector2i target,
        IReadOnlySet<Vector2i> clean,
        IReadOnlySet<Vector2i> network)
    {
        if (!clean.Contains(target))
            return [];
        var parents = new Dictionary<Vector2i, Vector2i>();
        var queue = new Queue<Vector2i>();
        foreach (var root in KsProcgenGeometry.SortCells(network))
        {
            if (!clean.Contains(root) || !parents.TryAdd(root, root))
                continue;
            queue.Enqueue(root);
        }

        while (queue.TryDequeue(out var cell))
        {
            if (cell == target)
            {
                var path = new List<Vector2i> { cell };
                while (parents[cell] != cell)
                {
                    cell = parents[cell];
                    path.Add(cell);
                }

                path.Reverse();
                return path;
            }

            foreach (var face in Faces)
            {
                var next = cell + face;
                if (clean.Contains(next) && parents.TryAdd(next, cell))
                    queue.Enqueue(next);
            }
        }

        return [];
    }
}
