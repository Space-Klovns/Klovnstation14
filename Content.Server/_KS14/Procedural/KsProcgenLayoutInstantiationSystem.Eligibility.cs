using System.Linq;
using System.Threading;
using Content.Shared._KS14.Procedural;

namespace Content.Server._KS14.Procedural;

internal sealed record KsProcgenRejectedLayoutOrientation(KsProcgenLayoutOrientation Orientation, string RoomAssetId, string Reason);
internal sealed record KsProcgenNativeLayoutEligibility(KsProcgenPackingEligibility Packing,
    IReadOnlyList<KsProcgenRejectedLayoutOrientation> Rejected, int OrientationProbes, int MemberProbes);

public sealed partial class KsProcgenLayoutInstantiationSystem
{
    /// <summary>
    /// Exclude whole option/rotation pairs with incompatible captured poses. Source component,
    /// terrain, physical access, support and runtime checks still run on the selected complete plan.
    /// </summary>
    private bool TryEligibility(KsProcgenCompiledLibrary library, IReadOnlyList<KsProcgenLayoutRoomBinding> roomBindings,
        KsProcgenRoomEntityExtraction extraction, int maximumOrientationProbes, int maximumMemberProbes,
        out KsProcgenNativeLayoutEligibility? eligibility, out KsProcgenIssue? issue, CancellationToken cancellationToken)
    {
        eligibility = null; issue = null;
        var allowed = new List<KsProcgenLayoutOrientation>(); var rejected = new List<KsProcgenRejectedLayoutOrientation>();
        var orientations = 0; var members = 0;
        var bindingsByOption = roomBindings.GroupBy(binding => (binding.FamilyId, binding.OptionId))
            .ToDictionary(group => group.Key, group => group.OrderBy(binding => binding.MemberId, StringComparer.Ordinal).ToArray());
        foreach (var family in library.Families.OrderBy(family => family.Id, StringComparer.Ordinal))
        foreach (var option in family.Options.OrderBy(option => option.Id, StringComparer.Ordinal))
        {
            var bindings = bindingsByOption.GetValueOrDefault((family.Id, option.Id)) ?? [];
            if (bindings.Length != option.Rooms.Count || !bindings.Select(binding => binding.MemberId).ToHashSet(StringComparer.Ordinal)
                    .SetEquals(option.Rooms.Select(room => room.Id)))
                return Reject("LayoutInstantiationMissingBindings", "Each alternative needs exactly its complete source member bindings.", out issue);
            foreach (var turns in family.AllowedQuarterTurns.OrderBy(turns => turns))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (orientations >= maximumOrientationProbes)
                    return Reject("LayoutEligibilityOrientationBudget", "Source eligibility exceeded its shared orientation allowance.", out issue);
                orientations++;
                var orientation = new KsProcgenLayoutOrientation(family.Id, option.Id, turns);
                var accepted = true;
                foreach (var binding in bindings)
                {
                    var origin = KsProcgenGeometry.TransformCell(binding.Origin, default, default, turns);
                    var ready = _rooms.TryCheckPlacement(extraction, binding.RoomAssetId, origin,
                        (binding.QuarterTurns + turns) % 4, maximumMemberProbes - members,
                        out var used, out issue, cancellationToken: cancellationToken);
                    members += used;
                    if (ready) continue;
                    if (issue?.Code == "RoomPlacementProbeBudget")
                        return Reject("LayoutEligibilityMemberBudget", "Source eligibility exceeded its shared member allowance.", out issue);
                    if (issue?.Code is not ("RoomInstantiationRotationUnsupported" or "RoomInstantiationAnchoredPoseUnsupported")) return false;
                    rejected.Add(new(orientation, binding.RoomAssetId, issue.Code));
                    issue = null; accepted = false; break;
                }
                if (accepted) allowed.Add(orientation);
            }
        }
        if (!KsProcgenPackingEligibility.TryCreate(allowed, out var packing, out issue)) return false;
        eligibility = new(packing!, rejected.AsReadOnly(), orientations, members); return true;
    }
}
