using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

public enum KsProcgenFiringPositionStatus : byte
{
    Complete,
    InvalidInput,
    BudgetExceeded,
}

public sealed record KsProcgenFiringPositionSample(
    Vector2i Cell,
    bool Reachable,
    bool ThreatInRange,
    bool TargetInRange,
    bool ProtectedFromThreat,
    bool ClearShotToTarget)
{
    public bool Accepted => Reachable && ThreatInRange && TargetInRange &&
                            ProtectedFromThreat && ClearShotToTarget;
}

/// <summary>
/// Geometric candidates only. Live projectile obstruction, stance, and actor traversal remain
/// unverified, and a protected position does not guarantee a playable defensive advantage.
/// </summary>
public sealed class KsProcgenFiringPositionResult
{
    public KsProcgenFiringPositionStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public IReadOnlyList<KsProcgenFiringPositionSample> Samples { get; init; } = [];
    public IReadOnlyList<Vector2i> AcceptedCells { get; init; } = [];
    public int ExpandedCells { get; init; }
    public int EvaluatedRays { get; init; }
    public bool EngineProjectileVerified => false;
    public bool EngineTraversalVerified => false;
}

/// <summary>
/// Finds nominated clean positions that are reachable from a root, blocked from one threat
/// origin, and have a clear sampled projectile ray to a different target cell.
/// </summary>
public static class KsProcgenFiringPositionAnalyzer
{
    private static readonly Vector2i[] Cardinal =
        [new(1, 0), new(0, 1), new(-1, 0), new(0, -1)];

    public static KsProcgenFiringPositionResult Analyze(
        IReadOnlySet<Vector2i> walkableCells,
        IReadOnlySet<Vector2i> vaultOnlyCells,
        IReadOnlySet<Vector2i> projectileBlockingCells,
        IReadOnlySet<Vector2i> candidateCells,
        Vector2i root,
        Vector2i threatOrigin,
        Vector2i firingTarget,
        int radius = 12,
        int maxCandidates = 512,
        int maxExpandedCells = 65_536,
        int maxRays = 8_192)
    {
        if (walkableCells == null || vaultOnlyCells == null || projectileBlockingCells == null ||
            candidateCells == null || radius <= 0 || radius > 64 ||
            maxCandidates <= 0 || maxCandidates > 2_048 ||
            maxExpandedCells <= 0 || maxExpandedCells > 65_536 ||
            maxRays <= 0 || maxRays > 65_536 ||
            threatOrigin == firingTarget || vaultOnlyCells.Any(cell => !walkableCells.Contains(cell)) ||
            projectileBlockingCells.Contains(threatOrigin) ||
            projectileBlockingCells.Contains(firingTarget))
            return Failure(KsProcgenFiringPositionStatus.InvalidInput, "InvalidFiringPositionInput");

        var clean = new HashSet<Vector2i>(walkableCells);
        clean.ExceptWith(vaultOnlyCells);
        if (!clean.Contains(root) || candidateCells.Any(cell => !clean.Contains(cell) ||
                                                       projectileBlockingCells.Contains(cell) ||
                                                       cell == firingTarget))
            return Failure(KsProcgenFiringPositionStatus.InvalidInput,
                "InvalidFiringPositionContract");
        if (candidateCells.Count > maxCandidates)
            return Failure(KsProcgenFiringPositionStatus.BudgetExceeded,
                "FiringPositionCandidateBudget");

        var reachable = new HashSet<Vector2i> { root };
        var queue = new Queue<Vector2i>();
        queue.Enqueue(root);
        var expanded = 0;
        while (queue.TryDequeue(out var cell))
        {
            if (expanded >= maxExpandedCells)
                return Failure(KsProcgenFiringPositionStatus.BudgetExceeded,
                    "FiringPositionExpansionBudget");
            expanded++;
            foreach (var offset in Cardinal)
            {
                if (!TryAdd(cell, offset, out var neighbor) ||
                    !clean.Contains(neighbor) || !reachable.Add(neighbor))
                    continue;
                queue.Enqueue(neighbor);
            }
        }

        var samples = new List<KsProcgenFiringPositionSample>();
        var accepted = new List<Vector2i>();
        var rays = 0;
        foreach (var candidate in candidateCells.OrderBy(cell => cell.Y).ThenBy(cell => cell.X))
        {
            var canReach = reachable.Contains(candidate);
            var threatInRange = InRange(candidate, threatOrigin, radius);
            var targetInRange = InRange(candidate, firingTarget, radius);
            var protectedFromThreat = false;
            var clearToTarget = false;
            if (canReach && threatInRange && targetInRange)
            {
                if (rays > maxRays - 2)
                    return Failure(KsProcgenFiringPositionStatus.BudgetExceeded,
                        "FiringPositionRayBudget");
                rays += 2;
                protectedFromThreat = !KsProcgenTileRay.IsClear(threatOrigin, candidate,
                    projectileBlockingCells);
                clearToTarget = KsProcgenTileRay.IsClear(candidate, firingTarget,
                    projectileBlockingCells);
            }

            var sample = new KsProcgenFiringPositionSample(candidate, canReach,
                threatInRange, targetInRange, protectedFromThreat, clearToTarget);
            samples.Add(sample);
            if (sample.Accepted)
                accepted.Add(candidate);
        }

        return new KsProcgenFiringPositionResult
        {
            Status = KsProcgenFiringPositionStatus.Complete,
            Samples = samples,
            AcceptedCells = accepted,
            ExpandedCells = expanded,
            EvaluatedRays = rays,
        };
    }

    private static bool InRange(Vector2i first, Vector2i second, int radius)
    {
        var deltaX = (long) first.X - second.X;
        var deltaY = (long) first.Y - second.Y;
        return Math.Abs(deltaX) <= radius && Math.Abs(deltaY) <= radius &&
               deltaX * deltaX + deltaY * deltaY <= (long) radius * radius;
    }

    private static bool TryAdd(Vector2i first, Vector2i second, out Vector2i result)
    {
        var x = (long) first.X + second.X;
        var y = (long) first.Y + second.Y;
        if (x is < int.MinValue or > int.MaxValue || y is < int.MinValue or > int.MaxValue)
        {
            result = default;
            return false;
        }

        result = new Vector2i((int) x, (int) y);
        return true;
    }

    private static KsProcgenFiringPositionResult Failure(
        KsProcgenFiringPositionStatus status, string code) => new()
    {
        Status = status,
        Issue = new KsProcgenIssue(code,
            "Firing-position input or the configured work budget is invalid."),
    };
}
