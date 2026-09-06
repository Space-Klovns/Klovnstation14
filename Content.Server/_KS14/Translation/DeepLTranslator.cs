using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared._KS14.CCVar;
using DeepL;
using DeepL.Model;
using Robust.Shared.Configuration;

namespace Content.Server._KS14.Translation;

/// <summary>
///     <see cref="IKsTranslator"/> backed by the official DeepL.net SDK, with multi-key rotation across
///     several DeepL accounts. Configured with a comma-separated list of API keys; each gets its own
///     <see cref="DeepLClient"/> and character counter. Translation sticks to one active key until it reaches
///     the per-key budget or DeepL reports its quota spent, then rotates to the next live key and retries the
///     SAME text, so hitting one account's limit never drops a translation. Reports unavailable only once
///     EVERY key is exhausted. Free-vs-Pro routing is automatic from each key's ":fx" suffix.
///
///     All field access happens on the game-loop main thread: the cvar callbacks, the system's Update-driven
///     poll, and translation continuations (no ConfigureAwait(false)) all resume there, so key/glossary state
///     needs no locking.
/// </summary>
public sealed class DeepLTranslator : IKsTranslator, IDisposable
{
    // One glossary named this is created/replaced by us on each account; stale ones are deleted so a
    // Free-tier single-glossary cap is never hit by leftovers from a previous run.
    private const string GlossaryName = "KS14 chat jargon";

    private readonly IConfigurationManager _cfg;

    private sealed class KeyState
    {
        public KeyState(DeepLClient client) => Client = client;

        public readonly DeepLClient Client;
        public long CharsUsed;
        public bool Exhausted;

        // Glossaries live on the account, so each key carries its own.
        public string? GlossaryId;
        public HashSet<(string Source, string Target)> GlossaryPairs = new();
    }

    private List<KeyState> _keys = new();
    private int _active;
    private int _perKeyBudget;

    // The last glossary dictionaries handed to us, re-applied to every key when the key list changes.
    private IReadOnlyList<KsGlossaryDictionary>? _glossaryData;

    public DeepLTranslator(IConfigurationManager cfg)
    {
        _cfg = cfg;
        _cfg.OnValueChanged(KsCCVars.TranslateDeeplKey, OnKeysChanged, invokeImmediately: true);
        _cfg.OnValueChanged(KsCCVars.TranslateMonthlyBudget, OnBudgetChanged, invokeImmediately: true);
    }

    // Available while at least one key still has budget.
    public bool IsAvailable => _keys.Any(k => !k.Exhausted);

    private void OnBudgetChanged(int budget) => _perKeyBudget = budget;

