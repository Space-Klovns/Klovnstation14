using System.IO;
using Content.Shared._KS14.CCVar;
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

    public override void Initialize()
    {
        base.Initialize();

        _configurationManager.OnValueChanged(KsCCVars.TtsEnabled, (x) => _enabled = x, invokeImmediately: true);

        SubscribeLocalEvent<TtsAudioComponent, ComponentStartup>(OnStartup);
    }

    private void OnStartup(Entity<TtsAudioComponent> entity, ref ComponentStartup args)
    {
        var stream = _audioManager.LoadAudioOggVorbis(new MemoryStream(entity.Comp.Bytes));
        _audioSystem.PlayEntity(stream, entity.Owner, null, audioParams: AudioParams.Default);
    }
}
