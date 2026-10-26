using System.Linq;

namespace Content.Shared._KS14.Procedural;

/// <summary>Canonical topological traversal: anchor support tree, required linked roots, then unrelated roots.</summary>
internal static class KsProcgenSupportedMemberOrder
{
    internal static IReadOnlyList<KsProcgenSupportMember> Order(KsProcgenAssemblySupportPlan support, KsProcgenResolvedAssembly assembly)
    {
        var byId = support.Members.ToDictionary(member => member.MemberId, StringComparer.Ordinal);
        var ordinal = support.Members.Select((member, index) => (member.MemberId, Index: index))
            .ToDictionary(item => item.MemberId, item => item.Index, StringComparer.Ordinal);
        var neighbors = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var member in support.Members) neighbors.TryAdd(member.FloorRootMemberId, new HashSet<string>(StringComparer.Ordinal));
        foreach (var relation in assembly.Relations.Where(relation => relation.Severity == KsProcgenRelationSeverity.Required && relation.Target != null))
        {
            if (!byId.TryGetValue(relation.Subject, out var subject) || !byId.TryGetValue(relation.Target!, out var target)) continue;
            if (subject.FloorRootMemberId == target.FloorRootMemberId) continue;
            neighbors[subject.FloorRootMemberId].Add(target.FloorRootMemberId);
            neighbors[target.FloorRootMemberId].Add(subject.FloorRootMemberId);
        }
        var anchor = byId[assembly.AnchorMember].FloorRootMemberId;
        var selectedIds = new HashSet<string>(StringComparer.Ordinal);
        var selectedRoots = new HashSet<string>(StringComparer.Ordinal);
        var pending = support.Members.ToList();
        var result = new List<KsProcgenSupportMember>();
        while (pending.Count > 0)
        {
            var next = pending.Where(member => member.ParentMemberId == null || selectedIds.Contains(member.ParentMemberId))
                .OrderBy(member => member.FloorRootMemberId == anchor ? 0 : neighbors[member.FloorRootMemberId].Overlaps(selectedRoots) ? 1 : 2)
                .ThenBy(member => ordinal[member.MemberId]).First();
            result.Add(next); pending.Remove(next); selectedIds.Add(next.MemberId); selectedRoots.Add(next.FloorRootMemberId);
        }
        return KsProcgenReadOnly.Copy(result);
    }
}
