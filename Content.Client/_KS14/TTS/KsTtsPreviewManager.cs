using System.IO;
using System.Threading.Tasks;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.TTS;
using Robust.Client.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.TTS;

/// <summary>
///     Whether a voice's preview can be played, and if not, why. The character editor turns this into its preview
///         button's state and tooltip.
/// </summary>
public enum KsTtsPreviewAvailability : byte
{
    Available,

    /// <summary>
    ///     <c>klovn.tts.enabled</c> is off: no voice is heard in game, so there is nothing to preview.
    /// </summary>
    TtsDisabled,

    /// <summary>
    ///     No voice is picked: the character gets a random one each round.
    /// </summary>
    NoVoice,

    /// <summary>
    ///     The server is still baking previews, or hasn't started yet.
    /// </summary>
    Baking,

    /// <summary>
    ///     The TTS endpoint failed; the server retries later.
    /// </summary>
    Failed,

    /// <summary>
    ///     The server finished without one for this voice.
    /// </summary>
    Missing,

    /// <summary>
    ///     Arrived, and still being decoded off the game thread.
    /// </summary>
    Decoding,

    /// <summary>
    ///     Arrived, but couldn't be decoded.
    /// </summary>
    Broken,
}

/// <summary>
///     Holds the voice previews the server baked (see the server's <c>KsTtsPreviewManager</c>) and plays them for the
///         character editor.
/// </summary>
/// <remarks>
///     Receiving them costs the game thread nothing but the copy out of the network buffer. Opus previews are decoded
///         on the thread pool as they arrive, since Concentus is plain content code. Vorbis ones can only be decoded by
///         the engine, which has to do it on the game thread, so they wait until they are first played: one short clip,
///         when the player asks for it, rather than every voice at once while they are connecting.
/// </remarks>
public sealed partial class KsTtsPreviewManager
{
    [Dependency] private IClientNetManager _netManager = default!;
    [Dependency] private IAudioManager _audioManager = default!;
    [Dependency] private IEntitySystemManager _entitySystemManager = default!;
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private ILogManager _logManager = default!;

    private readonly Dictionary<ProtoId<TtsVoicePrototype>, PreviewEntry> _entries = new();

    private ISawmill _sawmill = default!;

    /// <summary>
    ///     Bumped on disconnect, so a decode that finishes afterwards knows its entry is gone.
    /// </summary>
    private int _generation;

    private EntityUid? _playingUid;
    private AudioStream? _playingStream;

    public KsTtsPreviewStatus Status { get; private set; } = KsTtsPreviewStatus.Unavailable;

    /// <summary>
    ///     Raised on the game thread whenever what <see cref="GetAvailability"/> would answer may have changed.
    ///         Process-lifetime: anything shorter-lived that subscribes must unsubscribe.
    /// </summary>
    public event Action? AvailabilityChanged;

    public void Initialize()
    {
        _sawmill = _logManager.GetSawmill("ks.tts.preview");
        _netManager.RegisterNetMessage<KsTtsPreviewMessage>(OnPreviewMessage);
        _netManager.Disconnect += OnDisconnect;
        _configurationManager.OnValueChanged(KsCCVars.TtsEnabled, OnTtsEnabledChanged);
    }

    public void Shutdown()
    {
        _netManager.Disconnect -= OnDisconnect;
        _configurationManager.UnsubValueChanged(KsCCVars.TtsEnabled, OnTtsEnabledChanged);
        Clear();
    }

    private void OnTtsEnabledChanged(bool enabled)
    {
        AvailabilityChanged?.Invoke();
    }

    public KsTtsPreviewAvailability GetAvailability(ProtoId<TtsVoicePrototype>? voice)
    {
        if (!_configurationManager.GetCVar(KsCCVars.TtsEnabled))
            return KsTtsPreviewAvailability.TtsDisabled;

        if (voice is not { } id)
            return KsTtsPreviewAvailability.NoVoice;

        if (_entries.TryGetValue(id, out var entry))
        {
            if (entry.Decoding)
                return KsTtsPreviewAvailability.Decoding;

            return entry.Broken ? KsTtsPreviewAvailability.Broken : KsTtsPreviewAvailability.Available;
        }

        return Status switch
        {
            // TTS is on, so the server is about to start.
            KsTtsPreviewStatus.Unavailable or KsTtsPreviewStatus.Baking => KsTtsPreviewAvailability.Baking,
            KsTtsPreviewStatus.Failed => KsTtsPreviewAvailability.Failed,
            _ => KsTtsPreviewAvailability.Missing,
        };
    }

    /// <summary>
    ///     Plays <paramref name="voice"/>'s preview to the local player, cutting off any preview already playing.
    /// </summary>
    public bool TryPlay(ProtoId<TtsVoicePrototype> voice)
    {
        if (GetAvailability(voice) != KsTtsPreviewAvailability.Available ||
            !_entries.TryGetValue(voice, out var entry) ||
            !_entitySystemManager.TryGetEntitySystem<AudioSystem>(out var audioSystem))
            return false;

        if (!TryResolveStream(voice, entry, out var stream))
            return false;

        StopPlaying(audioSystem);
        _playingUid = audioSystem.PlayGlobal(stream, null, AudioParams.Default)?.Entity;
        _playingStream = stream;
        return _playingUid != null;
    }

