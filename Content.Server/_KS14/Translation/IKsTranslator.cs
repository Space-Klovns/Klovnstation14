using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Content.Server._KS14.Translation;

/// <summary>
///     Abstraction over a translation backend so the real DeepL implementation can be swapped for a fake
///     in integration tests. See <see cref="KsTranslationSystem"/> and deepl-translation-implementation.md.
/// </summary>
public interface IKsTranslator
{
    /// <summary>
    ///     Whether the translator is configured and ready (e.g. a DeepL key is present and valid).
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    ///     Replace the backend's glossary with these directional term dictionaries. The DeepL implementation
    ///     compiles them into one multilingual glossary and applies the matching dictionary to each call whose
    ///     (source, target) pair is covered; the fake just records them. Safe to call repeatedly (prototype
    ///     reloads) and a no-op while the backend is unavailable. Failures are swallowed: a broken glossary
    ///     must never break translation, only forgo the term substitutions.
    /// </summary>
    Task SetGlossaryAsync(IReadOnlyList<KsGlossaryDictionary> dictionaries, CancellationToken cancel);

    /// <summary>
    ///     Translate <paramref name="text"/> from <paramref name="sourceLang"/> (base code, e.g. "EN") to
    ///     <paramref name="targetLang"/> (may be a regional variant, e.g. "PT-BR"). <paramref name="context"/>
    ///     is an optional, unbilled quality hint. Returns the translated text, or null on any failure
    ///     (the caller keeps the original untranslated).
    /// </summary>
    Task<string?> TranslateAsync(string text, string sourceLang, string targetLang, string? context, CancellationToken cancel);

    /// <summary>
    ///     Query current billing-period usage, or null if unavailable.
    /// </summary>
    Task<TranslationUsage?> GetUsageAsync(CancellationToken cancel);
}

/// <summary>
///     Backend-agnostic usage snapshot for the current billing period.
/// </summary>
public sealed record TranslationUsage(long Used, long Limit, bool AnyLimitReached);

/// <summary>
///     One directional glossary dictionary: force <paramref name="Entries"/> (source term -> target term)
///     when translating from <paramref name="Source"/> to <paramref name="Target"/> (both base codes).
/// </summary>
public sealed record KsGlossaryDictionary(string Source, string Target, IReadOnlyDictionary<string, string> Entries);
