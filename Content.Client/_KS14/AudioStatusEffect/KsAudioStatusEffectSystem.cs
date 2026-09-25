using Content.Client._KS14.StatusEffect;
using Content.Shared.StatusEffectNew;
using Robust.Client.Player;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._KS14.AudioStatusEffect;

/// <summary>
///     Plays the sound of every active <see cref="KsAudioStatusEffectComponent"/> effect on the local player, and
///         stops it once that effect is no longer active on them.
/// </summary>
public sealed partial class KsAudioStatusEffectSystem : EntitySystem
{
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private StatusEffectsSystem _statusEffectsSystem = default!;

    [Dependency] private EntityQuery<AudioComponent> _audioQuery = default!;
    [Dependency] private EntityQuery<KsAudioStatusEffectComponent> _audioEffectQuery = default!;

    /// <summary>
    ///     The audio entity playing for each effect entity, stopped once that effect is no longer active. Null when
    ///         the sound could not be played, so it is not retried every frame.
    /// </summary>
    private readonly Dictionary<EntityUid, EntityUid?> _playingAudio = new();

    private readonly HashSet<EntityUid> _seenEffectUids = new();
    private readonly List<EntityUid> _staleEffectUids = new();

    public override void Shutdown()
    {
        base.Shutdown();

        foreach (var audioUid in _playingAudio.Values)
            _audioSystem.Stop(audioUid);

        _playingAudio.Clear();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        _seenEffectUids.Clear();

        if (_playerManager.LocalEntity is { } localUid)
        {
            var curTime = _gameTiming.CurTime;

            foreach (var effect in _statusEffectsSystem.EnumerateStatusEffects(localUid, _audioEffectQuery))
            {
                var statusEffectComponent = effect.Comp1;
                var audioEffectComponent = effect.Comp2;

                // Delayed and not started yet.
                if (statusEffectComponent.StartEffectTime > curTime)
                    continue;

                _seenEffectUids.Add(effect.Owner);

                if (!_playingAudio.TryGetValue(effect.Owner, out var audioUid))
                {
                    audioUid = _audioSystem.PlayGlobal(audioEffectComponent.Sound, Filter.Local(), recordReplay: false)?.Entity;
                    _playingAudio[effect.Owner] = audioUid;
                }

                if (!audioEffectComponent.FadeWithTimeLeft ||
                    !_audioQuery.TryGetComponent(audioUid, out var audioComponent))
                    continue;

                var fullGain = SharedAudioSystem.VolumeToGain(audioEffectComponent.Sound.Params.Volume);
                _audioSystem.SetGain(audioUid,
                    fullGain * KsStatusEffectTimeLeft.GetRatio(statusEffectComponent, curTime),
                    audioComponent);
            }
        }

        StopStaleAudio();
    }

    /// <summary>
    ///     Stops the audio of effects that were not seen this frame.
    /// </summary>
    private void StopStaleAudio()
    {
        _staleEffectUids.Clear();
        foreach (var effectUid in _playingAudio.Keys)
        {
            if (!_seenEffectUids.Contains(effectUid))
                _staleEffectUids.Add(effectUid);
        }

        foreach (var effectUid in _staleEffectUids)
        {
            _playingAudio.Remove(effectUid, out var audioUid);
            _audioSystem.Stop(audioUid);
        }
    }
}
