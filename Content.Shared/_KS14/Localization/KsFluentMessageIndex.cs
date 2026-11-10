using System.IO;

namespace Content.Shared._KS14.Localization;

/// <summary>
/// Indexes which values and attributes a resource supplies in its own culture.
/// Formatting remains the responsibility of Fluent. Both compilers use this index.
/// </summary>
public sealed class KsFluentMessageIndex
{
    private readonly Dictionary<string, HashSet<string>> _messages = new(StringComparer.Ordinal);

    public void AddResource(string source)
    {
        using var reader = new StringReader(source);
        HashSet<string>? patterns = null;
        var inAttribute = false;
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            if (!char.IsWhiteSpace(line[0]))
            {
                patterns = null;
                inAttribute = false;
                var equals = line.IndexOf('=');
                if (equals < 1)
                    continue;
                var id = line[..equals].TrimEnd();
                if (!IsIdentifier(id))
                    continue;
                patterns = new HashSet<string>(StringComparer.Ordinal);
                _messages[id] = patterns;
                if (!string.IsNullOrWhiteSpace(line[(equals + 1)..]))
                    patterns.Add("");
                continue;
            }
            if (patterns == null)
                continue;
            var content = line.TrimStart();
            var attributeEquals = content.IndexOf('=');
            if (content.StartsWith('.') && attributeEquals > 1)
            {
                var attribute = content[1..attributeEquals].TrimEnd();
                if (IsIdentifier(attribute))
                {
                    patterns.Add(attribute);
                    inAttribute = true;
                    continue;
                }
            }
            if (!inAttribute)
                patterns.Add("");
        }
    }

    public bool HasPattern(string key)
    {
        var dot = key.IndexOf('.');
        var id = dot < 0 ? key : key[..dot];
        var attribute = dot < 0 ? "" : key[(dot + 1)..];
        return _messages.TryGetValue(id, out var patterns) && patterns.Contains(attribute);
    }

    private static bool IsIdentifier(string value)
    {
        if (value.Length == 0 || !char.IsAsciiLetter(value[0]))
            return false;
        foreach (var character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '-' && character != '_')
                return false;
        }
        return true;
    }
}
