using System.Globalization;
using System.Text;
using Content.Shared._KS14.Entry;
using Content.Shared._KS14.Localization;
using Content.Shared.Localizations;
using Linguini.Bundle;
using Linguini.Bundle.Builder;
using Linguini.Syntax.Parser;
using Robust.Packaging.AssetProcessing;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;

namespace Content.Packaging._KS14.Localization;

public static class KsPrototypeNameCacheCompiler
{
    public static bool IsSource(string path)
    {
        return path.StartsWith("Locale/", StringComparison.Ordinal) && path.EndsWith(".ftl", StringComparison.OrdinalIgnoreCase)
            || (path.StartsWith("Prototypes/", StringComparison.Ordinal)
                || path.StartsWith("_KsModule_ReplacedPrototypes/", StringComparison.Ordinal)
                || path.StartsWith("IgnoredPrototypes/", StringComparison.Ordinal))
            && path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase);
    }

    public static byte[] Compile(IEnumerable<AssetFile> sources)
    {
        var files = sources.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
        var prototypes = ReadPrototypes(files);
        KsPrototypeLocalizationResolver.ResolveInheritedFields(prototypes);

        var bundles = ReadCultures(files);
        if (!bundles.TryGetValue(ContentLocalizationManager.DefaultCultureName, out var english))
            throw new InvalidDataException("The name-cache compiler requires English localization resources.");

        var cultures = files.Where(file => file.Path.EndsWith("/_KS14/Localization/options.ftl", StringComparison.Ordinal))
            .Select(file => CultureInfo.GetCultureInfo(file.Path.Split('/')[1]).Name)
            .Append(ContentLocalizationManager.DefaultCultureName).Distinct(StringComparer.Ordinal);
        var tables = new Dictionary<string, Dictionary<string, KsPrototypeTranslation>>(StringComparer.Ordinal);
        foreach (var culture in cultures)
        {
            var native = bundles[culture];
            var names = new Dictionary<string, KsPrototypeTranslation>(StringComparer.Ordinal);
            foreach (var prototype in prototypes.Values.Where(prototype => !prototype.Abstract))
            {
                var translation = KsPrototypeLocalizationResolver.Resolve(prototype, prototypes,
                    culture == ContentLocalizationManager.DefaultCultureName,
                    key => Format(native, key), key => Format(english, key));
                names.Add(prototype.Id, translation);
            }
            tables.Add(culture, names);
        }
        return Write(tables);
    }

    private static byte[] Write(Dictionary<string, Dictionary<string, KsPrototypeTranslation>> cultures)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(KsPrototypeNameCache.Magic);
        writer.Write(KsPrototypeNameCache.Version);
        writer.Write(cultures.Count);
        foreach (var (culture, names) in cultures.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            writer.Write(culture);
            writer.Write(names.Count);
            foreach (var (prototypeId, name) in names.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                writer.Write(prototypeId);
                writer.Write(name.Name);
                writer.Write(name.Description);
            }
        }
        writer.Flush();
        return stream.ToArray();
    }

    private static Dictionary<string, KsPrototypeLocalizationData> ReadPrototypes(AssetFile[] files)
    {
        var prototypes = new Dictionary<string, KsPrototypeLocalizationData>(StringComparer.Ordinal);
        var ignoredPaths = new List<string>();
        foreach (var file in files.Where(file => file.Path.StartsWith("IgnoredPrototypes/", StringComparison.Ordinal)))
        {
            using var stream = file.Open();
            using var reader = new StreamReader(stream);
            foreach (var document in DataNodeParser.ParseYamlStream(reader))
            {
                if (document.Root is SequenceDataNode sequence)
                    ignoredPaths.AddRange(sequence.Sequence.OfType<ValueDataNode>().Select(node => node.Value.TrimStart('/')));
            }
        }

        // Partials are merged in after everything else, as the prototype manager does.
        var partials = new List<(MappingDataNode Mapping, bool PartialOnly, string Path)>();
        var partialPaths = KsEntryPoint.PartialPrototypePaths.Select(path => path.ToString().TrimStart('/')).ToArray();

        // Replacement modules overwrite existing prototypes after normal prototype loading.
        foreach (var file in files.Where(file => file.Path.StartsWith("Prototypes/", StringComparison.Ordinal)
                     || file.Path.StartsWith("_KsModule_ReplacedPrototypes/", StringComparison.Ordinal))
                     .OrderBy(file => file.Path.StartsWith("_KsModule_ReplacedPrototypes/", StringComparison.Ordinal)))
        {
            using var stream = file.Open();
            using var reader = new StreamReader(stream);
            foreach (var document in DataNodeParser.ParseYamlStream(reader))
            {
                if (document.Root is not SequenceDataNode sequence)
                    continue;
                foreach (var mapping in sequence.Sequence.OfType<MappingDataNode>())
                {
                    if (!mapping.TryGet("type", out ValueDataNode? typeValueNode) ||
                        typeValueNode.IsNull)
                        continue;

                    if (typeValueNode.Value != "entity")
                        continue;

                    var partialOnly = typeValueNode.Tag == PartialOnlyTag;
                    if (partialOnly || IsInPartialPath(file.Path, partialPaths))
                    {
                        partials.Add((mapping, partialOnly, file.Path));
                        continue;
                    }

                    var id = Scalar(mapping, "id") ?? throw new InvalidDataException($"Entity without id in {file.Path}.");
                    var isReplacement = file.Path.StartsWith("_KsModule_ReplacedPrototypes/", StringComparison.Ordinal);
                    if (isReplacement && (!prototypes.TryGetValue(id, out var original) || original.Abstract))
                        throw new InvalidDataException($"Replacement entity {id} does not exist in the package inputs.");
                    var prototype = new KsPrototypeLocalizationData(id, Parents(mapping) ?? [], Scalar(mapping, "name"), Scalar(mapping, "description"),
                        Scalar(mapping, "localizationId"),
                        string.Equals(Scalar(mapping, "abstract"), "true", StringComparison.OrdinalIgnoreCase)
                        || ignoredPaths.Any(path => file.Path == path
                            || file.Path.StartsWith(path.TrimEnd('/') + "/", StringComparison.Ordinal)));
                    if (isReplacement)
                        prototypes[id] = prototype;
                    else if (!prototypes.TryAdd(id, prototype))
                        throw new InvalidDataException($"Duplicate entity {id} in {file.Path}.");
                }
            }
        }

        foreach (var (mapping, partialOnly, path) in partials)
            MergePartial(prototypes, mapping, partialOnly, path);

        return prototypes;
    }

    private const string PartialOnlyTag = "!PartialOnly";

    private static bool IsInPartialPath(string path, string[] partialPaths)
    {
        // A registered path with an extension is one file; one without is a whole directory.
        return partialPaths.Any(partialPath => path == partialPath
            || (Path.GetExtension(partialPath).Length == 0 && path.StartsWith(partialPath.TrimEnd('/') + "/", StringComparison.Ordinal)));
    }

    /// <summary>
    ///     Applies a partial entity to the prototype it modifies: the fields it states replace the original's, and the
    ///         rest are kept. Without an original, a <c>!PartialOnly</c> one does nothing and any other is a new prototype,
    ///         as the prototype manager has it.
    /// </summary>
    private static void MergePartial(Dictionary<string, KsPrototypeLocalizationData> prototypes, MappingDataNode mapping, bool partialOnly, string path)
    {
        var id = Scalar(mapping, "id") ?? throw new InvalidDataException($"Entity without id in {path}.");
        if (!prototypes.TryGetValue(id, out var original))
        {
            if (!partialOnly)
            {
                prototypes.Add(id, new KsPrototypeLocalizationData(id, Parents(mapping) ?? [], Scalar(mapping, "name"),
                    Scalar(mapping, "description"), Scalar(mapping, "localizationId"),
                    string.Equals(Scalar(mapping, "abstract"), "true", StringComparison.OrdinalIgnoreCase)));
            }

            return;
        }

        if (Scalar(mapping, "name") is { } name)
            original.Name = name;

        if (Scalar(mapping, "description") is { } description)
            original.Description = description;

        if (Scalar(mapping, "localizationId") is { } localizationId)
            original.LocalizationId = localizationId;

        // As the prototype manager merges them: a list is added to, a single value replaces
        if (mapping.TryGet("parent", out var parentNode) && !parentNode.IsNull)
        {
            original.Parents = parentNode is SequenceDataNode
                ? original.Parents.Concat(Parents(mapping)!).Distinct().ToArray()
                : Parents(mapping)!;
        }
    }

    private static string[]? Parents(MappingDataNode mapping)
    {
        if (!mapping.TryGet("parent", out var parentNode) || parentNode.IsNull)
            return null;

        return parentNode is ValueDataNode parent ? [parent.Value]
            : ((SequenceDataNode)parentNode).Sequence.Cast<ValueDataNode>().Select(node => node.Value).ToArray();
    }

    private static string? Scalar(MappingDataNode mapping, string key)
    {
        return mapping.TryGet(key, out ValueDataNode? value) && !value.IsNull ? value.Value : null;
    }

    private static Dictionary<string, CulturePatterns> ReadCultures(AssetFile[] files)
    {
        var bundles = new Dictionary<string, CulturePatterns>(StringComparer.Ordinal);
        foreach (var group in files.Where(file => file.Path.StartsWith("Locale/", StringComparison.Ordinal))
                     .GroupBy(file => file.Path.Split('/')[1], StringComparer.Ordinal))
        {
            var bundle = LinguiniBuilder.Builder().CultureInfo(CultureInfo.GetCultureInfo(group.Key))
                .SkipResources().SetUseIsolating(false).UncheckedBuild();
            var index = new KsFluentMessageIndex();
            foreach (var file in group)
            {
                using var stream = file.Open();
                using var reader = new StreamReader(stream);
                var source = reader.ReadToEnd();
                index.AddResource(source);
                bundle.AddResourceOverriding(new LinguiniParser(source).Parse());
            }
            bundles.Add(group.Key, new CulturePatterns(bundle, index));
        }
        return bundles;
    }

    private static string? Format(CulturePatterns culture, string key)
    {
        if (!culture.Index.HasPattern(key))
            return null;
        var dot = key.IndexOf('.');
        var id = dot < 0 ? key : key[..dot];
        var attribute = dot < 0 ? null : key[(dot + 1)..];
        if (!culture.Bundle.TryGetMessage(id, attribute, null, out var errors, out var value))
            throw new InvalidDataException($"Cannot compile prototype metadata {key}: {string.Join(", ", errors)}");
        return value;
    }

    private sealed record CulturePatterns(FluentBundle Bundle, KsFluentMessageIndex Index);
}
