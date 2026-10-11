using System.Linq;

namespace Content.Shared._KS14.Procedural;

/// <summary>Authored automatic zoning preferences. Work allowances remain caller controlled.</summary>
[DataDefinition]
public sealed partial class KsProcgenRoomSubdivisionSpec
{
    [DataField] public int MinimumRoomCells = 4;
    [DataField] public int PreferredMaximumRoomCells = 24;
    [DataField] public List<int> AlternativeMaximumRoomCells = new();
    /// <summary>Try a complete rectangular strip layout satisfying final room-size counts.</summary>
    [DataField] public bool UseSizeGoals;
}

/// <summary>Detached profile value; alternative order is semantically significant.</summary>
public sealed record KsProcgenRoomSubdivisionProfile(int MinimumRoomCells, int PreferredMaximumRoomCells,
    IReadOnlyList<int> AlternativeMaximumRoomCells)
{
    public bool UseSizeGoals { get; init; }
}

public static class KsProcgenRoomSubdivision
{
    internal static bool Valid(int minimum, int preferred, IReadOnlyList<int>? alternatives) =>
        minimum >= 1 && preferred >= minimum && preferred <= 4_096 && alternatives != null &&
        alternatives.Count <= 63 && alternatives.All(size => size >= minimum && size <= 4_096 && size != preferred) &&
        alternatives.Distinct().Count() == alternatives.Count;

    public static bool TrySnapshot(KsProcgenRoomSubdivisionSpec spec, out KsProcgenRoomSubdivisionProfile? profile)
    {
        profile = null;
        if (spec == null || !Valid(spec.MinimumRoomCells, spec.PreferredMaximumRoomCells, spec.AlternativeMaximumRoomCells))
            return false;
        profile = new(spec.MinimumRoomCells, spec.PreferredMaximumRoomCells,
            KsProcgenReadOnly.Copy(spec.AlternativeMaximumRoomCells)) { UseSizeGoals = spec.UseSizeGoals };
        return true;
    }
}
