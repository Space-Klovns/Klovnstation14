using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.TTS;
using Content.Shared.Chat;
using Content.Shared.GameTicking;
using Robust.Shared.Collections;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._KS14.TTS;

/// <inheritdoc/>
public sealed class TtsSystem : SharedTtsSystem
{
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IRobustRandom _robustRandom = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;

    private readonly HttpClient _httpClient = new();
    private readonly Dictionary<string, byte[]> _cache = new();

    private string _ttsEndpoint = "";
    private bool _enabled = false;

    private readonly Dictionary<EntityUid, TimeSpan> _timeUntilCooldownFinished = [];
    private readonly List<ProtoId<TtsVoicePrototype>> _voiceIds = [];

    private static readonly TimeSpan BaselineCooldown = TimeSpan.FromSeconds(0.5f);
    private static readonly TimeSpan CooldownPerChar = TimeSpan.FromSeconds(0.01f);
    private const int MaxTextLength = 50;

    public override void Initialize()
    {
        SubscribeLocalEvent<EntitySpokeEvent>(OnSpoke);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);

        ReloadVoices();

        _configurationManager.OnValueChanged(KsCCVars.TtsEndpoint, (x) => _ttsEndpoint = x, invokeImmediately: true);
        _configurationManager.OnValueChanged(KsCCVars.TtsEnabled, (x) => _enabled = x, invokeImmediately: true);
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (!args.Modified.Contains(typeof(TtsVoicePrototype)))
            return;

        ReloadVoices();
    }

    private void ReloadVoices()
    {
        _voiceIds.Clear();

        foreach (var prototype in _prototypeManager.EnumeratePrototypes<TtsVoicePrototype>())
            _voiceIds.Add(prototype.ID);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var done = new ValueList<EntityUid>();
        foreach (var (uid, time) in _timeUntilCooldownFinished)
        {
            if (time < _gameTiming.CurTime)
                continue;

            done.Add(uid);
        }

        foreach (var uid in done)
            _timeUntilCooldownFinished.Remove(uid);
    }

    private void OnSpoke(EntitySpokeEvent args)
    {
        var component = EnsureComp<TtsVoiceComponent>(args.Source);
        component.Id ??= _robustRandom.Pick(_voiceIds); // lol

        TrySpeak(args.Source, component.Id.Value, args.Message);
    }

    private void OnCleanup(RoundRestartCleanupEvent args)
    {
        _timeUntilCooldownFinished.Clear();
    }

    public void TrySpeak(EntityUid speakerUid, ProtoId<TtsVoicePrototype> voiceProto, string text)
    {
        if (!_enabled ||
            _timeUntilCooldownFinished.ContainsKey(speakerUid))
            return;

        if (text.Length > MaxTextLength)
            text = text[0..^MaxTextLength];

        _timeUntilCooldownFinished[speakerUid] = _gameTiming.CurTime + BaselineCooldown + (CooldownPerChar * text.Length);
        _ = Speak(speakerUid, voiceProto, text);
    }

    public async Task Speak(EntityUid speakerUid, ProtoId<TtsVoicePrototype> voiceProto, string text)
    {
        if (string.IsNullOrWhiteSpace(text) ||
            !_prototypeManager.TryIndex(voiceProto, out var proto))
            return;

        var cacheId = BuildCacheId(proto, text);
        if (!_cache.TryGetValue(cacheId, out var bytes))
        {
            try
            {
                var request = new TtsRequestBody
                {
                    Voice = proto.Voice,
                    Input = text
                };

                var response = await _httpClient.PostAsJsonAsync(_ttsEndpoint, request);
                response.EnsureSuccessStatusCode();

                bytes = await response.Content.ReadAsByteArrayAsync();
                _cache[cacheId] = bytes;

            }
            catch (Exception e)
            {
                Log.Error($"TTS failed: {e}");
                return;
            }
        }

        RaiseNetworkEvent(new PlayTtsEvent(GetNetEntity(speakerUid), bytes), Filter.Pvs(speakerUid));
    }

    private static string BuildCacheId(TtsVoicePrototype proto, string text)
    {
        var raw =
            $"{proto.Voice}|{text}";

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }

    public sealed record TtsRequestBody
    {
        [JsonPropertyName("text")]
        public string Input { get; set; } = default!;

        [JsonPropertyName("voice")]
        public string Voice { get; set; } = default!;
    }
}
