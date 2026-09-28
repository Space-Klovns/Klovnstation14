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
}

public sealed class KsProcgenChairAccessResult
{
    public KsProcgenInteractionStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public Vector2i? Approach { get; init; }
    public IReadOnlyList<Vector2i> CleanPath { get; init; } = [];
}

/// <summary>
/// One-cell machine and seat approach geometry. The engine adapter must confirm actual collision,
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
        Vector2i? associatedChair = null)
    {
        if (roomFloor == null || wallCells == null || blockingCells == null ||
            traversibleChairCells == null || networkCells == null || allowedQuarterTurns == null ||
            string.IsNullOrWhiteSpace(instanceId) || !roomFloor.Contains(machineCell) ||
            allowedQuarterTurns.Count == 0 || allowedQuarterTurns.Any(turn => turn is < 0 or > 3) ||
            networkCells.Count == 0 || networkCells.Any(cell => !roomFloor.Contains(cell) ||
                                                     blockingCells.Contains(cell) || cell == machineCell))
            return MachineFailure(KsProcgenInteractionStatus.InvalidInput, "InvalidMachineFacingInput");

        var clean = new HashSet<Vector2i>(roomFloor);
        clean.ExceptWith(blockingCells);
        clean.Remove(machineCell);
        var options = new List<(KsProcgenMachineFacing Facing, int Score, ulong Tie)>();
        foreach (var turn in allowedQuarterTurns.Distinct().OrderBy(turn => turn))
        {
            var face = Faces[turn];
            var approach = machineCell + face;
            if (!clean.Contains(approach) || wallCells.Contains(approach))
                continue;
            var bestPath = FindPathFromNetwork(approach, clean, networkCells);

            if (bestPath.Count == 0)
                continue;
            var backed = wallCells.Contains(machineCell - face);
            var chair = traversibleChairCells.Contains(approach);
            var associated = associatedChair.HasValue && associatedChair.Value == approach && chair;
            var score = (associated ? 1_000 : 0) + (chair ? 50 : 100) + (backed ? 10 : 0);
            var rank = KsProcgenRandom.ForStage(seed, "machine-facing", $"{instanceId}/{turn}").NextUInt64();
            options.Add((new KsProcgenMachineFacing(turn, face, approach, backed, bestPath), score, rank));
        }

        if (options.Count == 0)
            return MachineFailure(KsProcgenInteractionStatus.NoCleanApproach, "MachineApproachUnavailable");
        var selected = options.OrderByDescending(option => option.Score)
            .ThenBy(option => option.Facing.CleanPath.Count)
            .ThenBy(option => option.Tie)
            .ThenBy(option => option.Facing.QuarterTurns).First();
        return new KsProcgenMachineFacingResult
        {
            Status = KsProcgenInteractionStatus.Ready,
            Facing = selected.Facing,
        };
    }

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
        var selected = paths.OrderBy(path => path.Count).ThenBy(path => path[^1].Y)
            .ThenBy(path => path[^1].X).First();
        return new KsProcgenChairAccessResult
        {
            Status = KsProcgenInteractionStatus.Ready,
            Approach = selected[^1],
            CleanPath = selected,
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
