using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Chat;
using Content.Shared._KS14.TTS;
using Robust.Client.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;

namespace Content.Client._KS14.TTS;

/// <inheritdoc/>
public sealed partial class TtsSystem : SharedTtsSystem
{
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IAudioManager _audioManager = default!;
    [Dependency] private AudioSystem _audioSystem = default!;

    private bool _ttsEnabled = false;
    private bool _slurFilterEnabled = false;
    private ConcurrentQueue<(AudioStream Stream, EntityUid Uid)> _queued = [];

    /// <summary>
    ///     Clips playing now, each with the audio entity playing it. A clip's stream owns an OpenAL buffer, which is
    ///         freed once its entity is gone rather than left for the rest of the session.
    /// </summary>
    private readonly List<(AudioStream Stream, EntityUid AudioUid)> _playing = [];

    /// <summary>
    ///     Clips played since startup, of either codec. For tests.
    /// </summary>
    public int PlayedClipCount { get; private set; }

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_configurationManager, KsCCVars.TtsEnabled, value => _ttsEnabled = value, invokeImmediately: true);
        Subs.CVar(_configurationManager, KsCCVars.SlurFilterEnabled, value => _slurFilterEnabled = value, invokeImmediately: true);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        foreach (var (stream, _) in _playing)
            stream.Dispose();

        _playing.Clear();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Freed a tick after the entity goes, since a stopped entity's deletion is queued and its source holds the
        //      buffer until then.
        for (var i = _playing.Count - 1; i >= 0; i--)
        {
            if (Exists(_playing[i].AudioUid))
                continue;

            _playing[i].Stream.Dispose();
            _playing.RemoveAt(i);
        }

        if (_queued.IsEmpty)
            return;

        while (_queued.TryDequeue(out var datum))
        {
            if (TerminatingOrDeleted(datum.Uid))
            {
                datum.Stream.Dispose();
                continue;
            }

            var audioEntity = _audioSystem.PlayEntity(datum.Stream, datum.Uid, null, audioParams: AudioParams.Default);
            if (audioEntity == null)
            {
                datum.Stream.Dispose();
                continue;
            }

            _playing.Add((datum.Stream, audioEntity.Value.Entity));
            PlayedClipCount++;

            var ev = new EmoteSoundPlayedEvent((audioEntity.Value.Entity, audioEntity.Value.Component), null);
            RaiseLocalEvent(datum.Uid, ref ev);
        }
    }

    [SubscribeNetworkEvent]
    private async void OnPlayTts(PlayTtsEvent args)
    {
        if (!_ttsEnabled ||
            !TryGetEntity(args.Source, out var uid))
            return;

        switch (args.FilteredCategory)
        {
            case TtsFilteredCategory.Filtered:
                if (!_slurFilterEnabled)
                    return;

                break;
            case TtsFilteredCategory.WaitForFiltered:
                if (_slurFilterEnabled)
                    return;

                break;
        }

        AudioStream stream;
        switch (args.Codec)
        {
            case TtsCodec.Opus:
                // Decoded on the thread pool; the engine only has to take the samples, back on the game thread.
                var data = args.Data;
                var samples = await Task.Run(() => KsTtsOpus.TryDecode(data, out var decoded) ? decoded : null);
                if (samples == null)
                {
                    Log.Warning($"Couldn't decode an Opus TTS clip from {ToPrettyString(uid)}.");
                    return;
                }

                stream = _audioManager.LoadAudioRaw(samples, 1, KsTtsOpus.SampleRate);
                break;
            default:
                // An exception here would escape an async void handler, so a bad clip is caught rather than trusted.
                try
                {
                    stream = _audioManager.LoadAudioOggVorbis(new MemoryStream(args.Data));
                }
                catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or ArgumentException)
                {
                    Log.Warning($"Couldn't decode a Vorbis TTS clip from {ToPrettyString(uid)}: {exception.Message}");
                    return;
                }

                break;
        }

        _queued.Enqueue((stream, uid.Value));
    }
}
