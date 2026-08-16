using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Language;

/// <summary>
///     Built once per spoken message on the server, so every non-understander sees the same
///     scramble. Null context anywhere means default language, vanilla pipeline.
/// </summary>
public sealed class KsUtteranceContext
{
    public readonly KsLanguagePrototype Language;
    public readonly string ClearMessage;
    public readonly int RoundSeed;
    public readonly int LanguageSeed;

    private string? _obfuscated;

    public KsUtteranceContext(KsLanguagePrototype language, string clearMessage, int roundSeed)
    {
        Language = language;
        ClearMessage = clearMessage;
        RoundSeed = roundSeed;
        LanguageSeed = (int) KsScrambleRng.HashWord(language.ID);
    }

    public ProtoId<KsLanguagePrototype> LanguageId => Language.ID;

    /// <summary>
    ///     The scrambled variant, computed at most once per utterance.
    /// </summary>
    public string Obfuscated => _obfuscated ??= Language.Obfuscation.Obfuscate(ClearMessage, RoundSeed, LanguageSeed);

    /// <summary>
    ///     Relay paths carrying a derived variant (a distance-fuzzed whisper) must scramble the
    ///     text they actually transmit, not the original.
    /// </summary>
    public string ObfuscateText(string text)
    {
        return text == ClearMessage ? Obfuscated : Language.Obfuscation.Obfuscate(text, RoundSeed, LanguageSeed);
    }
}
