using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

public enum KsProcgenWindowPlanStatus : byte
{
    Planned,
    NotApplicable,
    HardTargetUnmet,
    InvalidInput,
}

public enum KsProcgenWindowExclusionReason : byte
{
    NoInteriorFace,
    NoExteriorFace,
    UnsupportedAirtightWindow,
    Corner,
    Doorway,
    RequiredStructure,
    DisabledByAuthor,
}

public sealed record KsProcgenWindowExclusion(Vector2i Cell,
    KsProcgenWindowExclusionReason Reason);

/// <summary>
/// One structural boundary cell classified by the hull/engine adapter. Fixed cells participate in
/// the denominator but cannot be changed. A fixed window is counted only when it is eligible.
/// </summary>
public sealed record KsProcgenWindowBoundaryCell(
    Vector2i Cell,
    bool FacesInterior,
    bool FacesExterior,
    bool SupportsAirtightWindow,
    bool Fixed = false,
    bool IsWindow = false,
    bool IsCorner = false,
    bool IsDoorway = false,
    bool RequiredStructure = false,
    bool WindowsDisabled = false);

/// <summary>
/// Selection/counting only. These choices do not prove airtightness or alter an entity.
/// </summary>
public sealed class KsProcgenWindowPlanResult
{
    public KsProcgenWindowPlanStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public IReadOnlyList<Vector2i> ChosenWindowCells { get; init; } = [];
    public int BoundaryCells { get; init; }
    public int EligibleCells { get; init; }
    public int FixedWindowCells { get; init; }
    public int FixedEligibleWallCells { get; init; }
    public int EditableEligibleCells { get; init; }
    public IReadOnlyList<KsProcgenWindowExclusion> ExcludedCells { get; init; } = [];
    public int RequestedWindowCells { get; init; }
    public int AchievedWindowCells { get; init; }
    public float RequestedFraction { get; init; }
    public float AchievedFraction => EligibleCells == 0 ? 0f : (float) AchievedWindowCells / EligibleCells;
    public bool AirtightnessVerified => false;
}

public static class KsProcgenWindowPlanner
{
    public static KsProcgenWindowPlanResult PlanForShape(
        KsProcgenNormalizedShape shape,
        IReadOnlyList<KsProcgenWindowBoundaryCell> boundary,
        int seed,
        KsProcgenWindowGoal goal)
    {
        if (shape == null || boundary == null || boundary.Any(cell => cell == null ||
            !shape.ContainsTarget(cell.Cell) && !shape.ContainsEnvelope(cell.Cell)))
            return Failure(KsProcgenWindowPlanStatus.InvalidInput, "WindowBoundaryOutsideRequest");
        return Plan(boundary, seed, goal);
    }

    public static KsProcgenWindowPlanResult Plan(
        IReadOnlyList<KsProcgenWindowBoundaryCell> boundary,
        int seed,
        KsProcgenWindowGoal goal)
    {
        if (goal == null)
            return Failure(KsProcgenWindowPlanStatus.InvalidInput, "InvalidWindowGoal");
        return Plan(boundary, seed, goal.ExteriorWindowFraction, goal.HardFraction,
            goal.ToleranceCells, goal.MinimumCount, goal.MaximumCount);
    }

