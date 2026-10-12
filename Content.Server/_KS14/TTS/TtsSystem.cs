using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.TTS;
using Content.Shared._KS14.WordFilter;
using Content.Shared.Chat;
using Content.Shared.GameTicking;
using Content.Shared.Preferences;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._KS14.TTS;

/// <inheritdoc/>
public sealed partial class TtsSystem : SharedTtsSystem
{
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private WordFilterSystem _wordFilterSystem = default!;

    private readonly HttpClient _httpClient = new();
    private readonly Dictionary<string, TtsClip> _cache = new();

    private string _ttsEndpoint = "";
    private bool _enabled = false;
    private TtsCodecMode _codecMode = TtsCodecMode.Vorbis;
    private int _opusBitrate;
    private int _opusComplexity;

    /// <summary>
    ///     The voices handed out at random: every <see cref="TtsVoicePrototype.Selectable"/> one.
    /// </summary>
    private readonly List<ProtoId<TtsVoicePrototype>> _voiceIds = [];

    private static readonly TimeSpan BaselineCooldown = TimeSpan.FromSeconds(0.5f);
    private static readonly TimeSpan CooldownPerChar = TimeSpan.FromSeconds(0.021f);

    /// <summary>
    ///     Text shorter than this won't be processed.
    /// </summary>
    private const int MinTextLength = 1;
    /// <summary>
    ///     Text longer than this will be truncated.
    /// </summary>
    private const int MaxTextLength = 50;

    /// <summary>
    ///     Whether TTS is on. Says nothing about whether the endpoint is actually answering.
    /// </summary>
    public bool Enabled => _enabled;

    public override void Initialize()
    {
        ReloadVoices();

        Subs.CVar(_configurationManager, KsCCVars.TtsEndpoint, value => _ttsEndpoint = value, invokeImmediately: true);
        Subs.CVar(_configurationManager, KsCCVars.TtsEnabled, value => _enabled = value, invokeImmediately: true);
        Subs.CVar(_configurationManager, KsCCVars.TtsCodec, OnCodecChanged, invokeImmediately: true);
        Subs.CVar(_configurationManager, KsCCVars.TtsOpusBitrate, value => _opusBitrate = KsTtsOpus.ClampBitrate(value), invokeImmediately: true);
        Subs.CVar(_configurationManager, KsCCVars.TtsOpusComplexity, value => _opusComplexity = KsTtsOpus.ClampComplexity(value), invokeImmediately: true);
    }

