using System.Text.RegularExpressions;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     tgui's window title handling, from <c>TitleBar.tsx</c> and tgui-core's <c>toTitleCase</c>.
/// </summary>
public static class KsTguiText
{
    private static readonly Regex WordRegex = new(@"([^\W_]+[^\s-]*) *", RegexOptions.ECMAScript);

    private static readonly string[] StopWords =
    [
        "A", "An", "And", "As", "At", "But", "By", "For", "From", "In", "Into", "Near", "Nor", "Of", "On", "Onto", "Or",
        "The", "To", "With",
    ];

    private static readonly Regex[] StopWordRegexes = MakeStopWordRegexes();

    private static readonly Regex[] AcronymRegexes =
    [
        new(@"\bId\b"),
        new(@"\bTv\b"),
    ];

    /// <summary>
    ///     What a title shows as in the tgui style: an all-lowercase one is title cased, as an entity's name usually needs,
    ///         and any other is left as it was written. Latin only, since title casing is wrong in Russian.
    /// </summary>
    public static string FormatTitle(string title)
    {
        return title == title.ToLowerInvariant() && IsLatin(title) ? ToTitleCase(title) : title;
    }

    /// <summary>
    ///     A port of tgui-core's <c>toTitleCase</c>, down to its turning "Id" and "Tv" to lowercase.
    /// </summary>
    public static string ToTitleCase(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var result = WordRegex.Replace(text, match => Capitalize(match.Value));

        foreach (var regex in StopWordRegexes)
            result = regex.Replace(result, match => match.Value.ToLowerInvariant());

        foreach (var regex in AcronymRegexes)
            result = regex.Replace(result, match => match.Value.ToLowerInvariant());

        return result;
    }

    private static Regex[] MakeStopWordRegexes()
    {
        var regexes = new Regex[StopWords.Length];
        for (var i = 0; i < StopWords.Length; i++)
            regexes[i] = new Regex($@"\s{StopWords[i]}\s");

        return regexes;
    }

    private static string Capitalize(string word)
    {
        // Substrings because joining a char to a string goes through a span constructor the sandbox refuses.
        return word.Substring(0, 1).ToUpperInvariant() + word.Substring(1).ToLowerInvariant();
    }

    private static bool IsLatin(string text)
    {
        foreach (var character in text)
        {
            if (char.IsLetter(character) && character > 'z')
                return false;
        }

        return true;
    }
}
