using System.Text;

namespace Content.Shared._KS14.Language;

/// <summary>
///     Scrambles a message for non-understanders. Implementations return literal display text
///     (never loc keys) and must be deterministic per (message, language, round).
/// </summary>
[ImplicitDataDefinitionForInheritors]
public abstract partial class KsObfuscationMethod
{
    public string Obfuscate(string message, int roundSeed, int languageSeed)
    {
        var sb = new StringBuilder(message.Length * 2);
        ObfuscateInto(sb, message, roundSeed, languageSeed);
        return sb.ToString();
    }

    protected abstract void ObfuscateInto(StringBuilder sb, string message, int roundSeed, int languageSeed);
}

/// <summary>
///     Scrambles word-by-word: word-character runs go to the subclass, whitespace and
///     punctuation survive verbatim.
/// </summary>
public abstract partial class KsWordObfuscationMethod : KsObfuscationMethod
{
    protected sealed override void ObfuscateInto(StringBuilder sb, string message, int roundSeed, int languageSeed)
    {
        // Fail closed: a misauthored bank must never pass the clear message through.
        if (!HasUsableBank)
        {
            sb.Append(Loc.GetString("ks-language-obfuscation-incomprehensible"));
            return;
        }

        var wordStart = -1;
        for (var i = 0; i <= message.Length; i++)
        {
            var isWordChar = i < message.Length && IsWordChar(message[i]);
            if (isWordChar)
            {
                if (wordStart < 0)
                    wordStart = i;
                continue;
            }

            if (wordStart >= 0)
            {
                EmitWord(sb, message.AsSpan(wordStart, i - wordStart), roundSeed, languageSeed);
                wordStart = -1;
            }

            if (i < message.Length)
                sb.Append(message[i]);
        }
    }

    /// <summary>
    ///     Not letter-category based: that would pass lookalike alphabets (circled or
    ///     mathematical letters, surrogate pairs) through verbatim and leak the clear message.
    /// </summary>
    private static bool IsWordChar(char c)
        => !char.IsWhiteSpace(c) && !char.IsPunctuation(c) && !char.IsControl(c);

    protected abstract bool HasUsableBank { get; }

    protected abstract void EmitWord(StringBuilder sb, ReadOnlySpan<char> word, int roundSeed, int languageSeed);
}

/// <summary>
///     Replaces each word with a run of syllables from the bank; leading capitals survive.
/// </summary>
public sealed partial class KsSyllableObfuscation : KsWordObfuscationMethod
{
    [DataField(required: true)]
    public List<string> Syllables = new();

    [DataField]
    public int MinSyllables = 1;

    [DataField]
    public int MaxSyllables = 3;

    protected override bool HasUsableBank
    {
        get
        {
            if (Syllables.Count == 0)
                return false;

            // An empty syllable would let a word silently emit nothing - treat it as misauthored.
            foreach (var syllable in Syllables)
            {
                if (syllable.Length == 0)
                    return false;
            }

            return true;
        }
    }

    protected override void EmitWord(StringBuilder sb, ReadOnlySpan<char> word, int roundSeed, int languageSeed)
    {
        var seed = KsScrambleRng.Mix(KsScrambleRng.HashWord(word), roundSeed, languageSeed);
        var span = MaxSyllables >= MinSyllables ? MaxSyllables - MinSyllables + 1 : 1;
        var count = MinSyllables + (int) (KsScrambleRng.Next(ref seed) % (uint) span);
        if (count < 1)
            count = 1;

        var capitalize = char.IsUpper(word[0]);
        for (var j = 0; j < count; j++)
        {
            var syllable = Syllables[(int) (KsScrambleRng.Next(ref seed) % (uint) Syllables.Count)];
            if (capitalize)
            {
                sb.Append(char.ToUpperInvariant(syllable[0]));
                sb.Append(syllable, 1, syllable.Length - 1);
                capitalize = false;
            }
            else
                sb.Append(syllable);
        }
    }
}

/// <summary>
///     One stretchable pseudo-word: the stretch segment repeats until the output matches the
///     clear word's length; shorter words truncate the lexeme ("the" honks as "hon").
/// </summary>
[DataDefinition]
public sealed partial class KsElasticLexeme
{
    [DataField]
    public string Prefix = string.Empty;