    private void OnCodecChanged(string value)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "vorbis":
                _codecMode = TtsCodecMode.Vorbis;
                break;
            case "opus":
                _codecMode = TtsCodecMode.Opus;
                break;
            case "transcode":
                _codecMode = TtsCodecMode.Transcode;
                break;
            default:
                _codecMode = TtsCodecMode.Vorbis;
                Log.Warning($"Unknown {KsCCVars.TtsCodec.Name} '{value}': expected vorbis, opus or transcode. Using vorbis.");
                break;
        }
    }

    [SubscribeLocalEvent]
    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (!args.WasModified<TtsVoicePrototype>())
            return;

        ReloadVoices();
    }

    private void ReloadVoices()
    {
        _voiceIds.Clear();

        foreach (var prototype in ProtoMan.EnumeratePrototypes<TtsVoicePrototype>())
        {
            if (prototype.Selectable)
                _voiceIds.Add(prototype.ID);
        }
    }

    [SubscribeLocalEvent]
    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        // Null is "random", which OnSpoke settles the first time they talk. The profile was validated when it was
        //      loaded, but a reload may have made its voice unselectable since.
        if (args.Profile.TtsVoice is not { } voice ||
            !HumanoidCharacterProfile.IsSelectableTtsVoice(voice, ProtoMan))
            return;

        EnsureComp<TtsVoiceComponent>(args.Mob).Id = voice;
    }

    [SubscribeLocalEvent]
    private void OnSpoke(EntitySpokeEvent args)
    {
        // No TTS for exotic speech: the audio goes to all of PVS and would voice the clear text.
        if (args.KsLanguage != null)
            return;

        var component = EnsureComp<TtsVoiceComponent>(args.Source);

        // Unset, or set to a voice a prototype reload has since removed.
        if (component.Id == null || !ProtoMan.HasIndex(component.Id.Value))
        {
            if (_voiceIds.Count == 0)
                return;

            component.Id = _robustRandom.Pick(_voiceIds); // lol
        }

        TrySpeak(args.Source, component.Id.Value, args.Message);
    }

    public void TrySpeak(EntityUid speakerUid, ProtoId<TtsVoicePrototype> voiceProto, string text)
    {
        if (text.Length < MinTextLength)
            return;

        if (!_enabled)
            return;

        var component = EnsureComp<TtsVoiceComponent>(speakerUid);
        if (_gameTiming.CurTime < component.CooldownEnd)
            return;

        // trim text
        if (text.Length > MaxTextLength)
            text = text[..MaxTextLength];

        component.CooldownEnd = _gameTiming.CurTime + BaselineCooldown + (CooldownPerChar * text.Length);

        // grimnuke alloc
        var filteredText = new string(text.ToCharArray());

        // Category for text that is always sent
        var baseTtsCategory = TtsFilteredCategory.DontProcess;

        if (_wordFilterSystem.FilterAndReplaceString(ref filteredText, WordFilterCategory.Slur))
        {
            baseTtsCategory = TtsFilteredCategory.WaitForFiltered;
            _ = Speak(speakerUid, voiceProto, filteredText, TtsFilteredCategory.Filtered);
        }

        _ = Speak(speakerUid, voiceProto, text, baseTtsCategory);
    }

    public async Task Speak(EntityUid speakerUid, ProtoId<TtsVoicePrototype> voiceProto, string text, TtsFilteredCategory category)
    {
        if (string.IsNullOrWhiteSpace(text) ||
            !ProtoMan.TryIndex(voiceProto, out var proto))
            return;

        if (await Synthesize(proto, text) is not { } clip)
            return;

        // The speaker may have gone while the endpoint was answering.
        if (TerminatingOrDeleted(speakerUid))
            return;

        RaiseNetworkEvent(new PlayTtsEvent(GetNetEntity(speakerUid), clip.Data, category, clip.Codec), Filter.Pvs(speakerUid));
    }

    /// <summary>
    ///     Gets <paramref name="text"/> said in <paramref name="voice"/>, from the cache or the endpoint, encoded per
    ///         <c>klovn.tts.codec</c>. Null if the endpoint failed. Resumes on the game thread.
    /// </summary>
    public async Task<TtsClip?> Synthesize(TtsVoicePrototype voice, string text)
    {
        // Read once: the continuation below must agree with the request about which mode it was made in.
        var mode = _codecMode;
        var bitrate = _opusBitrate;
        var complexity = _opusComplexity;

        var cacheId = BuildCacheId(voice, text, mode, bitrate, complexity);
        if (_cache.TryGetValue(cacheId, out var cached))
            return cached;

        byte[] bytes;
        try
        {
            var request = new TtsRequestBody
            {
                Voice = voice.Voice,
                Input = text,
                // Only ever set for opus, so the default mode sends exactly the request it always has.
                Format = mode == TtsCodecMode.Opus ? "opus" : null,
            };

            var response = await _httpClient.PostAsJsonAsync(_ttsEndpoint, request);
            response.EnsureSuccessStatusCode();

            bytes = await response.Content.ReadAsByteArrayAsync();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException
                                      or UriFormatException or NotSupportedException or IOException)
        {
            // A warning, not an error: the endpoint being down is an operational problem, not a bug.
            Log.Warning($"TTS request for {voice.ID} failed: {e.Message}");
            return null;
        }

        // Clients refuse anything bigger, and a clip this size is not a spoken line anyway.
        if (bytes.Length > KsTtsPreviewMessage.MaxPreviewBytes)
        {
            Log.Warning($"TTS reply for {voice.ID} is {bytes.Length} bytes, over the {KsTtsPreviewMessage.MaxPreviewBytes} limit; dropped.");
            return null;
        }

        // Labelled by content, not by mode: an endpoint that ignores the requested format still plays.
        var codec = KsTtsOpus.Identify(bytes);

        if (mode == TtsCodecMode.Transcode && codec != TtsCodec.Opus)
        {
            // Encoding a few seconds of audio takes tens of milliseconds, so it stays off the game thread.
            var transcoded = await Task.Run(() =>
                KsTtsTranscoder.TryTranscode(bytes, bitrate, complexity, out var opus) ? opus : null);

            if (transcoded != null)
            {
                bytes = transcoded;
                codec = TtsCodec.Opus;
            }
            else
            {
                Log.Warning($"TTS: couldn't transcode the endpoint's reply for {voice.ID} to Opus; sending it as it came.");
            }
        }

        // Nothing a client can play: sending it on would only fail there.
        if (codec is not { } playableCodec)
        {
            Log.Warning($"TTS reply for {voice.ID} is neither Ogg Vorbis nor Ogg Opus; dropped. " +
                        $"'{KsCCVars.TtsCodec.Name} transcode' converts WAV.");
            return null;
        }

        var clip = new TtsClip(playableCodec, bytes);
        _cache[cacheId] = clip;
        return clip;
    }

    private static string BuildCacheId(TtsVoicePrototype proto, string text, TtsCodecMode mode, int bitrate, int complexity)
    {
        var raw =
            $"{proto.Voice}|{text}|{mode}|{bitrate}|{complexity}";

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }

    public sealed record TtsRequestBody
    {
        [JsonPropertyName("text")]
        public string Input { get; set; } = default!;

        [JsonPropertyName("voice")]
        public string Voice { get; set; } = default!;

        /// <summary>
        ///     Asks the endpoint for this container instead of its default. Left out of the JSON when null.
        /// </summary>
        [JsonPropertyName("format"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Format { get; set; }
    }

    /// <summary>
    ///     <c>klovn.tts.codec</c>.
    /// </summary>
    private enum TtsCodecMode : byte
    {
        Vorbis,
        Opus,
        Transcode,
    }
}

/// <summary>
///     A synthesised line, as it goes to clients.
/// </summary>
public readonly record struct TtsClip(TtsCodec Codec, byte[] Data);
