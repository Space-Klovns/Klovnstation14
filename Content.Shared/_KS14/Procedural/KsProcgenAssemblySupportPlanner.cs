using System.Linq;

namespace Content.Shared._KS14.Procedural;

public enum KsProcgenAssemblySupportStatus : byte
{
    NeedsEngineValidation,
    InvalidInput,
    Rejected,
}

public enum KsProcgenPlacementLayer : byte
{
    Floor,
    Surface,
    Container,
}

/// <summary>Dependency metadata only; no transform, usable interaction pose or legal overlap is established.</summary>
public sealed record KsProcgenSupportMember(
    string MemberId, string EntityPrototypeId, KsProcgenPlacementLayer Layer,
    string? ParentMemberId, string? RelationId, string? SlotId,
    string FloorRootMemberId, int Depth, bool Exposed, ulong DeclarationHash);

public sealed record KsProcgenSupportReservation(
    string ParentMemberId, KsProcgenPlacementLayer Layer, string? SlotId,
    int ClaimCount, int? DeclaredCapacity);

public sealed class KsProcgenAssemblySupportPlan
{
    public KsProcgenAssemblySupportStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public IReadOnlyList<KsProcgenSupportMember> Members { get; init; } = [];
    public IReadOnlyList<KsProcgenSupportReservation> Reservations { get; init; } = [];
    public IReadOnlyList<KsProcgenAssemblyCapabilityWitness> SupportRelations { get; init; } = [];
    public ulong StructureHash { get; init; }
    public bool EnginePlacementVerified => false;
}

/// <summary>
/// Canonical dependency order and declaration-only claims for one selected complete variant.
/// Rejection is atomic. Unknown surface/container capacity never becomes inferred capacity one.
/// This does not authorize the floor solver or entity stage to accept supported placements.
/// </summary>
public static class KsProcgenAssemblySupportPlanner
{
    public static KsProcgenAssemblySupportPlan Plan(KsProcgenResolvedAssembly assembly,
        IReadOnlyCollection<string> selectedMemberIds, KsProcgenAssemblyCapabilityReport capabilities)
    {
        if (assembly.Members.Count is < 1 or > 64 || assembly.Relations.Count > 256 ||
            selectedMemberIds.Count is < 1 or > 64 || capabilities.Members.Count != assembly.Members.Count ||
            assembly.Members.Any(member => string.IsNullOrWhiteSpace(member.Id)) ||
            assembly.Members.Select(member => member.Id).Distinct(StringComparer.Ordinal).Count() != assembly.Members.Count ||
            assembly.Relations.Any(relation => string.IsNullOrWhiteSpace(relation.Id)) ||
            assembly.Relations.Select(relation => relation.Id).Distinct(StringComparer.Ordinal).Count() != assembly.Relations.Count ||
            capabilities.Members.Select(member => member.MemberId).Distinct(StringComparer.Ordinal).Count() != capabilities.Members.Count)
            return Failure("InvalidSupportAssembly", invalidInput: true);
        var members = assembly.Members.ToDictionary(member => member.Id, StringComparer.Ordinal);
        var inspected = capabilities.Members.ToDictionary(member => member.MemberId, member => member.Capabilities,
            StringComparer.Ordinal);
        if (members.Any(pair => !inspected.TryGetValue(pair.Key, out var declaration) ||
                declaration.PrototypeId != pair.Value.Entry.Entity || declaration.Fixtures.Count > 64 || declaration.Containers.Count > 64 ||
                declaration.Containers.Select(container => container.Id).Distinct(StringComparer.Ordinal).Count() != declaration.Containers.Count))
            return Failure("SupportCapabilityMismatch", invalidInput: true);
        var selected = new HashSet<string>(selectedMemberIds, StringComparer.Ordinal);
        if (selected.Count != selectedMemberIds.Count || selected.Any(id => !members.ContainsKey(id)) ||
            !selected.Contains(assembly.AnchorMember) || members.Values.Any(member => member.Required && !selected.Contains(member.Id)))
            return Failure("InvalidSupportSelection", invalidInput: true);

        var parents = new Dictionary<string, KsProcgenResolvedAssemblyRelation>(StringComparer.Ordinal);
        foreach (var relation in assembly.Relations.Where(relation => relation.Kind is
                     KsProcgenRelationKind.OnSurface or KsProcgenRelationKind.InContainer))
        {
            if (string.IsNullOrWhiteSpace(relation.Id) || !members.ContainsKey(relation.Subject) ||
                relation.Target == null || !members.ContainsKey(relation.Target) ||
                relation.Severity != KsProcgenRelationSeverity.Required ||
                relation.Kind == KsProcgenRelationKind.InContainer && string.IsNullOrWhiteSpace(relation.ContainerId) ||
                !parents.TryAdd(relation.Subject, relation))
                return Failure("InvalidSupportDependency", invalidInput: true);
            if (selected.Contains(relation.Subject) && !selected.Contains(relation.Target))
                return Failure("MissingSelectedSupportParent");
        }

        var witnesses = new List<KsProcgenAssemblyCapabilityWitness>();
        foreach (var relation in parents.Values.Where(relation => selected.Contains(relation.Subject))
                     .OrderBy(relation => relation.Id, StringComparer.Ordinal))
        {
            // Recompute from declarations instead of trusting a stale or mismatched supplied witness.
            var inspection = KsProcgenSupportCapabilityInspector.Inspect(inspected[relation.Subject],
                inspected[relation.Target!], relation.Kind,
                slotId: relation.Kind == KsProcgenRelationKind.OnSurface ? relation.Slot : relation.ContainerId);
            if (inspection.State == KsProcgenSupportInspectionState.Rejected)
                return Failure(inspection.Reason);
            witnesses.Add(new KsProcgenAssemblyCapabilityWitness(relation.Id, relation.Subject, relation.Target!,
                relation.Kind, inspection));
        }

        var ordered = new List<KsProcgenSupportMember>();
        var resolved = new Dictionary<string, KsProcgenSupportMember>(StringComparer.Ordinal);
        var pending = new SortedSet<string>(selected, StringComparer.Ordinal);
        while (pending.Count > 0)
        {
            var next = pending.FirstOrDefault(id => !parents.TryGetValue(id, out var relation) || resolved.ContainsKey(relation.Target!));
            if (next == null)
                return Failure("AssemblySupportCycle");
            parents.TryGetValue(next, out var dependency);
            var parent = dependency == null ? null : resolved[dependency.Target!];
            var layer = dependency == null ? KsProcgenPlacementLayer.Floor :
                dependency.Kind == KsProcgenRelationKind.OnSurface ? KsProcgenPlacementLayer.Surface : KsProcgenPlacementLayer.Container;
            var entry = new KsProcgenSupportMember(next, members[next].Entry.Entity, layer, parent?.MemberId,
                dependency?.Id, layer == KsProcgenPlacementLayer.Container ? dependency?.ContainerId : dependency?.Slot,
                parent?.FloorRootMemberId ?? next, parent == null ? 0 : parent.Depth + 1,
                layer != KsProcgenPlacementLayer.Container && (parent?.Exposed ?? true), inspected[next].DeclarationHash);
            ordered.Add(entry);
            resolved.Add(next, entry);
            pending.Remove(next);
        }

        var reservations = new List<KsProcgenSupportReservation>();
        foreach (var group in ordered.Where(member => member.ParentMemberId != null)
                     .GroupBy(member => (member.ParentMemberId, member.Layer, member.SlotId))
                     .OrderBy(group => group.Key.ParentMemberId, StringComparer.Ordinal).ThenBy(group => group.Key.Layer)
                     .ThenBy(group => group.Key.SlotId, StringComparer.Ordinal))
        {
            int? capacity = group.Key.Layer == KsProcgenPlacementLayer.Container ?
                inspected[group.Key.ParentMemberId!].Containers.Single(container => container.Id == group.Key.SlotId).DeclaredCapacity :
                group.Key.SlotId == null ? inspected[group.Key.ParentMemberId!].SurfaceTracker?.DeclaredCapacity : null;
            var initialClaims = group.Key.Layer == KsProcgenPlacementLayer.Surface && group.Key.SlotId == null ?
                inspected[group.Key.ParentMemberId!].SurfaceTracker?.InitiallyTrackedEntities ?? 0 : 0;
            if (capacity != null && (long) group.Count() + initialClaims > capacity)
                return Failure("AssemblySupportCapacityConflict");
            reservations.Add(new KsProcgenSupportReservation(group.Key.ParentMemberId!, group.Key.Layer,
                group.Key.SlotId, group.Count(), capacity));
        }
        return new KsProcgenAssemblySupportPlan
        {
            Status = KsProcgenAssemblySupportStatus.NeedsEngineValidation,
            Members = ordered.AsReadOnly(), Reservations = reservations.AsReadOnly(),
            SupportRelations = witnesses.AsReadOnly(), StructureHash = Hash(assembly, ordered, reservations),
        };
    }

