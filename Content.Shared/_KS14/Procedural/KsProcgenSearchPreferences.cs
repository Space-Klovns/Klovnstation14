using System.Linq;
using System.Numerics;

namespace Content.Shared._KS14.Procedural;

public sealed record KsProcgenRootDistancePreference(string Subject, string Target, int MinimumDistance, int MaximumDistance);
/// <summary>Observed Manhattan anchor separation, not a walkable-path distance.</summary>
public sealed record KsProcgenRootDistanceOutcome(string Subject, string Target, int MinimumDistance, int MaximumDistance,
    long Distance, long Penalty);

/// <summary>Lower missed-relation and distance penalties win; exact ties retain seeded traversal order.</summary>
public readonly record struct KsProcgenPreferenceScore(int MissedRelations, long DistancePenalty)
{
    public bool Satisfied => MissedRelations == 0 && DistancePenalty == 0;
    public bool BetterThan(KsProcgenPreferenceScore other) => MissedRelations < other.MissedRelations ||
        MissedRelations == other.MissedRelations && DistancePenalty < other.DistancePenalty;
}

/// <summary>One search tree's extra work after its first fully validated hard witness. Never refunds work.</summary>
public sealed class KsProcgenPreferenceBudget
{
    public KsProcgenPreferenceBudget(int maximumSelectionProbes = 32, int maximumPlacementProbes = 128,
        int maximumExpandedCells = 2048, int maximumLandingProbes = 128)
    {
        MaximumSelectionProbes = maximumSelectionProbes; MaximumPlacementProbes = maximumPlacementProbes;
        MaximumExpandedCells = maximumExpandedCells; MaximumLandingProbes = maximumLandingProbes;
    }
    public int MaximumSelectionProbes { get; }
    public int MaximumPlacementProbes { get; }
    public int MaximumExpandedCells { get; }
    public int MaximumLandingProbes { get; }
    public bool Active { get; private set; }
    public int SelectionProbes { get; private set; }
    public int PlacementProbes { get; private set; }
    public int ExpandedCells { get; private set; }
    public int LandingProbes { get; private set; }
    internal bool Valid => MaximumSelectionProbes is >= 0 and <= 65_536 && MaximumPlacementProbes is >= 0 and <= 65_536 &&
        MaximumExpandedCells is >= 0 and <= 65_536 && MaximumLandingProbes is >= 0 and <= 65_536;
    internal int RemainingExpanded(int hardRemaining) => Active ? Math.Min(hardRemaining, MaximumExpandedCells - ExpandedCells) : hardRemaining;
    internal int RemainingLandings(int hardRemaining) => Active ? Math.Min(hardRemaining, MaximumLandingProbes - LandingProbes) : hardRemaining;
    internal void Activate() => Active = true;
    internal bool ChargeSelection()
    {
        if (!Active) return true;
        if (SelectionProbes >= MaximumSelectionProbes) return false;
        SelectionProbes++; return true;
    }
    internal bool ChargePlacement()
    {
        if (!Active) return true;
        if (PlacementProbes >= MaximumPlacementProbes) return false;
        PlacementProbes++; return true;
    }
    internal void ChargeGraph(int expanded, int landings)
    {
        if (!Active) return;
        ExpandedCells += expanded; LandingProbes += landings;
    }
}

/// <summary>Soft Manhattan proximity of initial authored groups and spacing of repeated floor groups.</summary>
public sealed record KsProcgenRoomPreferencePolicy(string DominantPackId, int Proximity = 4, int RepeatSpacing = 3)
{
    internal bool Valid => !string.IsNullOrWhiteSpace(DominantPackId) && DominantPackId.Length <= 256 &&
        Proximity is >= 0 and <= 64 && RepeatSpacing is >= 0 and <= 64;

