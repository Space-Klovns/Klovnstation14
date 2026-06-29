using System.Collections.Concurrent;
using System.IO;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Chat;
using Content.Shared._KS14.TTS;
using Content.Shared._KS14.WordFilter;
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
    [Dependency] private readonly WordFilterSystem _wordFilterSystem = default!;

    private bool _ttsEnabled = false;
    private bool _slurFilterEnabled = false;
    private ConcurrentQueue<(AudioStream Stream, EntityUid Uid)> _queued = [];

    public override void Initialize()
    {
        base.Initialize();

        _configurationManager.OnValueChanged(KsCCVars.TtsEnabled, (x) => _ttsEnabled = x, invokeImmediately: true);
        _configurationManager.OnValueChanged(KsCCVars.SlurFilterEnabled, (x) => _slurFilterEnabled = x, invokeImmediately: true);

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

        var stream = _audioManager.LoadAudioOggVorbis(new MemoryStream(args.Data));
        _queued.Enqueue((stream, uid.Value));
    }
}
