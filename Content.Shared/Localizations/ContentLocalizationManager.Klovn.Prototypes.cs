using System.Collections.Frozen;
using System.Linq;
using Content.Shared._KS14.Localization;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;

namespace Content.Shared.Localizations;

public sealed partial class ContentLocalizationManager
{
    [Dependency] private IPrototypeManager _prototypeLocalePrototypeManager = default!;

    private readonly Dictionary<string, KsPrototypeLocalizationTable> _localizedPrototypeTables = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, KsPrototypeTranslation>> _runtimePrototypeTranslations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, KsFluentMessageIndex> _prototypeMessageIndexes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, string>> _runtimePatternValues = new(StringComparer.Ordinal);
    private Dictionary<string, KsPrototypeLocalizationData>? _runtimePrototypeGraph;
    private Dictionary<string, string>? _runtimeEnglishPatterns;
    private bool _packagedPrototypeNameCacheLoaded;
    private string[]? _packagedCultures;

    public string GetLocalizedEntityName(EntityUid entityUid, IEntityManager entityManager, string displayName)
    {
        if (entityManager.TryGetComponent<MetaDataComponent>(entityUid, out var metadataComponent)
            && metadataComponent.EntityPrototype is { } prototype && displayName == prototype.Name)
            return GetLocalizedPrototypeName(prototype);
        return displayName;
    }

    public string GetLocalizedPrototypeName(EntityPrototype prototype)
    {
        var culture = _loc.DefaultCulture?.Name ?? DefaultCultureName;
        if (culture == DefaultCultureName)
            return prototype.Name;
        var table = GetPrototypeTable(culture);
        return table.Names.TryGetValue(prototype.ID, out var name)
            ? name : GetRuntimeTranslation(prototype.ID, culture).Name;
    }

    public string GetLocalizedPrototypeDescription(EntityPrototype prototype)
    {
        var culture = _loc.DefaultCulture?.Name ?? DefaultCultureName;
        var table = GetPrototypeTable(culture);
        return table.Descriptions.TryGetValue(prototype.ID, out var description)
            ? description : GetRuntimeTranslation(prototype.ID, culture).Description;
    }

    private KsPrototypeLocalizationTable GetPrototypeTable(string culture)
    {
        if (!_localizedPrototypeTables.TryGetValue(culture, out var table))
            table = BuildLocalizedPrototypeTable(culture);
        return table;
    }

    private KsPrototypeTranslation GetRuntimeTranslation(string id, string culture)
    {
        if (!_runtimePrototypeTranslations.TryGetValue(culture, out var translations))
        {
            translations = new Dictionary<string, KsPrototypeTranslation>(StringComparer.Ordinal);
            _runtimePrototypeTranslations.Add(culture, translations);
        }
        if (!translations.TryGetValue(id, out var translation))
        {
            var graph = GetRuntimePrototypeGraph();
            var english = GetRuntimeEnglishPatterns();
            translation = KsPrototypeLocalizationResolver.Resolve(graph[id], graph, culture == DefaultCultureName,
                key => GetNativePattern(culture, key), key => english.GetValueOrDefault(key));
            translations.Add(id, translation);
        }
        return translation;
    }

    /// <summary>
    /// Call after changing Fluent resources or reloading engine localizations directly.
    /// Prototype reloads and content language refreshes invalidate the tables automatically.
    /// </summary>
    public void InvalidatePrototypeNameCache()
    {
        _localizedPrototypeTables.Clear();
        _runtimePrototypeTranslations.Clear();
        _prototypeMessageIndexes.Clear();
        _runtimePatternValues.Clear();
        _runtimePrototypeGraph = null;
        _runtimeEnglishPatterns = null;
        _packagedPrototypeNameCacheLoaded = false;
    }

    public void InitializePrototypeNameCaches()
    {
        if (!_packagedPrototypeNameCacheLoaded)
            TryLoadPackagedPrototypeNameCache();
    }

    public void OnClientCultureChanged()
    {
        if (!_packagedPrototypeNameCacheLoaded)
            InvalidatePrototypeNameCache();
    }

    private IEnumerable<string> GetPackagedCultureNames()
    {
        if (_packagedCultures == null)
            TryLoadPackagedPrototypeNameCache();
        return _packagedCultures ?? Array.Empty<string>();
    }

    private void TryLoadPackagedPrototypeNameCache()
    {
        if (!_localizationResourceManager.TryContentFileRead("/" + KsPrototypeNameCache.ResourcePath, out var stream))
        {
            _packagedCultures = [];
            return;
        }
        using (stream)
        {
            var cultures = KsPrototypeNameCache.Read(stream);
            _localizedPrototypeTables.Clear();
            foreach (var (culture, table) in cultures)
                _localizedPrototypeTables.Add(culture, table);
            _packagedCultures = cultures.Keys.ToArray();
            _packagedPrototypeNameCacheLoaded = true;
        }
    }

    private void InitializePrototypeNameCache()
    {
        _prototypeLocalePrototypeManager.PrototypesReloaded += OnLocalizedPrototypesReloaded;
    }

