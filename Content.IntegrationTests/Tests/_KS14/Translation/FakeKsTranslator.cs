#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._KS14.Translation;

namespace Content.IntegrationTests.Tests._KS14.Translation;

/// <summary>
///     Deterministic <see cref="IKsTranslator"/> test double. Records every request and returns a sentinel
///     transform. An optional <see cref="Gate"/> lets a test hold a call in-flight to exercise dedup.
/// </summary>
public sealed class FakeKsTranslator : IKsTranslator
{
    public readonly List<(string Text, string Source, string Target, string? Context)> Requests = new();

    /// <summary>If set, translation awaits this before returning, so a test can pin a call in-flight.</summary>
    public Task? Gate;

    public bool Available = true;

    public bool IsAvailable => Available;

    /// <summary>Glossary dictionaries handed to the translator, for assertions.</summary>
    public IReadOnlyList<KsGlossaryDictionary> Glossary = new List<KsGlossaryDictionary>();

    public async Task<string?> TranslateAsync(string text, string sourceLang, string targetLang, string? context, CancellationToken cancel)
    {
        Requests.Add((text, sourceLang, targetLang, context));

        if (Gate != null)
            await Gate;

        return $"[{targetLang}] {text}";
    }

    public Task SetGlossaryAsync(IReadOnlyList<KsGlossaryDictionary> dictionaries, CancellationToken cancel)
    {
        Glossary = dictionaries;
        return Task.CompletedTask;
    }

    public Task<TranslationUsage?> GetUsageAsync(CancellationToken cancel)
        => Task.FromResult<TranslationUsage?>(new TranslationUsage(0, 500000, false));
}
