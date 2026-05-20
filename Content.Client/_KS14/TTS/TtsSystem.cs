using System.Collections.Concurrent;
using System.IO;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Chat;
using Content.Shared._KS14.TTS;
using Robust.Client.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;

namespace Content.Client._KS14.TTS;

/// <inheritdoc/>
public sealed class TtsSystem : SharedTtsSystem
{
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;
    [Dependency] private readonly IAudioManager _audioManager = default!;
    [Dependency] private readonly AudioSystem _audioSystem = default!;

    private bool _enabled = false;
    private ConcurrentQueue<(AudioStream Stream, EntityUid Uid)> _queued = [];

    public override void Initialize()
    {
        base.Initialize();

        _configurationManager.OnValueChanged(KsCCVars.TtsEnabled, (x) => _enabled = x, invokeImmediately: true);

        SubscribeNetworkEvent<PlayTtsEvent>(OnPlayTts);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_queued.IsEmpty)
            return;

        while (_queued.TryDequeue(out var datum))
        {
            if (TerminatingOrDeleted(datum.Uid))
                continue;

            var audioEntity = _audioSystem.PlayEntity(datum.Stream, datum.Uid, null, audioParams: AudioParams.Default);

            var ev = new EmoteSoundPlayedEvent((audioEntity!.Value.Entity, audioEntity.Value.Component), null);
            RaiseLocalEvent(datum.Uid, ref ev);
        }
    }

    private async void OnPlayTts(PlayTtsEvent args)
    {
        if (!_enabled ||
            !TryGetEntity(args.Source, out var uid))
            return;

        var stream = _audioManager.LoadAudioOggVorbis(new MemoryStream(args.Data));
        _queued.Enqueue((stream, uid.Value));
    }
}