    private void OnLocalizedPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (!args.WasModified<EntityPrototype>())
            return;
        InvalidatePrototypeNameCache();
        _prototypeMetadataWarmed = false;
    }

    private KsPrototypeLocalizationTable BuildLocalizedPrototypeTable(string culture)
    {
        var graph = GetRuntimePrototypeGraph();
        var english = GetRuntimeEnglishPatterns();
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var descriptions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var prototype in graph.Values)
        {
            if (prototype.Abstract)
                continue;
            var translation = KsPrototypeLocalizationResolver.Resolve(prototype, graph, culture == DefaultCultureName,
                key => GetNativePattern(culture, key), key => english.GetValueOrDefault(key));
            names.Add(prototype.Id, translation.Name);
            descriptions.Add(prototype.Id, translation.Description);
        }
        var table = new KsPrototypeLocalizationTable(names.ToFrozenDictionary(StringComparer.Ordinal),
            descriptions.ToFrozenDictionary(StringComparer.Ordinal));
        _localizedPrototypeTables.Add(culture, table);
        return table;
    }

    private Dictionary<string, KsPrototypeLocalizationData> GetRuntimePrototypeGraph()
    {
        if (_runtimePrototypeGraph != null)
            return _runtimePrototypeGraph;
        var graph = new Dictionary<string, KsPrototypeLocalizationData>(StringComparer.Ordinal);
        var queue = new Queue<string>(_prototypeLocalePrototypeManager.EnumeratePrototypes<EntityPrototype>()
            .Select(prototype => prototype.ID));
        while (queue.TryDequeue(out var id))
        {
            if (graph.ContainsKey(id))
                continue;
            if (!_prototypeLocalePrototypeManager.TryGetMapping<EntityPrototype>(id, out var mapping))
                continue;
            var parents = Array.Empty<string>();
            if (mapping.TryGet("parent", out var parentNode) && !parentNode.IsNull)
                parents = parentNode is ValueDataNode parent ? [parent.Value]
                    : ((SequenceDataNode)parentNode).Sequence.Cast<ValueDataNode>().Select(node => node.Value).ToArray();
            var isAbstract = !_prototypeLocalePrototypeManager.TryIndex<EntityPrototype>(id, out _);
            graph.Add(id, new KsPrototypeLocalizationData(id, parents, Scalar(mapping, "name"),
                Scalar(mapping, "description"), Scalar(mapping, "localizationId"), isAbstract));
            foreach (var parentId in parents)
                queue.Enqueue(parentId);
        }
        KsPrototypeLocalizationResolver.ResolveInheritedFields(graph);
        _runtimePrototypeGraph = graph;
        return graph;
    }

    private static string? Scalar(MappingDataNode mapping, string key)
    {
        return mapping.TryGet(key, out ValueDataNode? value) && !value.IsNull ? value.Value : null;
    }

    private Dictionary<string, string> GetRuntimeEnglishPatterns()
    {
        if (_runtimeEnglishPatterns != null)
            return _runtimeEnglishPatterns;
        var english = new Dictionary<string, string>(StringComparer.Ordinal);
        using (BeginCultureScope(DefaultCultureName))
        {
            foreach (var prototype in GetRuntimePrototypeGraph().Values)
            {
                foreach (var description in new[] { false, true })
                {
                    var key = KsPrototypeLocalizationResolver.Key(prototype, description);
                    if (GetNativePattern(DefaultCultureName, key) is { } value)
                        english[key] = value;
                }
            }
        }
        _runtimeEnglishPatterns = english;
        return english;
    }

    private string? GetNativePattern(string culture, string key)
    {
        if (!GetPrototypeMessageIndex(culture).HasPattern(key))
            return null;
        if (!_runtimePatternValues.TryGetValue(culture, out var patterns))
        {
            patterns = new Dictionary<string, string>(StringComparer.Ordinal);
            _runtimePatternValues.Add(culture, patterns);
        }
        if (!patterns.TryGetValue(key, out var value))
        {
            value = _loc.GetString(key);
            patterns.Add(key, value);
        }
        return value;
    }

    private KsFluentMessageIndex GetPrototypeMessageIndex(string culture)
    {
        if (_prototypeMessageIndexes.TryGetValue(culture, out var index))
            return index;
        index = new KsFluentMessageIndex();
        var files = _localizationResourceManager.ContentFindFiles($"/Locale/{culture}")
            .Concat(_localizationResourceManager.ContentFindFiles("/Uploaded")
                .Where(path => path.ToString().Contains($"/Locale/{culture}/", StringComparison.Ordinal)))
            .Where(path => path.Filename.EndsWith(".ftl", StringComparison.OrdinalIgnoreCase))
            .Distinct().OrderBy(path => path.ToString(), StringComparer.Ordinal).ToArray();
        // Release content-root enumeration locks before opening files.
        foreach (var path in files)
        {
            using var reader = _localizationResourceManager.ContentFileReadText(path);
            index.AddResource(reader.ReadToEnd());
        }
        _prototypeMessageIndexes.Add(culture, index);
        return index;
    }
}
