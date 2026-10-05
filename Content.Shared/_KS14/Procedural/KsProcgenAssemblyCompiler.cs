using System.Globalization;
using System.Linq;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Procedural;

public sealed record KsProcgenResolvedAssemblyMember(
    string Id,
    string SourceMemberId,
    bool Required,
    KsProcgenEntityEntry Entry,
    KsProcgenMemberRotation RotationMode,
    int LocalQuarterTurns,
    Vector2i? ApproachLanding = null);

public sealed record KsProcgenResolvedAssemblyRelation(
    string Id,
    string Subject,
    KsProcgenRelationKind Kind,
    string? Target,
    KsProcgenRelationSeverity Severity,
    int MinimumDistance,
    int MaximumDistance,
    string? Slot,
    string? ContainerId,
    KsProcgenApproachPolicy ApproachPolicy);

public sealed record KsProcgenResolvedAssembly(
    string AssemblyId,
    string VariantId,
    string AnchorMember,
    IReadOnlyList<KsProcgenResolvedAssemblyMember> Members,
    IReadOnlyList<KsProcgenResolvedAssemblyRelation> Relations);

public sealed record KsProcgenResolvedEntityCore(
    string Id,
    float Weight,
    int MinimumCount,
    IReadOnlyList<KsProcgenResolvedAssembly> Variants);

/// <summary>Bounded schema compilation only. Support and placement capabilities remain unverified.</summary>
public static class KsProcgenAssemblyCompiler
{
    public const int MaximumNameLength = 128;
    public const int MaximumBindings = 64;
    public const int MaximumPrototypeIdLength = 256;
    public const int MaximumVariants = 17; // The base declaration plus sixteen complete alternatives.