    /// <summary>
    ///     The entry's audio stream, made the first time it is played. Both kinds of load hand the samples to OpenAL, so
    ///         this is game-thread work; for Opus it is only the upload, the decoding having been done already.
    /// </summary>
    private bool TryResolveStream(ProtoId<TtsVoicePrototype> voice, PreviewEntry entry, out AudioStream stream)
    {
        if (entry.Stream != null)
        {
            stream = entry.Stream;
            return true;
        }

        try
        {
            entry.Stream = entry.Codec switch
            {
                TtsCodec.Opus => _audioManager.LoadAudioRaw(entry.Samples, 1, KsTtsOpus.SampleRate),
                _ => _audioManager.LoadAudioOggVorbis(new MemoryStream(entry.Data)),
            };
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or ArgumentException)
        {
            _sawmill.Warning($"Couldn't load the {voice} voice preview: {exception.Message}");
            entry.Broken = true;
            AvailabilityChanged?.Invoke();
            stream = default!;
            return false;
        }

        // Nothing needs the source bytes once the stream holds the audio.
        entry.Samples = [];
        entry.Data = [];
        stream = entry.Stream;
        return true;
    }

    private void OnPreviewMessage(KsTtsPreviewMessage message)
    {
        Status = message.Status;

        if (message.Replace)
            Clear();

        foreach (var preview in message.Previews)
        {
            // Voices this client doesn't know (a server running newer prototypes) can't be picked anyway.
            if (!_prototypeManager.HasIndex(preview.Voice))
                continue;

            if (_entries.Remove(preview.Voice, out var old))
                DisposeStream(old, StillPlayingStream());

            var entry = new PreviewEntry(preview.Codec, preview.Data);
            _entries[preview.Voice] = entry;

            if (preview.Codec == TtsCodec.Opus)
                Decode(preview.Voice, entry);
        }

        AvailabilityChanged?.Invoke();
    }

    /// <summary>
    ///     Decodes an Opus preview on the thread pool. The continuation comes back to the game thread (the engine's
    ///         synchronisation context), so it can touch the entry and raise events.
    /// </summary>
    private async void Decode(ProtoId<TtsVoicePrototype> voice, PreviewEntry entry)
    {
        var generation = _generation;
        var data = entry.Data;
        entry.Decoding = true;

        var samples = await Task.Run(() => KsTtsOpus.TryDecode(data, out var decoded) ? decoded : null);

        // Disconnected, or replaced by a newer preview, while decoding.
        if (generation != _generation || !_entries.TryGetValue(voice, out var current) || current != entry)
            return;

        entry.Decoding = false;
        if (samples == null)
        {
            _sawmill.Warning($"Couldn't decode the {voice} voice preview.");
            entry.Broken = true;
        }
        else
        {
            entry.Samples = samples;
            entry.Data = [];
        }

        AvailabilityChanged?.Invoke();
    }

    private void OnDisconnect(object? sender, NetDisconnectedArgs args)
    {
        Clear();
        Status = KsTtsPreviewStatus.Unavailable;
        AvailabilityChanged?.Invoke();
    }

    private void Clear()
    {
        _generation++;

        var playingStream = StillPlayingStream();
        if (_entitySystemManager.TryGetEntitySystem<AudioSystem>(out var audioSystem))
            StopPlaying(audioSystem);

        foreach (var entry in _entries.Values)
            DisposeStream(entry, playingStream);

        _entries.Clear();
    }

    private void StopPlaying(AudioSystem audioSystem)
    {
        if (_playingUid is { } playingUid)
            audioSystem.Stop(playingUid);

        _playingUid = null;
        _playingStream = null;
    }

    /// <summary>
    ///     The stream of the preview playing now, if its audio entity is still around; null once it has finished.
    /// </summary>
    private AudioStream? StillPlayingStream()
    {
        return _playingUid is { } playingUid && _entityManager.EntityExists(playingUid) ? _playingStream : null;
    }

    private static void DisposeStream(PreviewEntry entry, AudioStream? playingStream)
    {
        // Stopping only queues the audio entity's deletion, so its source still holds the buffer until the end of
        //      the tick, and OpenAL refuses to delete a buffer in use. That one stream is left to leak instead: a few
        //      tens of kilobytes, and only if a reload or disconnect lands mid-preview.
        if (entry.Stream != null && entry.Stream != playingStream)
            entry.Stream.Dispose();

        entry.Stream = null;
    }

    private sealed class PreviewEntry(TtsCodec codec, byte[] data)
    {
        public readonly TtsCodec Codec = codec;

        /// <summary>
        ///     The clip as the server sent it, until it is decoded.
        /// </summary>
        public byte[] Data = data;

        /// <summary>
        ///     Decoded Opus, until it is loaded into <see cref="Stream"/>.
        /// </summary>
        public short[] Samples = [];

        public bool Decoding;
        public bool Broken;
        public AudioStream? Stream;
    }
}
