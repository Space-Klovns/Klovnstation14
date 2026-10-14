using System.Globalization;
using System.Linq;

namespace Content.Shared._KS14.Procedural;

public sealed record KsProcgenRoomAssemblyInstance(string PackId, string CoreId, int CopyIndex,
    KsProcgenResolvedAssembly Assembly);
public sealed record KsProcgenRoomMemberBinding(string MemberId, string PackId, string CoreId, int CopyIndex,
    string AssemblyId, string VariantId, string SourceMemberId);

public sealed class KsProcgenRoomAssemblyComposition
{
    public KsProcgenIssue? Issue { get; init; }
    public KsProcgenResolvedAssembly? Assembly { get; init; }
    public IReadOnlyList<KsProcgenRoomMemberBinding> Bindings { get; init; } = [];
    public bool EnginePlacementVerified => false;
}

/// <summary>
/// Copies complete selected cores into one namespaced assembly for joint support-aware pose search.
/// No member, relation target or support reservation can bind into another core instance by accident.
/// Optional members are included as selected; choosing optional members remains a separate policy.
/// </summary>
public static class KsProcgenRoomAssemblyComposer
{
    public static KsProcgenRoomAssemblyComposition Compose(string roomId,
        IReadOnlyList<KsProcgenRoomAssemblyInstance> instances)
    {
        KsProcgenRoomAssemblyComposition Fail(string code) => new()
            { Issue = new(code, "Room assembly composition requires bounded complete selected instances.") };
        if (string.IsNullOrWhiteSpace(roomId) || roomId.Length > 512 || instances == null ||
            instances.Count is < 1 or > 16 || instances.Any(instance => instance == null ||
                string.IsNullOrWhiteSpace(instance.PackId) || instance.PackId.Length > 256 ||
                string.IsNullOrWhiteSpace(instance.CoreId) || instance.CoreId.Length > 128 ||
                instance.CopyIndex is < 0 or > 63 || instance.Assembly == null))
            return Fail("InvalidRoomAssemblyInstances");
        if (instances.Select(instance => (instance.PackId, instance.CoreId, instance.CopyIndex)).Distinct().Count() != instances.Count)
            return Fail("DuplicateRoomAssemblyInstance");
        if (instances.Any(instance => instance.Assembly.Members == null || instance.Assembly.Relations == null ||
                instance.Assembly.Members.Count == 0) || instances.Sum(instance => (long) instance.Assembly.Members.Count) > 64 ||
            instances.Sum(instance => (long) instance.Assembly.Relations.Count) > 256)
            return Fail("RoomAssemblyCompositionBudget");
        var members = new List<KsProcgenResolvedAssemblyMember>();
        var relations = new List<KsProcgenResolvedAssemblyRelation>();
        var bindings = new List<KsProcgenRoomMemberBinding>();
        string? anchor = null;
        var identity = KsProcgenStableHash.Create();
        identity.AddString("ks-procgen-room-assembly-v1");
        identity.AddString(roomId);
        var ordered = instances.OrderBy(instance => instance.PackId, StringComparer.Ordinal)
            .ThenBy(instance => instance.CoreId, StringComparer.Ordinal).ThenBy(instance => instance.CopyIndex).ToArray();
        for (var instanceIndex = 0; instanceIndex < ordered.Length; instanceIndex++)
        {
            var instance = ordered[instanceIndex];
            var assembly = instance.Assembly;
            if (string.IsNullOrWhiteSpace(assembly.AssemblyId) || assembly.AssemblyId.Length > 256 ||
                string.IsNullOrWhiteSpace(assembly.VariantId) || assembly.VariantId.Length > 128 ||
                assembly.Members.Any(member => member == null || string.IsNullOrWhiteSpace(member.Id) || member.Id.Length > 256 ||
                    member.Entry == null || !KsProcgenAssemblyCompiler.ValidateEntry(member.Entry, _ => true) ||
                    !float.IsFinite(member.Entry.Weight) || member.Entry.Weight < 0f || member.Entry.MinimumCount is < 0 or > 64 ||
                    !Enum.IsDefined(member.RotationMode) || member.LocalQuarterTurns is < 0 or > 3) ||
                assembly.Members.Select(member => member.Id).Distinct(StringComparer.Ordinal).Count() != assembly.Members.Count ||
                !assembly.Members.Any(member => member.Id == assembly.AnchorMember))
                return Fail("InvalidRoomAssemblyMembers");
            var renamed = new Dictionary<string, string>(StringComparer.Ordinal);
            var sourceMembers = assembly.Members.OrderBy(member => member.Id, StringComparer.Ordinal).ToArray();
            for (var memberIndex = 0; memberIndex < sourceMembers.Length; memberIndex++)
                renamed.Add(sourceMembers[memberIndex].Id, $"I{instanceIndex:D2}_M{memberIndex:D2}");
            anchor ??= renamed[assembly.AnchorMember];
            identity.AddString(instance.PackId);
            identity.AddString(instance.CoreId);
            identity.AddInt(instance.CopyIndex);
            identity.AddString(assembly.AssemblyId);
            identity.AddString(assembly.VariantId);
            foreach (var member in sourceMembers)
            {
                var id = renamed[member.Id];
                var entry = member.Entry;
                members.Add(member with { Id = id, Entry = new()
                {
                    Id = id, Entity = entry.Entity, Weight = entry.Weight, MinimumCount = entry.MinimumCount,
                    Role = entry.Role, Movement = entry.Movement, Footprint = entry.Footprint.ToList(),
                    AllowedQuarterTurns = entry.AllowedQuarterTurns.ToList(),
                    RequiresInteractionApproach = entry.RequiresInteractionApproach,
                } });
                bindings.Add(new(id, instance.PackId, instance.CoreId, instance.CopyIndex,
                    assembly.AssemblyId, assembly.VariantId, member.Id));
            }
            if (assembly.Relations.Any(relation => relation == null || string.IsNullOrWhiteSpace(relation.Id) ||
                    string.IsNullOrWhiteSpace(relation.Subject) || !Enum.IsDefined(relation.Kind) ||
                    !Enum.IsDefined(relation.Severity) || !Enum.IsDefined(relation.ApproachPolicy) ||
                    relation.MinimumDistance < 0 || relation.MaximumDistance < relation.MinimumDistance || relation.MaximumDistance > 64 ||
                    !renamed.ContainsKey(relation.Subject) || relation.Target != null && !renamed.ContainsKey(relation.Target)) ||
                assembly.Relations.Select(relation => relation.Id).Distinct(StringComparer.Ordinal).Count() != assembly.Relations.Count)
                return Fail("InvalidRoomAssemblyRelations");
            var relationIndex = 0;
            foreach (var relation in assembly.Relations.OrderBy(relation => relation.Id, StringComparer.Ordinal))
                relations.Add(relation with
                {
                    Id = $"I{instanceIndex:D2}_R{relationIndex++:D3}", Subject = renamed[relation.Subject],
                    Target = relation.Target == null ? null : renamed[relation.Target],
                });
        }
        return new()
        {
            Assembly = new($"Room/{roomId}", identity.Value.ToString(CultureInfo.InvariantCulture), anchor!,
                KsProcgenReadOnly.Copy(members), KsProcgenReadOnly.Copy(relations)),
            Bindings = KsProcgenReadOnly.Copy(bindings),
        };
    }
}