    public static bool TryResolvePack(
        IPrototypeManager prototypeManager,
        KsProcgenEntityPackPrototype pack,
        out IReadOnlyList<KsProcgenResolvedEntityCore> cores,
        out KsProcgenIssue? issue)
    {
        cores = [];
        issue = null;
        if (pack == null || pack.Entries == null || pack.Assemblies == null ||
            pack.Entries.Count + pack.Assemblies.Count is < 1 or > 64)
            return Fail("AssemblyPackBudget", out issue);
        var compiled = new List<KsProcgenResolvedEntityCore>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in pack.Entries)
        {
            if (entry == null || !ValidName(entry.Id) || !seen.Add(entry.Id) || !float.IsFinite(entry.Weight) ||
                entry.Weight < 0f || entry.MinimumCount is < 0 or > 64 ||
                entry.MinimumCount > 0 && entry.Weight == 0f ||
                !ValidateEntry(entry, id => prototypeManager.TryIndex<EntityPrototype>(id, out _)))
                return Fail("InvalidEntityEntry", out issue);
            compiled.Add(new KsProcgenResolvedEntityCore(entry.Id, entry.Weight, entry.MinimumCount,
                [CompileSingleton(entry)]));
        }
        foreach (var reference in pack.Assemblies)
        {
            if (reference == null || !ValidName(reference.Id) || !seen.Add(reference.Id) ||
                !TryResolve(prototypeManager, reference, out var variants, out issue))
            {
                issue ??= new KsProcgenIssue("InvalidAssemblyReference", "Assembly core IDs must be unique.");
                return false;
            }
            compiled.Add(new KsProcgenResolvedEntityCore(reference.Id, reference.Weight,
                reference.MinimumCount, variants));
        }
        if (!compiled.Any(core => core.Weight > 0f))
            return Fail("EmptyPackChoice", out issue);
        cores = compiled.OrderBy(core => core.Id, StringComparer.Ordinal).ToArray();
        return true;
    }

    public static bool TryResolve(
        IPrototypeManager prototypeManager,
        KsProcgenAssemblyReference reference,
        out IReadOnlyList<KsProcgenResolvedAssembly> variants,
        out KsProcgenIssue? issue)
    {
        variants = [];
        issue = null;
        if (reference == null || !ValidName(reference.Id) ||
            string.IsNullOrWhiteSpace(reference.Assembly) || reference.Assembly.Length > MaximumPrototypeIdLength ||
            reference.Bindings == null || reference.Bindings.Count > MaximumBindings ||
            !float.IsFinite(reference.Weight) || reference.Weight < 0f ||
            reference.MinimumCount < 0 || reference.MinimumCount > 64 ||
            reference.MinimumCount > 0 && reference.Weight == 0f ||
            !prototypeManager.TryIndex<KsProcgenAssemblyPrototype>(reference.Assembly, out var prototype))
            return Fail("InvalidAssemblyReference", out issue);

        if (prototype.Variants == null || prototype.Variants.Count > MaximumVariants - 1)
            return Fail("AssemblyVariantBudget", out issue);
        var definitions = new List<KsProcgenAssemblyVariant>
        {
            new()
            {
                Id = "Base",
                AnchorMember = prototype.AnchorMember,
                Members = prototype.Members,
                Relations = prototype.Relations,
            },
        };
        definitions.AddRange(prototype.Variants);
        return TryCompileVariants(prototype.ID, definitions, reference.Bindings,
            id => prototypeManager.TryIndex<EntityPrototype>(id, out _), out variants, out issue);
    }

    /// <summary>Compile one ordered family atomically. Alternatives replace the complete base core.</summary>
    public static bool TryCompileVariants(string assemblyId, IReadOnlyList<KsProcgenAssemblyVariant> definitions,
        IReadOnlyDictionary<string, string> bindings, Func<string, bool> entityExists,
        out IReadOnlyList<KsProcgenResolvedAssembly> variants, out KsProcgenIssue? issue)
    {
        variants = [];
        issue = null;
        if (definitions == null || definitions.Count is < 1 or > MaximumVariants)
            return Fail("AssemblyVariantBudget", out issue);
        // Preflight every alternative before traversing its members or looking up any entity.
        if (string.IsNullOrWhiteSpace(assemblyId) || assemblyId.Length > MaximumPrototypeIdLength ||
            bindings == null || bindings.Count > MaximumBindings || entityExists == null ||
            bindings.Any(pair => !ValidName(pair.Key) || string.IsNullOrWhiteSpace(pair.Value) ||
                pair.Value.Length > MaximumPrototypeIdLength) ||
            definitions.Any(definition => definition == null || !ValidName(definition.Id) ||
                definition.Members == null || definition.Members.Count is < 1 or > 64 ||
                definition.Relations == null || definition.Relations.Count > 256))
            return Fail("InvalidAssemblyDefinition", out issue);
        if (definitions.Select(definition => definition.Id).Distinct(StringComparer.Ordinal).Count() !=
            definitions.Count)
            return Fail("DuplicateAssemblyVariant", out issue);
        if (definitions.SelectMany(definition => definition.Members)
            .Any(member => member?.Binding != null && !ValidName(member.Binding)))
            return Fail("InvalidAssemblyMember", out issue);
        var bindingNames = definitions.SelectMany(definition => definition.Members)
            .Where(member => member != null && member.Binding != null)
            .Select(member => member.Binding!).ToHashSet(StringComparer.Ordinal);
        if (bindings.Keys.Any(key => !bindingNames.Contains(key)))
            return Fail("UnknownAssemblyBinding", out issue);

        var compiled = new List<KsProcgenResolvedAssembly>();
        foreach (var definition in definitions)
        {
            if (!TryCompileVariant(assemblyId, definition, bindings, entityExists,
                    out var resolved, out issue))
                return false;
            compiled.Add(resolved!);
        }
        variants = compiled.ToArray();
        return true;
    }

    public static bool TryCompileVariant(
        string assemblyId,
        KsProcgenAssemblyVariant definition,
        IReadOnlyDictionary<string, string> bindings,
        Func<string, bool> entityExists,
        out KsProcgenResolvedAssembly? resolved,
        out KsProcgenIssue? issue)
    {
        resolved = null;
        issue = null;
        if (string.IsNullOrWhiteSpace(assemblyId) || assemblyId.Length > MaximumPrototypeIdLength ||
            definition == null || bindings == null || bindings.Count > MaximumBindings ||
            entityExists == null || !ValidName(definition.Id) || definition.Members == null ||
            definition.Relations == null || definition.Members.Count == 0 ||
            definition.Members.Count > 64 || definition.Relations.Count > 256 ||
            bindings.Any(pair => !ValidName(pair.Key) || string.IsNullOrWhiteSpace(pair.Value) ||
                                 pair.Value.Length > MaximumPrototypeIdLength ||
                                 !entityExists(pair.Value)))
            return Fail("InvalidAssemblyDefinition", out issue);

        var members = new List<KsProcgenResolvedAssemblyMember>();
        var bySource = new Dictionary<string, List<KsProcgenResolvedAssemblyMember>>(StringComparer.Ordinal);
        foreach (var member in definition.Members)
        {
            if (member == null || !ValidName(member.Id) || bySource.ContainsKey(member.Id) ||
                member.Footprint == null || member.AllowedQuarterTurns == null ||
                member.Binding != null && !ValidName(member.Binding) ||
                string.IsNullOrWhiteSpace(member.Entity) == string.IsNullOrWhiteSpace(member.Binding) ||
                member.MinimumCount < 0 || member.MaximumCount < 1 ||
                member.MinimumCount > member.MaximumCount || member.MaximumCount > 64 ||
                members.Count + member.MaximumCount > 64 || !Enum.IsDefined(member.RotationMode) ||
                member.LocalQuarterTurns is < 0 or > 3 ||
                !member.AllowedQuarterTurns.Contains(member.LocalQuarterTurns))
                return Fail("InvalidAssemblyMember", out issue);
            var entityId = member.Entity;
            if (entityId == null && (member.Binding == null || !bindings.TryGetValue(member.Binding, out entityId)))
                return Fail("MissingAssemblyBinding", out issue);
            var instances = new List<KsProcgenResolvedAssemblyMember>();
            for (var index = 0; index < member.MaximumCount; index++)
            {
                var instanceId = member.MaximumCount == 1 ? member.Id :
                    $"{member.Id}/{index.ToString(CultureInfo.InvariantCulture)}";
                var entry = new KsProcgenEntityEntry
                {
                    Id = instanceId,
                    Entity = entityId!,
                    Role = member.Role,
                    Movement = member.Movement,
                    Footprint = member.Footprint.OrderBy(cell => cell.Y).ThenBy(cell => cell.X).ToList(),
                    AllowedQuarterTurns = member.AllowedQuarterTurns.OrderBy(turn => turn).ToList(),
                    RequiresInteractionApproach = member.RequiresInteractionApproach,
                };
                if (!ValidateEntry(entry, entityExists))
                    return Fail("InvalidAssemblyEntity", out issue);
                if (member.ApproachLanding is { } landing &&
                    (Math.Abs((long) landing.X) > 9 || Math.Abs((long) landing.Y) > 9 ||
                     member.Footprint.Contains(landing) && member.Movement != KsProcgenMovementClass.Clear ||
                     !member.Footprint.Contains(landing) && !member.Footprint.Any(cell =>
                         Math.Abs((long) cell.X - landing.X) + Math.Abs((long) cell.Y - landing.Y) == 1)))
                    return Fail("InvalidAssemblyApproachLanding", out issue);
                var instance = new KsProcgenResolvedAssemblyMember(instanceId, member.Id,
                    index < member.MinimumCount, entry, member.RotationMode, member.LocalQuarterTurns,
                    ApproachLanding: member.ApproachLanding);
                members.Add(instance);
                instances.Add(instance);
            }
            bySource.Add(member.Id, instances);
        }
        var byId = members.ToDictionary(member => member.Id, StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(definition.AnchorMember) ||
            !byId.TryGetValue(definition.AnchorMember, out var anchor) || !anchor.Required ||
            bySource[anchor.SourceMemberId].Count != 1)
            return Fail("InvalidAssemblyAnchor", out issue);

        var relationIds = new HashSet<string>(StringComparer.Ordinal);
        var relations = new List<KsProcgenResolvedAssemblyRelation>();
        var supportParents = new Dictionary<string, string>(StringComparer.Ordinal);
        var assignedSeats = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var relation in definition.Relations)
        {
            if (relation == null || !ValidName(relation.Id) || !relationIds.Add(relation.Id) ||
                string.IsNullOrWhiteSpace(relation.Subject) || relation.Subject.Length > MaximumNameLength + 3 ||
                relation.Target != null && relation.Target.Length > MaximumNameLength + 3 ||
                !Enum.IsDefined(relation.Kind) || !Enum.IsDefined(relation.Severity) ||
                !Enum.IsDefined(relation.ApproachPolicy) || relation.MinimumDistance < 0 ||
                relation.MaximumDistance < relation.MinimumDistance || relation.MaximumDistance > 64)
                return Fail("InvalidAssemblyRelation", out issue);
            if (relation.Slot != null && (relation.Kind != KsProcgenRelationKind.OnSurface ||
                    string.IsNullOrWhiteSpace(relation.Slot) || relation.Slot.Length > 256) ||
                relation.ContainerId != null && (relation.Kind != KsProcgenRelationKind.InContainer ||
                    string.IsNullOrWhiteSpace(relation.ContainerId) || relation.ContainerId.Length > 256) ||
                relation.ApproachPolicy != KsProcgenApproachPolicy.EmptyFloor &&
                    relation.Kind is not (KsProcgenRelationKind.FacingOpenSpace or KsProcgenRelationKind.UsesSeat) ||
                relation.Kind != KsProcgenRelationKind.Near &&
                    (relation.MinimumDistance != 1 || relation.MaximumDistance != 4 &&
                        !(relation.Kind == KsProcgenRelationKind.AdjacentTo && relation.MaximumDistance == 1)))
                return Fail("InapplicableAssemblyRelationField", out issue);
            var unary = relation.Kind is KsProcgenRelationKind.AtCorner or KsProcgenRelationKind.FacingOpenSpace;
            KsProcgenResolvedAssemblyMember? target = null;
            if (unary ? relation.Target != null :
                relation.Target == null || !byId.TryGetValue(relation.Target, out target))
                return Fail("InvalidAssemblyRelationTarget", out issue);
            var subjects = bySource.TryGetValue(relation.Subject, out var repeated) ? repeated :
                byId.TryGetValue(relation.Subject, out var subjectInstance) ? [subjectInstance] : null;
            if (subjects == null || relations.Count + subjects.Count > 256)
                return Fail("InvalidAssemblyRelationSubject", out issue);
            foreach (var subject in subjects)
            {
                if (subject.Id == target?.Id || subject.Required && target is { Required: false } &&
                    relation.Severity == KsProcgenRelationSeverity.Required)
                    return Fail("AssemblyMandatoryDependencyUnmet", out issue);
                if (relation.Kind is KsProcgenRelationKind.OnSurface or KsProcgenRelationKind.InContainer)
                {
                    if (relation.Severity != KsProcgenRelationSeverity.Required ||
                        relation.Kind == KsProcgenRelationKind.InContainer &&
                        string.IsNullOrWhiteSpace(relation.ContainerId) ||
                        !supportParents.TryAdd(subject.Id, target!.Id))
                        return Fail("AssemblySupportConflict", out issue);
                }
                if (relation.Kind == KsProcgenRelationKind.UsesSeat &&
                    (!subject.Entry.RequiresInteractionApproach || target!.Entry.Role != KsProcgenEntityRole.Seat ||
                     !assignedSeats.TryAdd(subject.Id, target.Id)))
                    return Fail("AssemblySeatConflict", out issue);
                relations.Add(new KsProcgenResolvedAssemblyRelation(
                    subjects.Count == 1 ? relation.Id : $"{relation.Id}/{subject.Id}", subject.Id,
                    relation.Kind, target?.Id, relation.Severity, relation.MinimumDistance,
                    relation.MaximumDistance, relation.Slot, relation.ContainerId, relation.ApproachPolicy));
            }
        }
        foreach (var start in supportParents.Keys)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var current = start;
            while (supportParents.TryGetValue(current, out var parent))
            {
                if (!seen.Add(current))
                    return Fail("AssemblySupportCycle", out issue);
                current = parent;
            }
        }
        resolved = new KsProcgenResolvedAssembly(assemblyId, definition.Id, definition.AnchorMember,
            members.OrderBy(member => member.Id, StringComparer.Ordinal).ToArray(),
            relations.OrderBy(relation => relation.Id, StringComparer.Ordinal).ToArray());
        return true;
    }

    public static KsProcgenResolvedAssembly CompileSingleton(KsProcgenEntityEntry entry) => new(
        $"Singleton/{entry.Id}", "Base", entry.Id,
        [new KsProcgenResolvedAssemblyMember(entry.Id, entry.Id, true, new KsProcgenEntityEntry
        {
            Id = entry.Id,
            Entity = entry.Entity,
            Weight = entry.Weight,
            MinimumCount = entry.MinimumCount,
            Role = entry.Role,
            Movement = entry.Movement,
            Footprint = entry.Footprint.OrderBy(cell => cell.Y).ThenBy(cell => cell.X).ToList(),
            AllowedQuarterTurns = entry.AllowedQuarterTurns.OrderBy(turn => turn).ToList(),
            RequiresInteractionApproach = entry.RequiresInteractionApproach,
        }, KsProcgenMemberRotation.Independent, 0)], []);

    public static bool ValidateEntry(KsProcgenEntityEntry entry, Func<string, bool> entityExists) =>
        entry != null && entry.Footprint != null && entry.AllowedQuarterTurns != null &&
        !string.IsNullOrWhiteSpace(entry.Entity) && entry.Entity.Length <= MaximumPrototypeIdLength && entityExists(entry.Entity) &&
        Enum.IsDefined(entry.Role) && Enum.IsDefined(entry.Movement) &&
        entry.Footprint.Count is > 0 and <= 16 && entry.Footprint.Contains(new Vector2i(0, 0)) &&
        entry.Footprint.All(cell => cell.X is >= -8 and <= 8 && cell.Y is >= -8 and <= 8) &&
        entry.Footprint.Distinct().Count() == entry.Footprint.Count &&
        entry.AllowedQuarterTurns.Count is > 0 and <= 4 &&
        entry.AllowedQuarterTurns.All(turn => turn is >= 0 and <= 3) &&
        entry.AllowedQuarterTurns.Distinct().Count() == entry.AllowedQuarterTurns.Count;

    private static bool ValidName(string name) => !string.IsNullOrWhiteSpace(name) && name.Length <= MaximumNameLength &&
        name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    private static bool Fail(string code, out KsProcgenIssue? issue)
    {
        issue = new KsProcgenIssue(code, "The relational assembly declaration is invalid or cannot be resolved.");
        return false;
    }
}
