using System.Linq;
using System.Text.RegularExpressions;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Markdown.Value;

namespace Content.Shared.Localizations;

public sealed partial class ContentLocalizationManager
{
    [Dependency] private IPrototypeManager _prototypeLocalePrototypeManager = default!;

    private readonly Dictionary<string, HashSet<string>> _prototypeNameMessages = new();
    private static readonly Regex PrototypeNamePattern = new(
        @"(?m)^(?<id>[A-Za-z][A-Za-z0-9_-]*)[ \t]*=[ \t]*(?<value>[^\r\n]*)(?:\r?\n(?<continuation>[ \t]+[^ \t\r\n][^\r\n]*))?");

    /// <summary>
    /// Localizes a prototype's default display name while preserving custom names and identities.
    /// </summary>
    public string GetLocalizedEntityName(EntityUid entityUid, IEntityManager entityManager, string displayName)
    {
        if (entityManager.TryGetComponent<MetaDataComponent>(entityUid, out var metadataComponent)
            && metadataComponent.EntityPrototype is { } prototype && displayName == prototype.Name)
            return GetLocalizedPrototypeName(prototype);

        return displayName;
    }

    /// <summary>
    /// Resolves a nested prototype name in the active culture without the engine's culture-independent entity cache.
    /// </summary>
    public string GetLocalizedPrototypeName(EntityPrototype prototype)
    {
        var culture = _loc.DefaultCulture?.Name ?? DefaultCultureName;
        if (culture == DefaultCultureName)
            return prototype.Name;

        if (!_prototypeNameMessages.TryGetValue(culture, out var namedMessages))
        {
            namedMessages = new HashSet<string>();
            var files = _localizationResourceManager.ContentFindFiles($"/Locale/{culture}")
                .Concat(_localizationResourceManager.ContentFindFiles("/Uploaded")
                    .Where(path => path.ToString().Contains($"/Locale/{culture}/", StringComparison.Ordinal)))
                .Where(path => path.Filename.EndsWith(".ftl", StringComparison.OrdinalIgnoreCase))
                .ToArray(); // Release content-root enumeration locks before opening files.
            foreach (var path in files)
            {
                using var reader = _localizationResourceManager.ContentFileReadText(path);
                // Index only whether a name value exists. The engine still parses and
                // formats its Fluent pattern; attribute-only entries inherit their name.
                foreach (Match match in PrototypeNamePattern.Matches(reader.ReadToEnd()))
                {
                    var value = match.Groups["value"].Value;
                    var continuation = match.Groups["continuation"].Value.TrimStart();
                    if (value.Length > 0 || continuation.Length > 0 && !continuation.StartsWith('.'))
                        namedMessages.Add(match.Groups["id"].Value);
                }
            }
            _prototypeNameMessages[culture] = namedMessages;
        }

        foreach (var (parentId, parentPrototype) in _prototypeLocalePrototypeManager.EnumerateAllParents<EntityPrototype>(prototype.ID, includeSelf: true))
        {
            // Abstract parents have mappings but no EntityPrototype instance.
            var localizationId = parentPrototype?.CustomLocalizationID ?? $"ent-{parentId}";
            if (parentPrototype == null
                && _prototypeLocalePrototypeManager.TryGetMapping<EntityPrototype>(parentId, out var mapping)
                && mapping.TryGet("localizationId", out ValueDataNode? localizationNode))
                localizationId = localizationNode.Value;
            if (namedMessages.Contains(localizationId))
                return _loc.GetString(localizationId);
            if (parentPrototype?.SetName != null && !HasInheritedPrototypeName(parentPrototype))
                return parentPrototype.SetName;
        }

        return prototype.Name;
    }

    private bool HasInheritedPrototypeName(EntityPrototype prototype)
    {
        if (prototype.Parents == null)
            return false;

        // Resolved prototypes can carry an English name copied from an abstract
        // parent. That inherited literal must not mask the parent's translation.
        foreach (var parentId in prototype.Parents)
        {
            if (_prototypeLocalePrototypeManager.TryGetMapping<EntityPrototype>(parentId, out var mapping)
                && mapping.TryGet("name", out ValueDataNode? nameNode) && nameNode.Value == prototype.SetName)
                return true;
        }

        return false;
    }
}
