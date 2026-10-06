using System.IO;
using System.Linq;

namespace Content.Shared._KS14.Localization;

public sealed class KsPrototypeLocalizationData(
    string id, string[] parents, string? name, string? description, string? localizationId, bool isAbstract)
{
    public string Id { get; } = id;
    public string[] Parents { get; } = parents;
    public string? Name { get; set; } = name;
    public string? Description { get; set; } = description;
    public string? LocalizationId { get; set; } = localizationId;
    public bool Abstract { get; } = isAbstract;
    public bool Resolved { get; set; }
}

public readonly record struct KsPrototypeTranslation(string Name, string Description);

/// <summary>
/// One inheritance and fallback policy for packaged and runtime prototype metadata.
/// </summary>
public static class KsPrototypeLocalizationResolver
{
    public static void ResolveInheritedFields(IReadOnlyDictionary<string, KsPrototypeLocalizationData> prototypes)
    {
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        foreach (var prototype in prototypes.Values)
            ResolveInheritedFields(prototype, prototypes, visiting);
    }

    private static void ResolveInheritedFields(KsPrototypeLocalizationData prototype,
        IReadOnlyDictionary<string, KsPrototypeLocalizationData> prototypes, HashSet<string> visiting)
    {
        if (prototype.Resolved)
            return;
        if (!visiting.Add(prototype.Id))
            throw new InvalidDataException($"Cyclic entity inheritance at {prototype.Id}.");
        foreach (var parentId in prototype.Parents)
        {
            var parent = prototypes[parentId];
            ResolveInheritedFields(parent, prototypes, visiting);
            prototype.Name ??= parent.Name;
            prototype.Description ??= parent.Description;
            prototype.LocalizationId ??= parent.LocalizationId;
        }
        visiting.Remove(prototype.Id);
        prototype.Resolved = true;
    }

    public static KsPrototypeTranslation Resolve(KsPrototypeLocalizationData prototype,
        IReadOnlyDictionary<string, KsPrototypeLocalizationData> prototypes, bool defaultCulture,
        Func<string, string?> nativePattern, Func<string, string?> englishPattern)
    {
        var name = defaultCulture
            ? englishPattern(Key(prototype, false)) ?? prototype.Name ?? ""
            : ResolveField(prototype, prototypes, false, nativePattern, englishPattern);
        var description = ResolveField(prototype, prototypes, true, nativePattern, englishPattern);
        return new KsPrototypeTranslation(name, description);
    }

    private static string ResolveField(KsPrototypeLocalizationData prototype,
        IReadOnlyDictionary<string, KsPrototypeLocalizationData> prototypes, bool description,
        Func<string, string?> nativePattern, Func<string, string?> englishPattern)
    {
        var queue = new Queue<KsPrototypeLocalizationData>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        queue.Enqueue(prototype);
        while (queue.TryDequeue(out var ancestor))
        {
            if (!visited.Add(ancestor.Id))
                continue;
            if (nativePattern(Key(ancestor, description)) is { } value)
                return value; // An explicitly empty description is a valid override.
            var literal = description ? ancestor.Description : ancestor.Name;
            if (!ancestor.Abstract && literal != null && !ancestor.Parents.Any(parentId =>
                    literal == (description ? prototypes[parentId].Description : prototypes[parentId].Name)))
                return literal;
            foreach (var parentId in ancestor.Parents)
                queue.Enqueue(prototypes[parentId]);
        }
        return englishPattern(Key(prototype, description))
            ?? (description ? prototype.Description : prototype.Name) ?? "";
    }

    public static string Key(KsPrototypeLocalizationData prototype, bool description)
    {
        return (prototype.LocalizationId ?? $"ent-{prototype.Id}") + (description ? ".desc" : "");
    }
}
