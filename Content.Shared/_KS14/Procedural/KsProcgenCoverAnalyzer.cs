using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

public enum KsProcgenCoverStatus : byte
{
    Complete,
    InvalidInput,
    BudgetExceeded,
}

public sealed record KsProcgenCoverSample(
    Vector2i Cell,
    int ThreatsInRange,
    int ClearThreatRays,
    int BlockedThreatRays,
    int NearbyLegalPositions,
    int NearbyProtectedPositions)
{
    public bool Protected => ThreatsInRange > 0 && ClearThreatRays == 0;
}

/// <summary>
/// Directional geometric projectile-cover estimate. Stance, height, projectile type, and live
/// engine obstruction are not verified.
/// </summary>
public sealed class KsProcgenCoverResult
{
    public KsProcgenCoverStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public IReadOnlyList<KsProcgenCoverSample> Samples { get; init; } = [];
    public int EvaluatedRays { get; init; }
    public int ProtectedSamples { get; init; }
    public bool EngineProjectileVerified => false;
}

/// <summary>
/// Tests nominated positions and their legal cardinal neighbors against caller-supplied threat
/// origins. This uses a projectile mask separate from the vision and movement masks.
/// </summary>
public static class KsProcgenCoverAnalyzer
{
    private static readonly Vector2i[] Cardinal =
        [new(1, 0), new(0, 1), new(-1, 0), new(0, -1)];

    private readonly record struct PositionRays(int ThreatsInRange, int ClearThreatRays)
    {
        public bool Protected => ThreatsInRange > 0 && ClearThreatRays == 0;
    }

    public static KsProcgenCoverResult Analyze(
        IReadOnlySet<Vector2i> legalCells,
        IReadOnlySet<Vector2i> sampleCells,
        IReadOnlySet<Vector2i> projectileBlockingCells,
        IReadOnlySet<Vector2i> threatOrigins,
        int radius = 12,
        int maxSamples = 512,
        int maxThreats = 32,
        int maxRays = 8_192)
    {
        if (legalCells == null || sampleCells == null || projectileBlockingCells == null ||
            threatOrigins == null || radius <= 0 || radius > 64 ||
            maxSamples <= 0 || maxSamples > 2_048 ||
            maxThreats <= 0 || maxThreats > 256 ||
            maxRays <= 0 || maxRays > 65_536 ||
            sampleCells.Any(cell => !legalCells.Contains(cell) ||
                                    projectileBlockingCells.Contains(cell)) ||
            threatOrigins.Any(projectileBlockingCells.Contains))
            return Failure(KsProcgenCoverStatus.InvalidInput, "InvalidCoverInput");
        if (sampleCells.Count > maxSamples || threatOrigins.Count > maxThreats)
            return Failure(KsProcgenCoverStatus.BudgetExceeded, "CoverSampleBudget");

        var evaluatedCells = new HashSet<Vector2i>(sampleCells);
        foreach (var sample in sampleCells)
        foreach (var offset in Cardinal)
        {
            if (!TryAdd(sample, offset, out var neighbor) ||
                !legalCells.Contains(neighbor) || projectileBlockingCells.Contains(neighbor))
                continue;
            evaluatedCells.Add(neighbor);
        }

        var orderedThreats = threatOrigins.OrderBy(cell => cell.Y).ThenBy(cell => cell.X).ToArray();
        var evaluated = new Dictionary<Vector2i, PositionRays>();
        var rayCount = 0;
        var radiusSquared = (long) radius * radius;
        foreach (var position in evaluatedCells.OrderBy(cell => cell.Y).ThenBy(cell => cell.X))
        {
            var inRange = 0;
            var clear = 0;
            foreach (var threat in orderedThreats)
            {
                var deltaX = (long) position.X - threat.X;
                var deltaY = (long) position.Y - threat.Y;
                if (Math.Abs(deltaX) > radius || Math.Abs(deltaY) > radius ||
                    deltaX * deltaX + deltaY * deltaY > radiusSquared)
                    continue;
                if (rayCount >= maxRays)
                    return Failure(KsProcgenCoverStatus.BudgetExceeded, "CoverRayBudget");
                rayCount++;
                inRange++;
                if (KsProcgenTileRay.IsClear(threat, position, projectileBlockingCells))
                    clear++;
            }
            evaluated.Add(position, new PositionRays(inRange, clear));
        }

        var samples = new List<KsProcgenCoverSample>();
        var protectedCount = 0;
        foreach (var sample in sampleCells.OrderBy(cell => cell.Y).ThenBy(cell => cell.X))
        {
            var rays = evaluated[sample];
            var nearbyLegal = 0;
            var nearbyProtected = 0;
            foreach (var offset in Cardinal)
            {
                if (!TryAdd(sample, offset, out var neighbor) ||
                    !evaluated.TryGetValue(neighbor, out var neighborRays))
                    continue;
                nearbyLegal++;
                if (neighborRays.Protected)
                    nearbyProtected++;
            }
            samples.Add(new KsProcgenCoverSample(sample, rays.ThreatsInRange,
                rays.ClearThreatRays, rays.ThreatsInRange - rays.ClearThreatRays,
                nearbyLegal, nearbyProtected));
            if (rays.Protected)
                protectedCount++;
        }

        return new KsProcgenCoverResult
        {
            Status = KsProcgenCoverStatus.Complete,
            Samples = samples,
            EvaluatedRays = rayCount,
            ProtectedSamples = protectedCount,
        };
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

    private static KsProcgenCoverResult Failure(KsProcgenCoverStatus status, string code) =>
        new()
        {
            Status = status,
            Issue = new KsProcgenIssue(code,
                "Cover sampling input or the configured work budget is invalid."),
        };
}
