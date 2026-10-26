using System.Globalization;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;

namespace Content.Server._KS14.Procedural;

public sealed partial class KsProcgenPrototypeManifestSystem
{
    /// <summary>Inverse of the canonical writer, with no YAML parser, hooks or world allocation.</summary>
    internal static bool TryDecodeCanonicalBody(string text, KsProcgenPrototypeManifestLimits limits,
        out DataNode? node, out string? issueCode)
        => TryDecodeCanonicalBody(text, limits, out node, out issueCode, out _);

    internal static bool TryDecodeCanonicalBody(string text, KsProcgenPrototypeManifestLimits limits,
        out DataNode? node, out string? issueCode, out int visitedNodes)
    {
        node = null; issueCode = null; visitedNodes = 0;
        if (text == null || limits == null || !ValidLimits(limits) || text.Length > limits.MaximumCharacters)
        { issueCode = "InvalidCanonicalPrototypeInput"; return false; }
        var decoder = new CanonicalDecoder(text, limits);
        try
        {
            var decoded = decoder.ReadNode(0);
            if (!decoder.AtEnd) throw new FormatException();
            // Reject alternate spellings, unsorted mappings and trailing data. The exact encoding
            // is the retained identity, rather than any equivalent permissive input language.
            if (!TryCanonicalBody(decoded, limits, out var canonical, out issueCode, out _, out _) || canonical != text)
            { issueCode ??= "NonCanonicalPrototypeBody"; return false; }
            node = decoded; return true;
        }
        catch (FormatException) { issueCode = "InvalidCanonicalPrototypeBody"; return false; }
        finally { visitedNodes = decoder.Nodes; }
    }

    private sealed class CanonicalDecoder(string text, KsProcgenPrototypeManifestLimits limits)
    {
        private int _offset;
        private int _nodes;
        public int Nodes => _nodes;
        public bool AtEnd => _offset == text.Length;

        private char Next()
        {
            if (_offset >= text.Length) throw new FormatException();
            return text[_offset++];
        }

        private int Number()
        {
            var start = _offset;
            while (_offset < text.Length && text[_offset] is >= '0' and <= '9') _offset++;
            if (start == _offset || _offset >= text.Length || text[_offset++] != ':' ||
                !int.TryParse(text.AsSpan(start, _offset - start - 1), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var count)) throw new FormatException();
            return count;
        }

        private string? ReadText()
        {
            if (_offset < text.Length && text[_offset] == 'N')
            {
                _offset++; if (Next() != ';') throw new FormatException();
                return null;
            }
            var count = Number();
            if (count > text.Length - _offset) throw new FormatException();
            var value = text.Substring(_offset, count); _offset += count; return value;
        }

        public DataNode ReadNode(int depth)
        {
            if (depth > limits.MaximumDepth || _nodes >= limits.MaximumNodes) throw new FormatException();
            _nodes++;
            var tag = ReadText();
            var isNull = Next() switch { '0' => false, '1' => true, _ => throw new FormatException() };
            switch (Next())
            {
                case 'V':
                    return new ValueDataNode(ReadText() ?? throw new FormatException()) { Tag = tag, IsNull = isNull };
                case 'S':
                {
                    var count = Number();
                    if (count > limits.MaximumNodes - _nodes) throw new FormatException();
                    var sequence = new SequenceDataNode { Tag = tag, IsNull = isNull };
                    for (var index = 0; index < count; index++) sequence.Add(ReadNode(depth + 1));
                    return sequence;
                }
                case 'M':
                {
                    var count = Number();
                    if (count > limits.MaximumNodes - _nodes) throw new FormatException();
                    var mapping = new MappingDataNode { Tag = tag, IsNull = isNull };
                    string? previous = null;
                    for (var index = 0; index < count; index++)
                    {
                        var keyTag = ReadText(); var key = ReadText() ?? throw new FormatException();
                        if (previous != null && StringComparer.Ordinal.Compare(previous, key) >= 0) throw new FormatException();
                        mapping.Add(key, ReadNode(depth + 1));
                        if (keyTag != null) mapping.SetKeyTag(key, keyTag);
                        previous = key;
                    }
                    return mapping;
                }
                default: throw new FormatException();
            }
        }
    }
}
