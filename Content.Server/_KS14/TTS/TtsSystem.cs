using System.Net.Http;
using System.Net.Http.Json;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.TTS;
using Content.Shared.Chat;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.TTS;

/// <inheritdoc/>
public sealed class TtsSystem : SharedTtsSystem
{
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    private readonly HttpClient _httpClient = new();
    private readonly Dictionary<string, byte[]> _cache = new();

    private string _ttsEndpoint = "";
    private bool _enabled = false;

    private const int MaxTextLength = 300;

    public override void Initialize()
    {
        SubscribeLocalEvent<EntitySpokeEvent>(OnSpoke);

        _configurationManager.OnValueChanged(KsCCVars.TtsEndpoint, (x) => _ttsEndpoint = x, invokeImmediately: true);
        _configurationManager.OnValueChanged(KsCCVars.TtsEnabled, (x) => _enabled = x, invokeImmediately: true);
    }

    private void OnSpoke(EntitySpokeEvent args)
    {
        TrySpeak(args.Source, "Default", args.Message);
    }

    public void TrySpeak(EntityUid speaker, ProtoId<TtsVoicePrototype> voiceProto, string text)
    {
        if (!_enabled)
            return;

        _ = Speak(speaker, voiceProto, text);
    }

    public async Task Speak(EntityUid speaker, ProtoId<TtsVoicePrototype> voiceProto, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (!_prototypeManager.TryIndex(voiceProto, out var proto))
            return;

        if (text.Length > 15)
            text = text[0..^MaxTextLength];

        var cacheId = BuildCacheId(proto, text);
        if (!_cache.TryGetValue(cacheId, out var bytes))
        {
            try
            {
                var request = new TtsRequestBody
                {
                    Voice = proto.Voice,
                    Text = text,
                    Speed = proto.Speed,
                    Pitch = proto.Pitch
                };

                var response = await _httpClient.PostAsJsonAsync(_ttsEndpoint, request);
                response.EnsureSuccessStatusCode();

                bytes = await response.Content.ReadAsByteArrayAsync();
                _cache[cacheId] = bytes;

                Log.Info($"Generated TTS {cacheId}");
            }
            catch (Exception e)
            {
                Log.Error($"TTS failed: {e}");
                return;
            }
        }

        var ttsEntity = SpawnAttachedTo(null, new(speaker, Vector2.Zero));
        var component = EntityManager.ComponentFactory.GetComponent<TtsAudioComponent>();
        component.Bytes = bytes;

        AddComp(ttsEntity, component);
        Dirty(ttsEntity, component);

        Log.Debug($"Played TTS {cacheId}");
    }

    private static string BuildCacheId(TtsVoicePrototype proto, string text)
    {
        var raw =
            $"{proto.Voice}|{proto.Speed}|{proto.Pitch}|{text}";

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }

    public sealed class TtsRequestBody
    {
        public string Voice = default!;
        public string Text = default!;
        public float Speed;
        public float Pitch;
    }
}
