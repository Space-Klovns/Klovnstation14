using System.Globalization;
using System.IO;
using System.Text;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using YamlDotNet.RepresentationModel;

namespace Content.Client._KS14.Actions;

/// <summary>
///     Small JSON codec for the action layout schema.
/// </summary>
/// <remarks>
///     Reading goes through YamlDotNet because JSON is close enough to a YAML subset for files this codec
///         wrote itself, and unlike <c>System.Text.Json</c> those APIs are permitted in sandboxed content
///         assemblies. It is not a general JSON parser, though - a hand-edited file that repeats a key,
///         for instance, is rejected outright where a JSON parser would take the last one.
/// </remarks>
public static class KsActionBarConfigurationJson
{
    /// <summary>
    ///     Writes a layout out as JSON.
    /// </summary>
    public static string Serialize(KsActionBarConfiguration configuration)
    {
        var builder = new StringBuilder();
        builder.AppendLine("{");
        builder.Append("  \"version\": ").Append(configuration.Version).AppendLine(",");
        builder.AppendLine("  \"entries\": [");

        for (var index = 0; index < configuration.Entries.Count; index++)
        {
            var entry = configuration.Entries[index];
            builder.Append("    { ");
            if (entry.Action != null)
            {
                builder.Append("\"action\": ");
                AppendIdentity(builder, entry.Action);
            }
            else
            {
                builder.Append("\"folder\": [");
                var folder = entry.Folder ?? [];
                for (var memberIndex = 0; memberIndex < folder.Count; memberIndex++)
                {
                    if (memberIndex > 0)
                        builder.Append(", ");

                    AppendIdentity(builder, folder[memberIndex]);
                }

                builder.Append(']');
            }

            builder.Append(" }");
            builder.AppendLine(index + 1 < configuration.Entries.Count ? "," : string.Empty);
        }

        builder.AppendLine("  ],");
        builder.AppendLine("  \"knownActions\": [");
        for (var index = 0; index < configuration.KnownActions.Count; index++)
        {
            builder.Append("    ");
            AppendIdentity(builder, configuration.KnownActions[index]);
            builder.AppendLine(index + 1 < configuration.KnownActions.Count ? "," : string.Empty);
        }

        builder.AppendLine("  ]");
        builder.AppendLine("}");
        return builder.ToString();
    }

    /// <summary>
    ///     Reads a layout back, returning false rather than throwing on anything malformed.
    /// </summary>
    /// <remarks>
    ///     The version is reported, not enforced - it is up to the caller to decide what to do with a
    ///         layout written by a different version.
    /// </remarks>
    public static bool TryDeserialize(string json, out KsActionBarConfiguration configuration)
    {
        configuration = new KsActionBarConfiguration();
        using var reader = new StringReader(json);
        var stream = new YamlStream();

        // The parser throws on malformed input and on a duplicate key. This is a Try method, and the
        // file it reads sits in user data where anything could have happened to it.
        try
        {
            stream.Load(reader);
        }
        catch (Exception)
        {
            return false;
        }

        if (stream.Documents.Count != 1 ||
            stream.Documents[0].RootNode.ToDataNode() is not MappingDataNode root ||
            !TryGetScalar(root, "version", out var versionText) ||
            !int.TryParse(versionText, out var version) ||
            !TryGetSequence(root, "entries", out var entries) ||
            !TryGetSequence(root, "knownActions", out var knownActions))
        {
            return false;
        }

        configuration.Version = version;
        foreach (var knownNode in knownActions.Sequence)
        {
            if (TryReadIdentity(knownNode, out var identity))
                configuration.KnownActions.Add(identity);
        }

        foreach (var entryNode in entries.Sequence)
        {
            if (entryNode is not MappingDataNode entry)
                continue;

            if (TryGetNode(entry, "action", out var actionNode) && TryReadIdentity(actionNode, out var action))
            {
                configuration.Entries.Add(new KsActionBarConfigurationEntry { Action = action });
                continue;
            }

            if (!TryGetSequence(entry, "folder", out var folderNodes))
                continue;

            var folder = new List<KsSavedActionIdentity>();
            foreach (var memberNode in folderNodes.Sequence)
            {
                if (TryReadIdentity(memberNode, out var member))
                    folder.Add(member);
            }

            configuration.Entries.Add(new KsActionBarConfigurationEntry { Folder = folder });
        }

        return true;
    }

    private static void AppendIdentity(StringBuilder builder, KsSavedActionIdentity identity)
    {
        builder.Append("{ \"actionPrototype\": ");
        AppendJsonString(builder, identity.ActionPrototype);
        if (KsActionBarIdentity.NormalizeProvider(identity.ProviderPrototype) is { } providerPrototype)
        {
            builder.Append(", \"providerPrototype\": ");
            AppendJsonString(builder, providerPrototype);
        }

        builder.Append(", \"occurrence\": ").Append(identity.Occurrence).Append(" }");
    }

    private static void AppendJsonString(StringBuilder builder, string value)
    {
        builder.Append('"');
        foreach (var character in value)
        {
            switch (character)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (character < ' ')
                    {
                        builder.Append("\\u");
                        builder.Append(((int) character).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        builder.Append('"');
    }

    private static bool TryReadIdentity(DataNode node, out KsSavedActionIdentity identity)
    {
        identity = new KsSavedActionIdentity();
        if (node is not MappingDataNode mapping ||
            !TryGetScalar(mapping, "actionPrototype", out var actionPrototype) ||
            !TryGetScalar(mapping, "occurrence", out var occurrenceText) ||
            !int.TryParse(occurrenceText, out var occurrence) ||
            occurrence < 0)
        {
            return false;
        }

        TryGetScalar(mapping, "providerPrototype", out var providerPrototype);
        identity.ActionPrototype = actionPrototype;
        identity.ProviderPrototype = KsActionBarIdentity.NormalizeProvider(providerPrototype);
        identity.Occurrence = occurrence;
        return true;
    }

    private static bool TryGetSequence(MappingDataNode mapping, string key, out SequenceDataNode sequence)
    {
        sequence = default!;
        if (!TryGetNode(mapping, key, out var node) || node is not SequenceDataNode result)
            return false;

        sequence = result;
        return true;
    }

    private static bool TryGetScalar(MappingDataNode mapping, string key, out string value)
    {
        value = string.Empty;
        if (!TryGetNode(mapping, key, out var node) || node is not ValueDataNode { Value: { } result })
            return false;

        value = result;
        return true;
    }

    private static bool TryGetNode(MappingDataNode mapping, string key, out DataNode node)
    {
        return mapping.TryGet(key, out node!);
    }
}
