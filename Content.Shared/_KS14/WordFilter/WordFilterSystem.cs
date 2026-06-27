using System.Text;
using System.Text.RegularExpressions;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._KS14.WordFilter;

public sealed class WordFilterSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    /// <summary>
    ///     Characters that are totally removed.
    /// </summary>
    private static readonly char[] UnspacedPunctuation = ['“', '”', '‘', '‚', '"', '.', ',', '_'];

    /// <summary>
    ///     Characters that are replaced with spaces.
    /// </summary>
    private static readonly char[] SpacedPunctuation = ['-'];

    private static readonly Dictionary<char, char> HomoglyphMap = new()
    {
        // Cyrillic to Latin
        { 'А', 'A' }, { 'а', 'a' },
        { 'В', 'B' },
        { 'С', 'C' }, { 'с', 'c' },
        { 'Е', 'E' }, { 'е', 'e' },
        { 'Н', 'H' }, { 'н', 'h' },
        { 'І', 'I' }, { 'і', 'i' },
        { 'К', 'K' }, { 'к', 'k' },
        { 'М', 'M' }, { 'м', 'm' },
        { 'О', 'O' }, { 'о', 'o' },
        { 'Р', 'P' }, { 'р', 'p' },
        { 'Т', 'T' }, { 'т', 't' },
        { 'Х', 'X' }, { 'х', 'x' },
        { 'У', 'Y' }, { 'у', 'y' },
        // Greek to Latin
        { 'Α', 'A' }, { 'Β', 'B' },
        { 'Ε', 'E' }, { 'Ι', 'I' },
        { 'Ο', 'O' }, { 'Ρ', 'P' },
        { 'Τ', 'T' }, { 'Χ', 'X' }
    };

    public static string ParseToLatin(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var result = new StringBuilder(input.Length);

        foreach (var c in input)
        {
            // If the character is in our homoglyph map, use the Latin equivalent
            if (HomoglyphMap.TryGetValue(c, out var latinChar))
            {
                result.Append(latinChar);
            }
            else
            {
                // Otherwise, keep the original character
                result.Append(c);
            }
        }

        return result.ToString();
    }

    public static string SkeletoniseString(string message)
    {
        var newMessage = new StringBuilder(message.Length);

        for (var i = 0; i < message.Length; i++)
        {
            var currentChar = message[i];
            if (SpacedPunctuation.Contains(currentChar))
                currentChar = ' ';
            else if (UnspacedPunctuation.Contains(currentChar))
                continue;

            newMessage.Append(currentChar);
        }

        return newMessage.ToString();
    }

    public static string SkeletoniseAndConvertString(string message)
        => ParseToLatin(SkeletoniseString(message));

    private readonly Dictionary<WordFilterCategory, List<WordFilterCacheDatum>> _cache = [];

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        UpdateCache();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (!args.WasModified<WordFilterPrototype>())
            return;

        UpdateCache();
    }

    private void UpdateCache()
    {
        _cache.Clear();

        var protoCount = _prototypeManager.Count<WordFilterPrototype>();
        _cache.TrimExcess(protoCount);
        _cache.EnsureCapacity(protoCount);

        foreach (var filterPrototype in _prototypeManager.EnumeratePrototypes<WordFilterPrototype>())
        {
            string replacement;
            string capitalisedReplacement;
            if (filterPrototype.Replacement is { })
            {
                replacement = filterPrototype.Replacement;

                var builder = new StringBuilder(replacement);
                builder.Remove(0, 1);
                builder.Insert(0, char.ToUpperInvariant(replacement[0]));
                capitalisedReplacement = builder.ToString();
            }
            else
            {
                replacement = "";
                capitalisedReplacement = "";
            }

            var datum = new WordFilterCacheDatum(filterPrototype.Matcher, replacement, capitalisedReplacement);

            var list = _cache.GetOrNew(filterPrototype.Category);
            list.Add(datum);
        }
    }

    /// <returns>True if any matching wordfilter filtered the string.</returns>
    public bool AnyFilterMatches(string message, WordFilterCategory category)
    {
        if (!_cache.TryGetValue(category, out var cacheData))
            return false;

        foreach (var cacheDatum in cacheData)
        {
            if (!cacheDatum.Matcher.IsMatch(message))
                continue;

            return true;
        }

        return false;
    }

    public void FilterAndReplaceString(ref string message, WordFilterCategory category)
    {
        if (!_cache.TryGetValue(category, out var cacheData))
            return;

        foreach (var cacheDatum in cacheData)
        {
            var replacementLength = cacheDatum.Replacement.Length;
            if (replacementLength == 0)
            {
                message = cacheDatum.Matcher.Replace(message, "");
                continue;
            }

            var matchIndex = 0;
            while (cacheDatum.Matcher.Match(message, matchIndex) is { } match &&
                match.Success)
            {
                var matchString = match.Value;
                var consecutiveUppercase = 0;
                for (var ix = 0; ix < matchString.Length; ix++)
                {
                    var character = matchString[ix];
                    if (char.IsWhiteSpace(character))
                        continue;

                    if (!char.IsUpper(character))
                        break;

                    consecutiveUppercase++;
                }

                // obsessed string ops
                string replacement;
                if (consecutiveUppercase == matchString.Length)
                    replacement = cacheDatum.Replacement.ToUpperInvariant();
                else if (consecutiveUppercase == 1)
                    replacement = cacheDatum.ReplacementCapitalised;
                else
                    replacement = cacheDatum.Replacement;

                message = message.Remove(match.Index, match.Length).Insert(match.Index, replacement);
                matchIndex += match.Index + replacementLength;
            }
        }
    }

    // Why does ReplacementCapitalised exist?
    // Well... [ERRO] res.typecheck: Found reference to[System.Runtime]System.ReadOnlySpan`1 < char > ..ctor in method Content.Shared._KS14.WordFilter.WordFilterSystem.FilterAndReplaceString
    /// <param name="Replacement">May be empty.</param>
    /// <param name="ReplacementCapitalised">May be empty.</param>
    private sealed record class WordFilterCacheDatum(Regex Matcher, string Replacement, string ReplacementCapitalised);
}