    private static KsProcgenAssemblySupportPlan Failure(string code, bool invalidInput = false) => new()
    {
        Status = invalidInput ? KsProcgenAssemblySupportStatus.InvalidInput : KsProcgenAssemblySupportStatus.Rejected,
        Issue = new KsProcgenIssue(code, "Assembly support structure cannot be accepted."),
    };

    private static ulong Hash(KsProcgenResolvedAssembly assembly, IReadOnlyList<KsProcgenSupportMember> members,
        IReadOnlyList<KsProcgenSupportReservation> reservations)
    {
        var hash = KsProcgenStableHash.Create();
        hash.AddString("ks-procgen-support-structure-v1");
        hash.AddString(assembly.AssemblyId);
        hash.AddString(assembly.VariantId);
        hash.AddString(assembly.AnchorMember);
        hash.AddInt(members.Count);
        foreach (var member in members)
        {
            hash.AddString(member.MemberId);
            hash.AddString(member.EntityPrototypeId);
            hash.AddInt((int) member.Layer);
            hash.AddString(member.ParentMemberId ?? string.Empty);
            hash.AddString(member.RelationId ?? string.Empty);
            hash.AddString(member.SlotId ?? string.Empty);
            hash.AddString(member.FloorRootMemberId);
            hash.AddInt(member.Depth);
            hash.AddInt(member.Exposed ? 1 : 0);
            hash.AddInt(unchecked((int) member.DeclarationHash));
            hash.AddInt(unchecked((int) (member.DeclarationHash >> 32)));
        }
        hash.AddInt(reservations.Count);
        foreach (var reservation in reservations)
        {
            hash.AddString(reservation.ParentMemberId);
            hash.AddInt((int) reservation.Layer);
            hash.AddString(reservation.SlotId ?? string.Empty);
            hash.AddInt(reservation.ClaimCount);
            hash.AddInt(reservation.DeclaredCapacity ?? -1);
        }
        return hash.Value;
    }
}
