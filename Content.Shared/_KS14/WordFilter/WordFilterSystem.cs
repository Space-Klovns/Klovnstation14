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
    private static readonly char[] UnspacedPunctuation = ['\'', '"', '.', ',', '_'];

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
            var datum = new WordFilterCacheDatum(filterPrototype.Matcher, filterPrototype.Replacement ?? "");

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
            message = cacheDatum.Matcher.Replace(message, cacheDatum.Replacement);
    }

    /// <param name="Replacement">May be empty.</param>
    private sealed record class WordFilterCacheDatum(Regex Matcher, string Replacement);
}
