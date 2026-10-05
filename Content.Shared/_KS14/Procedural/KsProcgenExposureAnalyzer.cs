using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

public enum KsProcgenExposureStatus : byte
{
    Complete,
    InvalidInput,
    BudgetExceeded,
}

public sealed record KsProcgenExposureSample(
    Vector2i Cell,
    int InRangePeers,
    int VisiblePeers,
    long LongestClearRaySquared)
{
    public float VisibleFraction => InRangePeers == 0 ? 0f : (float) VisiblePeers / InRangePeers;
}

/// <summary>
/// Geometric vision-mask estimate. The engine's actual vision and projectile rules are not verified.
/// </summary>
public sealed class KsProcgenExposureResult
{
    public KsProcgenExposureStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public IReadOnlyList<KsProcgenExposureSample> Samples { get; init; } = [];
    public int InRangePairs { get; init; }
    public int VisiblePairs { get; init; }
    public long LongestClearRaySquared { get; init; }
    public float VisiblePairFraction => InRangePairs == 0 ? 0f :
        (float) VisiblePairs / InRangePairs;
    public bool EngineVisionVerified => false;
}

/// <summary>
/// Counts visible pairs among caller-selected usable cells. Rays visit each tile crossed by the
/// segment between cell centers; at an exact corner they inspect both incident side cells.
/// The vision mask is independent of the movement graph and projectile obstruction mask.
/// </summary>
public static class KsProcgenExposureAnalyzer
{
    public static KsProcgenExposureResult Analyze(
        IReadOnlySet<Vector2i> sampleCells,
        IReadOnlySet<Vector2i> visionOpaqueCells,
        int radius = 12,
        int maxSamples = 1_024,
        int maxPairs = 8_192)
    {
        if (sampleCells == null || visionOpaqueCells == null || radius <= 0 || radius > 64 ||
            maxSamples <= 0 || maxSamples > 4_096 || maxPairs <= 0 || maxPairs > 65_536 ||
            sampleCells.Any(visionOpaqueCells.Contains))
            return Failure(KsProcgenExposureStatus.InvalidInput, "InvalidExposureInput");
        if (sampleCells.Count > maxSamples)
            return Failure(KsProcgenExposureStatus.BudgetExceeded, "ExposureSampleBudget");

        var ordered = sampleCells.OrderBy(cell => cell.Y).ThenBy(cell => cell.X).ToArray();
        var inRange = new int[ordered.Length];
        var visible = new int[ordered.Length];
        var longest = new long[ordered.Length];
        var pairs = 0;
        var visiblePairs = 0;
        var longestRay = 0L;
        var radiusSquared = (long) radius * radius;
        for (var first = 0; first < ordered.Length; first++)
        for (var second = first + 1; second < ordered.Length; second++)
        {
            var deltaX = (long) ordered[second].X - ordered[first].X;
            var deltaY = (long) ordered[second].Y - ordered[first].Y;
            if (Math.Abs(deltaX) > radius || Math.Abs(deltaY) > radius)
                continue;
            var distanceSquared = deltaX * deltaX + deltaY * deltaY;
            if (distanceSquared > radiusSquared)
                continue;
            if (pairs >= maxPairs)
                return Failure(KsProcgenExposureStatus.BudgetExceeded, "ExposurePairBudget");
            pairs++;
            inRange[first]++;
            inRange[second]++;
            if (!KsProcgenTileRay.IsClear(ordered[first], ordered[second], visionOpaqueCells))
                continue;
            visiblePairs++;
            visible[first]++;
            visible[second]++;
            longest[first] = Math.Max(longest[first], distanceSquared);
            longest[second] = Math.Max(longest[second], distanceSquared);
            longestRay = Math.Max(longestRay, distanceSquared);
        }

        var samples = new KsProcgenExposureSample[ordered.Length];
        for (var index = 0; index < ordered.Length; index++)
            samples[index] = new KsProcgenExposureSample(ordered[index],
                inRange[index], visible[index], longest[index]);
        return new KsProcgenExposureResult
        {
            Status = KsProcgenExposureStatus.Complete,
            Samples = samples,
            InRangePairs = pairs,
            VisiblePairs = visiblePairs,
            LongestClearRaySquared = longestRay,
        };
    }

    private static KsProcgenExposureResult Failure(KsProcgenExposureStatus status, string code) =>
        new()
        {
            Status = status,
            Issue = new KsProcgenIssue(code,
                "Exposure sampling input or the configured work budget is invalid."),
        };
}