    private void OnKeysChanged(string raw)
    {
        foreach (var key in _keys)
            (key.Client as IDisposable)?.Dispose();

        _keys = new();
        _active = 0;

        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                _keys.Add(new KeyState(new DeepLClient(part)));
            }
            catch (Exception)
            {
                // Malformed key: skip it and keep the rest usable, rather than throwing on startup.
            }
        }

        if (_glossaryData != null)
            _ = BuildGlossaryAsync(_glossaryData, CancellationToken.None);
    }

    // The current key, advancing past any that are exhausted; null when every key is spent.
    private KeyState? ActiveKey()
    {
        for (var i = 0; i < _keys.Count; i++)
        {
            var idx = (_active + i) % _keys.Count;
            if (_keys[idx].Exhausted)
                continue;

            _active = idx;
            return _keys[idx];
        }

        return null;
    }

    public async Task<string?> TranslateAsync(string text, string sourceLang, string targetLang, string? context, CancellationToken cancel)
    {
        var baseTarget = BaseLang(targetLang);
        var options = new TextTranslateOptions
        {
            Context = context,
            Formality = Formality.Default,
            // TagHandling stays null: we translate plain text only and re-wrap ourselves.
            SentenceSplittingMode = SentenceSplittingMode.All,
        };

        // Rotate through the keys: an account that is out of quota is retired and the SAME text is retried on
        // the next live key, so hitting a limit never drops a translation.
        for (var attempt = 0; attempt < _keys.Count; attempt++)
        {
            var key = ActiveKey();
            if (key == null)
                return null;

            // Attach the glossary only when a dictionary covers this exact (source, base-target) pair on THIS
            // account; passing an id for an uncovered pair can error. Base target covers its regional variants.
            options.GlossaryId = key.GlossaryId is { } gid && key.GlossaryPairs.Contains((sourceLang, baseTarget))
                ? gid
                : null;

            try
            {
                var result = await key.Client.TranslateTextAsync(text, sourceLang, targetLang, options, cancel);
                key.CharsUsed += text.Length;
                if (_perKeyBudget > 0 && key.CharsUsed >= _perKeyBudget)
                    key.Exhausted = true;
                return result.Text;
            }
            catch (QuotaExceededException)
            {
                key.Exhausted = true; // account out for the period: rotate and retry on the next key
            }
            catch (AuthorizationException)
            {
                key.Exhausted = true; // key invalid: retire it so rotation skips it
            }
            catch (DeepLException)
            {
                return null; // rate-limit / connection / other transient: keep the original, do not rotate
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        return null;
    }

    public async Task SetGlossaryAsync(IReadOnlyList<KsGlossaryDictionary> dictionaries, CancellationToken cancel)
    {
        _glossaryData = dictionaries;
        await BuildGlossaryAsync(dictionaries, cancel);
    }

    private async Task BuildGlossaryAsync(IReadOnlyList<KsGlossaryDictionary> dictionaries, CancellationToken cancel)
    {
        // Compile the usable directional dictionaries once (base codes, non-empty, source != target)...
        var usable = dictionaries
            .Select(d => (Source: BaseLang(d.Source), Target: BaseLang(d.Target), d.Entries))
            .Where(d => d.Source.Length > 0 && d.Target.Length > 0 && d.Source != d.Target && d.Entries.Count > 0)
            .ToList();

        var pairs = usable.Select(d => (d.Source, d.Target)).ToHashSet();
        var dicts = usable
            .Select(d => new MultilingualGlossaryDictionaryEntries(d.Source, d.Target, new GlossaryEntries(d.Entries)))
            .ToArray();

        // ...then rebuild it on every key, since any of them may become the active account.
        foreach (var key in _keys)
        {
            try
            {
                var existing = await key.Client.ListMultilingualGlossariesAsync(cancel);
                foreach (var glossary in existing)
                {
                    if (glossary.Name == GlossaryName)
                        await key.Client.DeleteMultilingualGlossaryAsync(glossary.GlossaryId, cancel);
                }

                if (dicts.Length == 0)
                {
                    key.GlossaryId = null;
                    key.GlossaryPairs = new();
                    continue;
                }

                var info = await key.Client.CreateMultilingualGlossaryAsync(GlossaryName, dicts, cancel);
                key.GlossaryId = info.GlossaryId;
                key.GlossaryPairs = pairs;
            }
            catch (DeepLException)
            {
                // Unsupported pair, quota, auth, etc.: this key runs without a glossary rather than failing.
                key.GlossaryId = null;
                key.GlossaryPairs = new();
            }
            catch (OperationCanceledException)
            {
                return; // shutdown
            }
        }
    }

    public async Task<TranslationUsage?> GetUsageAsync(CancellationToken cancel)
    {
        if (_keys.Count == 0)
            return null;

        long totalUsed = 0, totalLimit = 0;
        var anyLive = false;

        foreach (var key in _keys)
        {
            // A free-tier credit is one-time and does not recover, so a retired key is never re-polled.
            if (key.Exhausted)
                continue;

            try
            {
                var usage = await key.Client.GetUsageAsync(cancel);
                var used = usage.Character?.Count ?? 0;
                var limit = usage.Character?.Limit ?? 0;

                key.CharsUsed = used; // reconcile the local counter to DeepL's authoritative count
                if (usage.AnyLimitReached || (_perKeyBudget > 0 && used >= _perKeyBudget))
                    key.Exhausted = true;
                else
                    anyLive = true;

                totalUsed += used;
                totalLimit += limit;
            }
            catch (DeepLException)
            {
                // Leave this key's state as-is; a real quota error during translation will retire it.
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        return new TranslationUsage(totalUsed, totalLimit, !anyLive);
    }

    private static string BaseLang(string code)
    {
        var trimmed = code.Trim();
        var dash = trimmed.IndexOf('-');
        var basePart = dash < 0 ? trimmed : trimmed[..dash];
        return basePart.ToUpperInvariant();
    }

    public void Dispose()
    {
        _cfg.UnsubValueChanged(KsCCVars.TranslateDeeplKey, OnKeysChanged);
        _cfg.UnsubValueChanged(KsCCVars.TranslateMonthlyBudget, OnBudgetChanged);
        foreach (var key in _keys)
            (key.Client as IDisposable)?.Dispose();
        _keys = new();
    }
}
