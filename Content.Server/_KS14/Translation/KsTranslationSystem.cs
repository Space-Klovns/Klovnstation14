using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Chat;
using Content.Shared._KS14.Translation;
using Content.Shared.Chat;
using Content.Shared.GameTicking;
using Robust.Server.Player;
using Robust.Shared.Collections;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Translation;

/// <summary>
///     Orchestrates per-reader, swap-in-place DeepL chat translation. The original message is delivered
///     synchronously by the normal chat path; this system fires an async translation and, when it returns,
///     sends a <see cref="MsgReplaceChatMessage"/> to each reader whose language differs from the speaker.
///
///     Callers (the Local/LOOC/Dead seams in ChatSystem, the OOC seam in ChatManager) drive it in three steps:
///     <see cref="TryBeginLocal"/>/<see cref="TryBeginSession"/> to gate a message, <see cref="TryReader"/>/
///     <see cref="TryReaderShared"/> per reader to stamp a message id and queue the work, then
///     <see cref="EndMessage"/> to apply the per-speaker cooldown. See deepl-translation-implementation.md.
/// </summary>
public sealed class KsTranslationSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly INetConfigurationManager _netCfg = default!;
    [Dependency] private readonly IServerNetManager _net = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    /// <summary>
    ///     The translation backend. Deliberately NOT a [Dependency] so integration tests can swap in a fake;
    ///     defaults to the real DeepL implementation in <see cref="Initialize"/>.
    /// </summary>
    public IKsTranslator Translator = default!;

    // Completed async calls and cache-hit swaps, both drained on the main thread in Update.
    private readonly ConcurrentQueue<AsyncResult> _asyncResults = new();
    private readonly ConcurrentQueue<CachedSwap> _cachedSwaps = new();

    // (src|tgt|text) hash -> translated text. Persists across rounds; growth stops once every key's budget is spent.
    private readonly Dictionary<string, string> _cache = new();
    // (src|tgt|text) hash -> readers awaiting the single in-flight call for that key (collapses duplicates).
    private readonly Dictionary<string, InFlight> _inFlight = new();
    // Per-speaker cooldown, drained in Update like TtsSystem. Keyed with the last message text so a same-text
    // companion line (a local say and its radio copy) or a repeat is not throttled, only a new distinct call.
    private readonly Dictionary<NetUserId, (TimeSpan Until, string Text)> _cooldownUntil = new();

    // Rolling per-channel buffer of recent plain lines, joined into the unbilled DeepL "context" hint.
    private readonly Dictionary<ChatChannel, Queue<string>> _channelHistory = new();

    private readonly CancellationTokenSource _shutdownCts = new();

    private int _nextMessageId;

    private TimeSpan _nextUsagePoll;
    private bool _usagePollInFlight;

    // cvar-bound settings
    private bool _enabled;
    private float _cooldownBaseline;
    private float _cooldownPerChar;
    private int _minLength;
    private int _maxLength;
    private int _contextLines;
    private float _usagePollMinutes;

    // A short, unbilled hint that biases DeepL toward the setting's register and jargon. Prepended to the
    // rolling per-channel history to form the full "context" string.
    private const string SettingHint =
        "Dialogue from a sci-fi space station role-playing game. Casual crew slang; departments include Security, Medical, Engineering, Cargo and Science.";

    public override void Initialize()
    {
        base.Initialize();

        _net.RegisterNetMessage<MsgReplaceChatMessage>();

        Translator = new DeepLTranslator(_cfg);

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        RebuildGlossary();

        _cfg.OnValueChanged(KsCCVars.TranslateEnabled, v => _enabled = v, invokeImmediately: true);
        _cfg.OnValueChanged(KsCCVars.TranslateCooldownBaseline, v => _cooldownBaseline = v, invokeImmediately: true);
        _cfg.OnValueChanged(KsCCVars.TranslateCooldownPerChar, v => _cooldownPerChar = v, invokeImmediately: true);
        _cfg.OnValueChanged(KsCCVars.TranslateMinLength, v => _minLength = v, invokeImmediately: true);
        _cfg.OnValueChanged(KsCCVars.TranslateMaxLength, v => _maxLength = v, invokeImmediately: true);
        _cfg.OnValueChanged(KsCCVars.TranslateContextLines, v => _contextLines = v, invokeImmediately: true);
        _cfg.OnValueChanged(KsCCVars.TranslateUsagePollMinutes, v => _usagePollMinutes = v, invokeImmediately: true);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _shutdownCts.Cancel();
        _shutdownCts.Dispose();
        (Translator as IDisposable)?.Dispose();
    }

    private void OnCleanup(RoundRestartCleanupEvent ev)
    {
        _cooldownUntil.Clear();
        _channelHistory.Clear(); // conversational context does not carry across rounds
        // Cache/budget intentionally persist across rounds (budget is a billing-period concept).
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs ev)
    {
        if (ev.WasModified<KsTranslationGlossaryPrototype>())
            RebuildGlossary();
    }

    /// <summary>
    ///     Collects the glossary prototypes into directional dictionaries and hands them to the translator,
    ///     which (for DeepL) compiles them into one multilingual glossary. Fire-and-forget: the glossary is a
    ///     quality bias, never a prerequisite for translating. Also the prototype-reload hook.
    /// </summary>
    public void RebuildGlossary()
    {
        var dictionaries = new List<KsGlossaryDictionary>();
        foreach (var proto in _proto.EnumeratePrototypes<KsTranslationGlossaryPrototype>())
        {
            if (proto.Entries.Count == 0)
                continue;

            dictionaries.Add(new KsGlossaryDictionary(proto.Source, proto.Target, proto.Entries));
        }

        _ = Translator.SetGlossaryAsync(dictionaries, _shutdownCts.Token);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Drain expired cooldowns (mirrors TtsSystem.Update).
        if (_cooldownUntil.Count > 0)
        {
            var done = new ValueList<NetUserId>();
            foreach (var (user, cd) in _cooldownUntil)
            {
                if (cd.Until > _timing.CurTime)
                    continue;
                done.Add(user);
            }
            foreach (var user in done)
                _cooldownUntil.Remove(user);
        }

        // Cache-hit swaps: sent from Update (after the original was dispatched). Note the swap and the
        // original are independent unordered net messages, so on the cache-hit path they can still be
        // reordered on the wire; the client buffers a swap that outruns its original (see ChatUIController).
        while (_cachedSwaps.TryDequeue(out var swap))
            SendSwap(swap.MessageId, swap.Reader, swap.Translated);

        // Completed async calls: cache the result and fan it out to every reader that attached to it.
        while (_asyncResults.TryDequeue(out var res))
        {
            if (!_inFlight.Remove(res.CacheKey, out var inflight))
                continue;

            if (res.Translated == null)
                continue; // failure/timeout: readers keep the original

            _cache[res.CacheKey] = res.Translated;
            foreach (var (id, reader) in inflight.Readers)
                SendSwap(id, reader, res.Translated);
        }

        PollUsage();
    }

    /// <summary>
    ///     Periodically refreshes each key's DeepL usage so an account that has hit its limit is retired
    ///     before a translation call has to fail into it. The reconciliation and rotation happen inside the
    ///     translator; this only drives the cadence. Runs while translation is enabled.
    /// </summary>
    private void PollUsage()
    {
        if (!_enabled || _usagePollMinutes <= 0 || _usagePollInFlight)
            return;
        if (_timing.CurTime < _nextUsagePoll)
            return;

        _nextUsagePoll = _timing.CurTime + TimeSpan.FromMinutes(_usagePollMinutes);
        _usagePollInFlight = true;
        _ = RunUsagePollAsync();
    }

    private async Task RunUsagePollAsync()
    {
        try
        {
            await Translator.GetUsageAsync(_shutdownCts.Token);
        }
        catch (Exception e)
        {
            Log.Warning($"Chat translation usage poll failed: {e}");
        }

        // The continuation resumes on the main thread (no ConfigureAwait(false)), so this is safe here.
        _usagePollInFlight = false;
    }

    private void SendSwap(int messageId, INetChannel reader, string translated)
    {
        if (!reader.IsConnected)
            return;

        _net.ServerSendMessage(new MsgReplaceChatMessage { MessageId = messageId, Message = translated }, reader);
    }

    #region Message-level gating

    /// <summary>
    ///     Local/say seam entry: gate a message from an in-world speaker entity (also LOOC, Whisper, Radio).
    ///     Returns true and a context to thread through <see cref="TryReader"/> if the message is eligible.
    /// </summary>
    public bool TryBeginLocal(ChatChannel channel, string plain, EntityUid speaker, [NotNullWhen(true)] out KsTranslationContext? ctx)
    {
        ctx = null;
        if (!_playerManager.TryGetSessionByEntity(speaker, out var session))
            return false;

        var speakerLang = _netCfg.GetClientCVar(session.Channel, KsCCVars.TranslateLanguage);
        return TryBeginCommon(channel, plain, speakerLang, session.UserId, out ctx);
    }

    /// <summary>
    ///     Session seam entry: gate a message whose speaker is a player session (OOC, Dead).
    /// </summary>
    public bool TryBeginSession(ChatChannel channel, string plain, ICommonSession speaker, [NotNullWhen(true)] out KsTranslationContext? ctx)
    {
        var speakerLang = _netCfg.GetClientCVar(speaker.Channel, KsCCVars.TranslateLanguage);
        return TryBeginCommon(channel, plain, speakerLang, speaker.UserId, out ctx);
    }

    private bool TryBeginCommon(ChatChannel channel, string plain, string speakerLang, NetUserId speaker, [NotNullWhen(true)] out KsTranslationContext? ctx)
    {
        ctx = null;

        if (!_enabled || !Translator.IsAvailable)
            return false;

        var len = plain.Length;
        if (len < _minLength || len > _maxLength)
            return false;

        if (string.IsNullOrWhiteSpace(speakerLang))
            return false;
        var speakerBase = BaseLang(speakerLang);
        if (speakerBase.Length == 0)
            return false;

        // One utterance can fan out to several lines (a local say and its radio copy carry the same text), and
        // the say path sets this cooldown before the radio path begins. Throttle only a NEW distinct message: a
        // same-text companion or repeat is let through and resolves to a free cache hit.
        if (_cooldownUntil.TryGetValue(speaker, out var cd) && cd.Until > _timing.CurTime && cd.Text != plain)
            return false;

        // Build the context from the buffer as it stands (excludes this line), then record this line for the
        // benefit of the next message on this channel.
        ctx = new KsTranslationContext { SpeakerBase = speakerBase, Speaker = speaker, Length = len, Text = plain, Context = BuildContext(channel) };
        AppendContext(channel, plain);
        return true;
    }

    /// <summary>
    ///     Apply the per-speaker cooldown, but only if this message actually started a real API call
    ///     (cache hits and same-language readers are free and should not rate-limit the speaker).
    /// </summary>
    public void EndMessage(KsTranslationContext ctx)
    {
        if (!ctx.StartedCall)
            return;

        var until = _timing.CurTime + TimeSpan.FromSeconds(_cooldownBaseline + _cooldownPerChar * ctx.Length);
        _cooldownUntil[ctx.Speaker] = (until, ctx.Text);
    }

    #endregion

    #region Per-reader

    /// <summary>
    ///     Per-reader seam for channels delivered one message per reader (Local). Allocates a fresh message
    ///     id for this reader and queues the translation. Returns the id to stamp on the delivered
    ///     <see cref="Content.Shared.Chat.ChatMessage"/>, or null if this reader needs no translation.
    /// </summary>
    public int? TryReader(string plain, KsTranslationContext ctx, INetChannel reader)
    {
        if (!TryResolveTarget(ctx, reader, out var target))
            return null;

        var id = ++_nextMessageId;
        Enqueue(id, reader, plain, ctx, target);
        return id;
    }

    /// <summary>
    ///     Per-reader seam for broadcast channels where every reader shares one delivered message (OOC).
    ///     Uses one shared message id, allocated lazily on the first reader that actually needs translation.
    /// </summary>
    public void TryReaderShared(string plain, KsTranslationContext ctx, INetChannel reader, ref int? sharedId)
    {
        if (!TryResolveTarget(ctx, reader, out var target))
            return;

        sharedId ??= ++_nextMessageId;
        Enqueue(sharedId.Value, reader, plain, ctx, target);
    }

    /// <summary>
    ///     Radio seam. Radio delivers one shared <see cref="MsgChatMessage"/> to every receiver, so a
    ///     per-reader translation swap MUST NOT mutate that shared object. Given the gating context begun once
    ///     for the whole broadcast (or null if the message was not eligible) and one reader session, this
    ///     returns either the shared message unchanged, or a CLONE stamped with a fresh id whose translation
    ///     has been queued. Callers send whatever this returns to that reader's channel.
    /// </summary>
    public MsgChatMessage ApplyRadioReader(MsgChatMessage shared, KsTranslationContext? ctx, ICommonSession reader)
    {
        if (ctx is null)
            return shared;

        // Translate exactly what this reader will see: if the jammer already swapped in a garbled clone,
        // this keys off that garbled text (harmless) rather than leaking the clear original onto it.
        var id = TryReader(shared.Message.Message, ctx, reader.Channel);
        if (id is not { } messageId)
            return shared;

        var m = shared.Message;
        var clone = new ChatMessage(m.Channel, m.Message, m.WrappedMessage, m.SenderEntity, m.SenderKey, m.HideChat, m.MessageColorOverride, m.AudioPath, m.AudioVolume)
        {
            MessageId = messageId,
        };
        return new MsgChatMessage { Message = clone };
    }

    private bool TryResolveTarget(KsTranslationContext ctx, INetChannel reader, [NotNullWhen(true)] out string? target)
    {
        target = null;

        var readerLang = _netCfg.GetClientCVar(reader, KsCCVars.TranslateLanguage);
        if (string.IsNullOrWhiteSpace(readerLang))
            return false;
        if (BaseLang(readerLang) == ctx.SpeakerBase)
            return false;

        target = NormalizeTarget(readerLang);
        return true;
    }

    private void Enqueue(int id, INetChannel reader, string plain, KsTranslationContext ctx, string target)
    {
        var key = CacheKey(ctx.SpeakerBase, target, plain);

        if (_cache.TryGetValue(key, out var cached))
        {
            _cachedSwaps.Enqueue(new CachedSwap(id, reader, cached));
            return;
        }

        if (_inFlight.TryGetValue(key, out var infl))
        {
            infl.Readers.Add((id, reader));
            return;
        }

        _inFlight[key] = new InFlight(id, reader);
        ctx.StartedCall = true;
        _ = RunTranslateAsync(key, plain, ctx.SpeakerBase, target, ctx.Context);
    }

    private async Task RunTranslateAsync(string key, string plain, string src, string target, string context)
    {
        string? translated = null;
        try
        {
            translated = await Translator.TranslateAsync(plain, src, target, context, _shutdownCts.Token);
        }
        catch (Exception e)
        {
            Log.Warning($"Chat translation task failed: {e}");
        }

        // The continuation runs on the main thread (RobustToolbox installs a main-thread
        // SynchronizationContext and we never use ConfigureAwait(false)); the queue keeps the actual
        // net send and shared-state mutation explicitly in Update regardless.
        _asyncResults.Enqueue(new AsyncResult(key, translated));
    }

    #endregion

    #region Helpers

    private static string BaseLang(string code)
    {
        var trimmed = code.Trim();
        var dash = trimmed.IndexOf('-');
        var basePart = dash < 0 ? trimmed : trimmed[..dash];
        return basePart.ToUpperInvariant();
    }

    private static string NormalizeTarget(string code) => code.Trim().ToUpperInvariant();

    /// <summary>
    ///     Builds the unbilled DeepL "context" hint for a channel: the static setting hint followed by the
    ///     recent plain lines on that channel. Context characters are not billed, so this is a free quality
    ///     bias that is strongest for exactly our case: short, low-context chat lines.
    /// </summary>
    private string BuildContext(ChatChannel channel)
    {
        if (_contextLines <= 0 || !_channelHistory.TryGetValue(channel, out var history) || history.Count == 0)
            return SettingHint;

        return $"{SettingHint}\n{string.Join('\n', history)}";
    }

    private void AppendContext(ChatChannel channel, string plain)
    {
        if (_contextLines <= 0)
            return;

        if (!_channelHistory.TryGetValue(channel, out var history))
            _channelHistory[channel] = history = new Queue<string>();

        history.Enqueue(plain);
        while (history.Count > _contextLines)
            history.Dequeue();
    }

    private static string CacheKey(string src, string tgt, string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{src}|{tgt}|{text}"));
        return Convert.ToHexString(hash);
    }

    private sealed class InFlight
    {
        public readonly List<(int Id, INetChannel Reader)> Readers = new();

        public InFlight(int id, INetChannel reader)
        {
            Readers.Add((id, reader));
        }
    }

    private readonly record struct AsyncResult(string CacheKey, string? Translated);
    private readonly record struct CachedSwap(int MessageId, INetChannel Reader, string Translated);

    #endregion
}

/// <summary>
///     Per-message translation context threaded from a Begin call, through the per-reader calls, to
///     <see cref="KsTranslationSystem.EndMessage"/>. Public so the ChatSystem/ChatManager seams can hold it.
/// </summary>
public sealed class KsTranslationContext
{
    /// <summary>The speaker's base language (e.g. "EN"), used as the DeepL source and the skip comparison.</summary>
    public string SpeakerBase = "";

    /// <summary>The speaker, for cooldown keying.</summary>
    public NetUserId Speaker;

    /// <summary>The plain message length, for the per-character cooldown.</summary>
    public int Length;

    /// <summary>The plain message text, letting a same-text companion line or repeat bypass the cooldown.</summary>
    public string Text = "";

    /// <summary>The unbilled DeepL context hint (setting hint + recent channel lines) for this message.</summary>
    public string Context = "";

    /// <summary>Set once a real (non-cached) API call is started for this message.</summary>
    public bool StartedCall;
}