    internal IReadOnlyList<KsProcgenRootDistancePreference> Compile(KsProcgenRoomAssemblyComposition composition,
        IReadOnlyList<KsProcgenRoomCoreSelection> selections, IReadOnlyList<KsProcgenRoomAssemblyInstance> instances,
        KsProcgenAssemblyCapabilityReport capabilities)
    {
        var assembly = composition.Assembly!;
        var support = KsProcgenAssemblySupportPlanner.Plan(assembly, assembly.Members.Select(member => member.Id).ToArray(), capabilities);
        if (support.Status != KsProcgenAssemblySupportStatus.NeedsEngineValidation) return [];
        var supports = support.Members.ToDictionary(member => member.MemberId, StringComparer.Ordinal);
        var groups = new List<(string Root, string Pack, bool Repeat, bool Floor, bool Required, string Core, int Copy)>();
        foreach (var instance in composition.Bindings.Where(binding => binding.Origin == KsProcgenRoomMemberOrigin.Authored)
                     .GroupBy(binding => (binding.PackId, binding.CoreId, binding.CopyIndex)))
        {
            var selection = selections.Single(item => item.Origin == KsProcgenRoomMemberOrigin.Authored &&
                item.PackId == instance.Key.PackId && item.Core.Id == instance.Key.CoreId && item.CopyIndex == instance.Key.CopyIndex);
            var sourceAssembly = instances.Single(item => item.Origin == KsProcgenRoomMemberOrigin.Authored &&
                item.PackId == instance.Key.PackId && item.CoreId == instance.Key.CoreId && item.CopyIndex == instance.Key.CopyIndex).Assembly;
            var anchor = instance.Single(binding => binding.SourceMemberId == sourceAssembly.AnchorMember);
            var root = supports[anchor.MemberId].FloorRootMemberId;
            groups.Add((root, instance.Key.PackId, instance.Key.CopyIndex >= Math.Max(1, selection.Core.MinimumCount),
                supports[root].Layer == KsProcgenPlacementLayer.Floor, selection.Core.MinimumCount > 0, instance.Key.CoreId, instance.Key.CopyIndex));
        }
        var ordered = groups.OrderBy(group => group.Pack == DominantPackId ? 0 : 1).ThenBy(group => group.Required ? 0 : 1)
            .ThenBy(group => group.Core, StringComparer.Ordinal).ThenBy(group => group.Copy).ThenBy(group => group.Pack, StringComparer.Ordinal).ToArray();
        var dominant = ordered.Where(group => group.Pack == DominantPackId).ToArray();
        if (dominant.Length == 0) return [];
        var pairs = new HashSet<(string Subject, string Target, int Minimum, int Maximum)>();
        void Add(string first, string second, int minimum, int maximum)
        {
            if (first == second) return;
            var pair = StringComparer.Ordinal.Compare(first, second) < 0 ? (first, second) : (second, first);
            pairs.Add((pair.Item1, pair.Item2, minimum, maximum));
        }
        foreach (var group in ordered.Where(group => !group.Repeat)) Add(dominant[0].Root, group.Root, 0, Proximity);
        for (var first = 0; first < dominant.Length; first++)
        for (var second = first + 1; second < dominant.Length; second++)
            if (dominant[first].Floor && dominant[second].Floor && (dominant[first].Repeat || dominant[second].Repeat))
                Add(dominant[first].Root, dominant[second].Root, RepeatSpacing, 64);
        return pairs.OrderBy(pair => pair.Subject, StringComparer.Ordinal).ThenBy(pair => pair.Target, StringComparer.Ordinal)
            .ThenBy(pair => pair.Minimum).ThenBy(pair => pair.Maximum)
            .Select(pair => new KsProcgenRootDistancePreference(pair.Subject, pair.Target, pair.Minimum, pair.Maximum)).ToArray();
    }
}

internal static class KsProcgenPreferenceScoring
{
    internal static long DistancePenalty(Vector2 first, Vector2 second, KsProcgenRootDistancePreference preference)
    {
        var distance = (long) Math.Abs(first.X - second.X) + (long) Math.Abs(first.Y - second.Y);
        return Math.Max(0L, preference.MinimumDistance - distance) + Math.Max(0L, distance - preference.MaximumDistance);
    }
}
