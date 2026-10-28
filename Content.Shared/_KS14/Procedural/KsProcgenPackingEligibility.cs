using System.Collections.Frozen;
using System.Linq;

namespace Content.Shared._KS14.Procedural;

/// <summary>A whole layout alternative at one family rotation, independent of its translation.</summary>
public readonly record struct KsProcgenLayoutOrientation(string FamilyId, string OptionId, int QuarterTurns);

/// <summary>
/// Detached eligibility constraints for geometric selection. These do not certify source contents,
/// mounting, access or materialization. An omitted eligibility object leaves all orientations eligible.
/// </summary>
public sealed class KsProcgenPackingEligibility
{
    public const int MaximumOrientations = 4096;
    private readonly FrozenSet<KsProcgenLayoutOrientation> _allowed;
    private KsProcgenPackingEligibility(KsProcgenLayoutOrientation[] orientations)
    {
        Orientations = orientations.ToReadOnlyList();
        _allowed = orientations.ToFrozenSet();
    }

    public IReadOnlyList<KsProcgenLayoutOrientation> Orientations { get; }

    public static bool TryCreate(IReadOnlyCollection<KsProcgenLayoutOrientation> orientations,
        out KsProcgenPackingEligibility? eligibility, out KsProcgenIssue? issue)
    {
        eligibility = null; issue = null;
        if (orientations == null || orientations.Count > MaximumOrientations)
        { issue = new("InvalidPackingEligibility", "Eligibility needs a bounded collection of whole layout orientations."); return false; }
        var copy = orientations.ToArray();
        if (copy.Any(item => string.IsNullOrWhiteSpace(item.FamilyId) || string.IsNullOrWhiteSpace(item.OptionId) ||
                item.QuarterTurns is < 0 or > 3) || copy.Distinct().Count() != copy.Length)
        { issue = new("InvalidPackingEligibility", "Eligible orientations need unique complete identities and cardinal rotations."); return false; }
        eligibility = new(copy.OrderBy(item => item.FamilyId, StringComparer.Ordinal)
            .ThenBy(item => item.OptionId, StringComparer.Ordinal).ThenBy(item => item.QuarterTurns).ToArray());
        return true;
    }

    internal bool Allows(KsProcgenPlacementCandidate candidate) =>
        _allowed.Contains(new(candidate.FamilyId, candidate.OptionId, candidate.QuarterTurns));

    internal bool Matches(IReadOnlyList<KsProcgenLayoutFamily> families)
    {
        var byId = families.ToDictionary(family => family.Id, StringComparer.Ordinal);
        return Orientations.All(item => byId.TryGetValue(item.FamilyId, out var family) &&
            family.AllowedQuarterTurns.Contains(item.QuarterTurns) && family.Options.Any(option => option.Id == item.OptionId));
    }
}
