using System.Linq;

namespace Content.Shared._KS14.Procedural;

/// <summary>Authoring for existing request constraints; no work limits or permission to publish.</summary>
[DataDefinition]
public sealed partial class KsProcgenAreaGoalsSpec
{
    [DataField] public List<KsProcgenRoomSizeGoal> SizeMix = new();
    [DataField] public KsProcgenFallbackPolicy FallbackPolicy = new();
    [DataField] public KsProcgenWindowGoal WindowGoal = new();
}

public sealed record KsProcgenAreaSizeGoal(string Id, int MinCells, int MaxCells, int TargetCount)
{
    internal KsProcgenRoomSizeGoal Copy() => new() { Id = Id, MinCells = MinCells, MaxCells = MaxCells, TargetCount = TargetCount };
}

public sealed record KsProcgenAreaFallbackPolicy(bool AllowMergedPartition, bool AllowRoomSizeShortfall,
    bool AllowSparseFurnishing, bool AllowSparseLighting)
{
    internal KsProcgenFallbackPolicy Copy() => new()
    {
        AllowMergedPartition = AllowMergedPartition, AllowRoomSizeShortfall = AllowRoomSizeShortfall,
        AllowSparseFurnishing = AllowSparseFurnishing, AllowSparseLighting = AllowSparseLighting,
    };
}

public sealed record KsProcgenAreaWindowGoal(float ExteriorWindowFraction, bool HardFraction, int ToleranceCells,
    int MinimumCount, int? MaximumCount)
{
    internal KsProcgenWindowGoal Copy() => new()
    {
        ExteriorWindowFraction = ExteriorWindowFraction, HardFraction = HardFraction,
        ToleranceCells = ToleranceCells, MinimumCount = MinimumCount, MaximumCount = MaximumCount,
    };
}

/// <summary>Immutable scalar goals and policy; the size bands are canonical by ID.</summary>
public sealed record KsProcgenAreaGoalsProfile(IReadOnlyList<KsProcgenAreaSizeGoal> SizeMix,
    KsProcgenAreaFallbackPolicy FallbackPolicy, KsProcgenAreaWindowGoal WindowGoal);

public static class KsProcgenAreaGoals
{
    public static bool TrySnapshot(KsProcgenAreaGoalsSpec spec, out KsProcgenAreaGoalsProfile? profile)
    {
        profile = null;
        if (spec == null || spec.FallbackPolicy == null || spec.SizeMix == null || spec.SizeMix.Count > 64 ||
            spec.SizeMix.Any(goal => goal == null || !KsProcgenAreaNormalizer.ValidId(goal.Id)) ||
            !KsProcgenGoalValidation.TryValidate(spec.SizeMix, spec.WindowGoal, out _))
            return false;
        var fallback = spec.FallbackPolicy;
        var window = spec.WindowGoal;
        profile = new(KsProcgenReadOnly.Copy(spec.SizeMix.OrderBy(goal => goal.Id, StringComparer.Ordinal)
                .Select(goal => new KsProcgenAreaSizeGoal(goal.Id, goal.MinCells, goal.MaxCells, goal.TargetCount))),
            new(fallback.AllowMergedPartition, fallback.AllowRoomSizeShortfall, fallback.AllowSparseFurnishing, fallback.AllowSparseLighting),
            new(window.ExteriorWindowFraction, window.HardFraction, window.ToleranceCells, window.MinimumCount, window.MaximumCount));
        return true;
    }

    internal static bool Valid(KsProcgenAreaGoalsProfile profile) => profile != null && profile.FallbackPolicy != null &&
        profile.WindowGoal != null && profile.SizeMix != null && profile.SizeMix.Count <= 64 &&
        profile.SizeMix.All(goal => goal != null && KsProcgenAreaNormalizer.ValidId(goal.Id)) &&
        KsProcgenGoalValidation.TryValidate(profile.SizeMix.Select(goal => goal.Copy()).ToArray(), profile.WindowGoal.Copy(), out _);

    internal static KsProcgenAreaGoalsProfile Freeze(KsProcgenAreaGoalsProfile profile) => profile with
    {
        SizeMix = KsProcgenReadOnly.Copy(profile.SizeMix.OrderBy(goal => goal.Id, StringComparer.Ordinal)),
    };

    internal static void AddHash(ref KsProcgenStableHash hash, KsProcgenAreaGoalsProfile? profile)
    {
        hash.AddInt(profile == null ? 0 : 1);
        if (profile == null) return;
        hash.AddInt(profile.SizeMix.Count);
        foreach (var goal in profile.SizeMix)
        {
            hash.AddString(goal.Id); hash.AddInt(goal.MinCells); hash.AddInt(goal.MaxCells); hash.AddInt(goal.TargetCount);
        }
        var fallback = profile.FallbackPolicy;
        hash.AddInt(fallback.AllowMergedPartition ? 1 : 0); hash.AddInt(fallback.AllowRoomSizeShortfall ? 1 : 0);
        hash.AddInt(fallback.AllowSparseFurnishing ? 1 : 0); hash.AddInt(fallback.AllowSparseLighting ? 1 : 0);
        var window = profile.WindowGoal;
        hash.AddInt(BitConverter.SingleToInt32Bits(window.ExteriorWindowFraction)); hash.AddInt(window.HardFraction ? 1 : 0);
        hash.AddInt(window.ToleranceCells); hash.AddInt(window.MinimumCount);
        hash.AddInt(window.MaximumCount.HasValue ? 1 : 0); hash.AddInt(window.MaximumCount ?? 0);
    }
}