    [DataField(required: true)]
    public string Stretch = string.Empty;

    [DataField]
    public string Suffix = string.Empty;
}

/// <summary>
///     Every word becomes a lexeme stretched to exactly the clear word's length ("maintenance"
///     honks as "hoooooooonk"). Casing survives; the lexeme pick is round-stable.
/// </summary>
public sealed partial class KsElasticLexemeObfuscation : KsWordObfuscationMethod
{
    [DataField(required: true)]
    public List<KsElasticLexeme> Lexemes = new();

    /// <summary>
    ///     Stretch cap; word lengths past it render identically.
    /// </summary>
    [DataField]
    public int MaxStretch = 16;

    protected override bool HasUsableBank
    {
        get
        {
            if (Lexemes.Count == 0)
                return false;

            // A stretchless lexeme can never match long words; treat it as misauthored.
            foreach (var lexeme in Lexemes)
            {
                if (lexeme.Stretch.Length == 0)
                    return false;
            }

            return true;
        }
    }

    protected override void EmitWord(StringBuilder sb, ReadOnlySpan<char> word, int roundSeed, int languageSeed)
    {
        var seed = KsScrambleRng.Mix(KsScrambleRng.HashWord(word), roundSeed, languageSeed);
        var lexeme = Lexemes[(int) (KsScrambleRng.Next(ref seed) % (uint) Lexemes.Count)];

        var start = sb.Length;
        var stretchLen = word.Length - lexeme.Prefix.Length - lexeme.Suffix.Length;
        if (stretchLen < lexeme.Stretch.Length)
        {
            var remaining = word.Length;
            AppendClamped(sb, lexeme.Prefix, ref remaining);
            AppendClamped(sb, lexeme.Stretch, ref remaining);
            AppendClamped(sb, lexeme.Suffix, ref remaining);
        }
        else
        {
            sb.Append(lexeme.Prefix);
            // Never cap below one full stretch segment, or a small MaxStretch could emit nothing.
            var cap = Math.Max(MaxStretch, lexeme.Stretch.Length);
            if (stretchLen > cap)
                stretchLen = cap;
            for (var j = 0; j < stretchLen; j++)
                sb.Append(lexeme.Stretch[j % lexeme.Stretch.Length]);
            sb.Append(lexeme.Suffix);
        }

        ApplyCase(sb, start, word);
    }

    private static void AppendClamped(StringBuilder sb, string part, ref int remaining)
    {
        var take = Math.Min(part.Length, remaining);
        sb.Append(part, 0, take);
        remaining -= take;
    }

    private static void ApplyCase(StringBuilder sb, int start, ReadOnlySpan<char> word)
    {
        if (word.Length > 1 && IsAllCaps(word))
        {
            for (var i = start; i < sb.Length; i++)
                sb[i] = char.ToUpperInvariant(sb[i]);
            return;
        }

        if (char.IsUpper(word[0]) && start < sb.Length)
            sb[start] = char.ToUpperInvariant(sb[start]);
    }

    private static bool IsAllCaps(ReadOnlySpan<char> word)
    {
        var hasLetter = false;
        foreach (var c in word)
        {
            if (!char.IsLetter(c))
                continue;

            if (char.IsLower(c))
                return false;

            hasLetter = true;
        }

        return hasLetter;
    }
}

/// <summary>
///     Replaces the whole message with one round-stable canned line; loc resolved here, output
///     is final display text.
/// </summary>
public sealed partial class KsCannedObfuscation : KsObfuscationMethod
{
    [DataField(required: true)]
    public List<LocId> Lines = new();

    protected override void ObfuscateInto(StringBuilder sb, string message, int roundSeed, int languageSeed)
    {
        if (Lines.Count == 0)
            return;

        var seed = KsScrambleRng.Mix(KsScrambleRng.HashWord(message), roundSeed, languageSeed);
        sb.Append(Loc.GetString(Lines[(int) (KsScrambleRng.Next(ref seed) % (uint) Lines.Count)]));
    }
}