    public static KsProcgenWindowPlanResult Plan(
        IReadOnlyList<KsProcgenWindowBoundaryCell> boundary,
        int seed,
        float exteriorWindowFraction = 0.25f,
        bool hardFraction = false,
        int windowToleranceCells = 1,
        int minimumWindowCells = 0,
        int? maximumWindowCells = null)
    {
        if (boundary == null || boundary.Count > 65_536 ||
            !float.IsFinite(exteriorWindowFraction) || exteriorWindowFraction is < 0f or > 1f ||
            windowToleranceCells < 0 || minimumWindowCells < 0 ||
            maximumWindowCells is < 0 ||
            maximumWindowCells.HasValue && maximumWindowCells.Value < minimumWindowCells ||
            boundary.Any(cell => cell == null || cell.IsWindow && !cell.Fixed) ||
            boundary.Select(cell => cell.Cell).Distinct().Count() != boundary.Count)
            return Failure(KsProcgenWindowPlanStatus.InvalidInput, "InvalidWindowBoundary");

        var eligible = new List<KsProcgenWindowBoundaryCell>();
        var excluded = new List<KsProcgenWindowExclusion>();
        foreach (var cell in boundary)
        {
            var reason = ExclusionReason(cell);
            if (reason.HasValue)
                excluded.Add(new KsProcgenWindowExclusion(cell.Cell, reason.Value));
            else
                eligible.Add(cell);
        }
        var orderedExclusions = excluded.OrderBy(item => item.Cell.Y)
            .ThenBy(item => item.Cell.X).ToArray();
        if (boundary.Any(cell => cell.Fixed && cell.IsWindow && !eligible.Contains(cell)))
            return Failure(KsProcgenWindowPlanStatus.InvalidInput, "IneligibleFixedWindow");

        var fixedWindows = eligible.Count(cell => cell.Fixed && cell.IsWindow);
        var fixedWalls = eligible.Count(cell => cell.Fixed && !cell.IsWindow);
        var editable = eligible.Where(cell => !cell.Fixed)
            .Select(cell => (cell.Cell, Rank: Rank(seed, cell.Cell)))
            .OrderBy(item => item.Rank).ThenBy(item => item.Cell.Y).ThenBy(item => item.Cell.X)
            .Select(item => item.Cell).ToArray();
        var requested = (int) Math.Floor((double) exteriorWindowFraction * eligible.Count + 0.5);
        var lower = Math.Max(fixedWindows, minimumWindowCells);
        var upper = Math.Min(fixedWindows + editable.Length, maximumWindowCells ?? int.MaxValue);
        if (eligible.Count == 0 && minimumWindowCells == 0 && !hardFraction)
            return new KsProcgenWindowPlanResult
            {
                Status = KsProcgenWindowPlanStatus.NotApplicable,
                BoundaryCells = boundary.Count,
                ExcludedCells = orderedExclusions,
                RequestedFraction = exteriorWindowFraction,
            };
        if (lower > upper)
            return new KsProcgenWindowPlanResult
            {
                Status = KsProcgenWindowPlanStatus.HardTargetUnmet,
                Issue = new KsProcgenIssue("WindowCountInfeasible",
                    "Fixed windows and editable cells cannot meet the window count bounds."),
                BoundaryCells = boundary.Count,
                EligibleCells = eligible.Count,
                FixedWindowCells = fixedWindows,
                FixedEligibleWallCells = fixedWalls,
                EditableEligibleCells = editable.Length,
                ExcludedCells = orderedExclusions,
                RequestedWindowCells = requested,
                RequestedFraction = exteriorWindowFraction,
            };

        var achieved = Math.Clamp(requested, lower, upper);
        var selected = editable.Take(achieved - fixedWindows)
            .OrderBy(cell => cell.Y).ThenBy(cell => cell.X).ToArray();
        var fractionUnmet = hardFraction && Math.Abs(achieved - requested) > windowToleranceCells;
        return new KsProcgenWindowPlanResult
        {
            Status = fractionUnmet ? KsProcgenWindowPlanStatus.HardTargetUnmet :
                eligible.Count == 0 ? KsProcgenWindowPlanStatus.NotApplicable :
                KsProcgenWindowPlanStatus.Planned,
            Issue = fractionUnmet ? new KsProcgenIssue("WindowFractionInfeasible",
                "The feasible window count misses the hard fraction beyond its tolerance.") : null,
            ChosenWindowCells = selected,
            BoundaryCells = boundary.Count,
            EligibleCells = eligible.Count,
            FixedWindowCells = fixedWindows,
            FixedEligibleWallCells = fixedWalls,
            EditableEligibleCells = editable.Length,
            ExcludedCells = orderedExclusions,
            RequestedWindowCells = requested,
            AchievedWindowCells = achieved,
            RequestedFraction = exteriorWindowFraction,
        };
    }

    private static KsProcgenWindowExclusionReason? ExclusionReason(KsProcgenWindowBoundaryCell cell)
    {
        if (!cell.FacesInterior) return KsProcgenWindowExclusionReason.NoInteriorFace;
        if (!cell.FacesExterior) return KsProcgenWindowExclusionReason.NoExteriorFace;
        if (!cell.SupportsAirtightWindow) return KsProcgenWindowExclusionReason.UnsupportedAirtightWindow;
        if (cell.IsCorner) return KsProcgenWindowExclusionReason.Corner;
        if (cell.IsDoorway) return KsProcgenWindowExclusionReason.Doorway;
        if (cell.RequiredStructure) return KsProcgenWindowExclusionReason.RequiredStructure;
        if (cell.WindowsDisabled) return KsProcgenWindowExclusionReason.DisabledByAuthor;
        return null;
    }

    private static ulong Rank(int seed, Vector2i cell)
    {
        var hash = KsProcgenStableHash.Create();
        hash.AddInt(seed);
        hash.AddString("exterior-window-cell");
        hash.AddInt(cell.X);
        hash.AddInt(cell.Y);
        return hash.Value;
    }

    private static KsProcgenWindowPlanResult Failure(KsProcgenWindowPlanStatus status, string code) => new()
    {
        Status = status,
        Issue = new KsProcgenIssue(code, "Exterior window boundary input is invalid."),
    };
}
