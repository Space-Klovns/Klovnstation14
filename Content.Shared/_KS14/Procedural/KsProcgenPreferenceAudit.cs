using System.Linq;

namespace Content.Shared._KS14.Procedural;

/// <summary>Checks automatic intent against captured authored content and independently revalidated poses.</summary>
public static class KsProcgenPreferenceAudit
{
    public static bool TryVerify(KsProcgenThemedRegion region, KsProcgenRoomAssemblyComposition composition,
        KsProcgenAssemblyCapabilityReport capabilities, KsProcgenSupportedAssemblySearchResult placement,
        KsProcgenSupportedAccessPlan actualAccess, out KsProcgenIssue? issue)
    {
        issue = new("StaleLayeredPreferenceEvidence", "Selected source variants, automatic distances or preference score changed.");
        if (region?.Theme.Content == null || composition?.Assembly == null || composition.Instances == null || composition.Bindings == null ||
            capabilities == null || composition.Instances.Count is < 1 or > 16 ||
            placement?.Status != KsProcgenSupportedAccessStatus.Candidate || actualAccess?.Status != KsProcgenSupportedAccessStatus.Candidate ||
            actualAccess.Poses == null || placement.RootPreferenceOutcomes == null || placement.RootPreferenceOutcomes.Count > 256 ||
            placement.Access?.AccessHash != actualAccess.AccessHash)
            return false;
        var rebuilt = KsProcgenRoomAssemblyComposer.Compose(region.Id, composition.Instances);
        if (!KsProcgenAssemblyValue.Same(rebuilt.Assembly, composition.Assembly) || !rebuilt.Bindings.SequenceEqual(composition.Bindings))
            return false;
        var poses = KsProcgenAssemblyPosePlanner.Plan(composition.Assembly, composition.Assembly.Members.Select(member => member.Id).ToArray(),
            capabilities, placement.FloorRoots, placement.Orientations, placement.AssemblyQuarterTurns);
        if (poses.Status != KsProcgenAssemblySupportStatus.NeedsEngineValidation || poses.PoseHash != actualAccess.Poses.PoseHash)
            return false;
        var selectedPackIds = region.Theme.SupportingEntityPackIds.Concat(region.UnplacedRequiredPacks.Select(pack => pack.PackId))
            .Concat(region.Theme.DominantEntityPackId is { } packId ? new[] { packId } : []).ToHashSet(StringComparer.Ordinal);
        var selections = new List<KsProcgenRoomCoreSelection>();
        foreach (var instance in composition.Instances.Where(instance => instance.Origin == KsProcgenRoomMemberOrigin.Authored))
        {
            var packs = region.Theme.Content.EntityPacks.Where(pack => pack.Id == instance.PackId && selectedPackIds.Contains(pack.Id)).ToArray();
            if (packs.Length != 1) return false;
            var cores = packs[0].Cores.Where(core => core.Id == instance.CoreId).ToArray();
            if (cores.Length != 1 || !cores[0].Variants.Any(variant => KsProcgenAssemblyValue.Same(variant, instance.Assembly))) return false;
            selections.Add(new(instance.PackId, cores[0], instance.CopyIndex));
        }
        if (selections.GroupBy(selection => (selection.PackId, selection.Core.Id)).Any(group => group.Count() < group.First().Core.MinimumCount))
            return false;
        var expected = region.Theme.Content.Goals.CoherentContents && region.Theme.DominantEntityPackId is { } dominant
            ? new KsProcgenRoomPreferencePolicy(dominant).Compile(composition, selections, composition.Instances, capabilities)
            : Array.Empty<KsProcgenRootDistancePreference>();
        if (expected.Count != placement.RootPreferenceOutcomes.Count) return false;
        var roots = actualAccess.Poses.Members.Where(pose => pose.Support.Layer is KsProcgenPlacementLayer.Floor or KsProcgenPlacementLayer.Wall)
            .ToDictionary(pose => pose.Support.MemberId, pose => pose.Position, StringComparer.Ordinal);
        var totalPenalty = 0L;
        for (var index = 0; index < expected.Count; index++)
        {
            var preference = expected[index];
            if (!roots.TryGetValue(preference.Subject, out var subject) || !roots.TryGetValue(preference.Target, out var target)) return false;
            var distance = (long) Math.Abs(subject.X - target.X) + (long) Math.Abs(subject.Y - target.Y);
            var penalty = distance < preference.MinimumDistance ? preference.MinimumDistance - distance :
                distance > preference.MaximumDistance ? distance - preference.MaximumDistance : 0L;
            var observed = placement.RootPreferenceOutcomes[index];
            if (observed != new KsProcgenRootDistanceOutcome(preference.Subject, preference.Target, preference.MinimumDistance,
                    preference.MaximumDistance, distance, penalty)) return false;
            totalPenalty += penalty;
        }
        if (placement.PreferenceScore != new KsProcgenPreferenceScore(actualAccess.PreferredRelationsApplicable - actualAccess.PreferredRelationsSatisfied, totalPenalty))
            return false;
        issue = null;
        return true;
    }
}
