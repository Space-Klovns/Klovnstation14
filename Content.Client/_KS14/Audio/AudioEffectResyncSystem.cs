using Content.Shared._KS14.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Sources;
using Robust.Shared.Audio.Systems;

namespace Content.Client._KS14.Audio;

/// <summary>
///     Applies server-set auxiliaries to the audio sources of entities marked with <see cref="AudioEffectAppliedComponent"/>.
///
///     The engine only pushes <see cref="AudioComponent.Auxiliary"/> into the audio source from its state handler,
///         which returns early while the component is not yet initialised - and for a newly-received audio entity,
///         that is always the case. The source is then created in <c>ComponentStartup</c> without an auxiliary, and
///         unless the audio entity happens to be dirtied again, it never gets one.
///
///     The auxiliary's own effect has the same problem: if the auxiliary's state is applied before the effect
///         entity has its components, the slot is bound to no effect at all.
/// </summary>
// TODO: remove this when the engine applies the auxiliary on audio startup
public sealed partial class AudioEffectResyncSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private EntityQuery<AudioComponent> _audioQuery = default!;
    [Dependency] private EntityQuery<AudioAuxiliaryComponent> _auxiliaryQuery = default!;
    [Dependency] private EntityQuery<AudioEffectComponent> _effectQuery = default!;

    private readonly HashSet<EntityUid> _pendingResyncs = new();
    private readonly List<EntityUid> _drainedResyncs = new();

    // Deferred rather than applied here, as there's no guarantee the audio component
    //      has started - and so has an audio source to apply anything to - before this one.
    [SubscribeLocalEvent]
    private void OnStartup(Entity<AudioEffectAppliedComponent> entity, ref ComponentStartup args)
    {
        _pendingResyncs.Add(entity.Owner);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_pendingResyncs.Count == 0)
            return;

        _drainedResyncs.Clear();
        _drainedResyncs.AddRange(_pendingResyncs);
        _pendingResyncs.Clear();

        foreach (var audioUid in _drainedResyncs)
        {
            if (!_audioQuery.TryComp(audioUid, out var audioComponent) ||
                audioComponent.Auxiliary is not { } auxiliaryUid ||
                !_auxiliaryQuery.TryComp(auxiliaryUid, out var auxiliaryComponent))
                continue;

            if (_effectQuery.HasComp(auxiliaryComponent.Effect))
                _audioSystem.SetEffect(auxiliaryUid, auxiliaryComponent, auxiliaryComponent.Effect);

            // Straight to the source rather than through SetAuxiliary, which would dirty the audio entity
            //      and have its state, playback position included, reset to the server's next tick.
            ((IAudioSource)audioComponent).SetAuxiliary(auxiliaryComponent.Auxiliary);
        }
    }
}
