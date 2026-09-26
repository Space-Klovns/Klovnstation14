using Content.Client._KS14.StatusEffect;
using Content.Shared.StatusEffectNew;
using Robust.Client.Player;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Sources;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._KS14.AudioPitchStatusEffect;

/// <summary>
///     While the local player has an active <see cref="KsAudioPitchStatusEffectComponent"/> effect, tags every sound on
///         the client with <see cref="KsPitchWarpedAudioComponent"/> and warps its pitch each frame. Once no such effect
///         is active, the tags come off and every sound goes back to its normal pitch.
/// </summary>
/// <remarks>
///     The engine only writes a sound's pitch when it starts and when a network state for it arrives, so writing it
///         every frame holds. Randomised pitch variation is already baked into <c>Params.Pitch</c> by then, which makes
///         that the sound's true normal pitch.
/// </remarks>
public sealed partial class KsAudioPitchStatusEffectSystem : EntitySystem
{
    /// <summary>
    ///     OpenAL rejects a pitch of zero or below.
    /// </summary>
    private const float MinimumPitch = 0.05f;

    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private StatusEffectsSystem _statusEffectsSystem = default!;

    [Dependency] private EntityQuery<AudioComponent> _audioQuery = default!;
    [Dependency] private EntityQuery<KsPitchWarpedAudioComponent> _warpedAudioQuery = default!;
    [Dependency] private EntityQuery<KsAudioPitchStatusEffectComponent> _pitchEffectQuery = default!;

    [SubscribeLocalEvent]
    private void OnWarpedAudioStartup(Entity<KsPitchWarpedAudioComponent> entity, ref ComponentStartup args)
    {
        entity.Comp.Phase = _random.NextFloat(0f, 1000f);
    }

    // The one place pitch is restored, so it happens however the tag comes off.
    [SubscribeLocalEvent]
    private void OnWarpedAudioShutdown(Entity<KsPitchWarpedAudioComponent> entity, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(entity.Owner) ||
            !_audioQuery.TryGetComponent(entity.Owner, out var audioComponent) ||
            audioComponent.LifeStage > ComponentLifeStage.Running)
            return;

        SetSourcePitch(audioComponent, audioComponent.Params.Pitch);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (!TryGetStrongestEffect(out var pitchEffectComponent, out var strength))
        {
            UntagAllAudio();
            return;
        }

        TagAllAudio();

        var realTime = _gameTiming.RealTime;
        var lurchProbability = 1f - MathF.Exp(-pitchEffectComponent.LurchChance * frameTime);
        var logMax = MathF.Log(pitchEffectComponent.MaxMultiplier);
        var logMin = MathF.Log(pitchEffectComponent.MinMultiplier);

        var warpedEnumerator = AllEntityQuery<KsPitchWarpedAudioComponent, AudioComponent>();
        while (warpedEnumerator.MoveNext(out _, out var warpedAudioComponent, out var audioComponent))
        {
            // Already shut down - and its pitch already restored - by a deferred removal that hasn't been culled yet.
            //      Warping it now would leave it warped for good once the cull takes it away.
            if (warpedAudioComponent.LifeStage > ComponentLifeStage.Running)
                continue;

            if (warpedAudioComponent.LurchEndTime <= realTime && _random.Prob(lurchProbability))
            {
                warpedAudioComponent.LurchValue = _random.NextFloat(-1f, 1f);
                warpedAudioComponent.LurchEndTime = realTime + _random.Next(pitchEffectComponent.MinLurchDuration, pitchEffectComponent.MaxLurchDuration);
            }

            var distortion = warpedAudioComponent.LurchEndTime > realTime
                ? warpedAudioComponent.LurchValue
                : SmoothNoise((float)realTime.TotalSeconds * pitchEffectComponent.WanderSpeed + warpedAudioComponent.Phase);

            // In log space, so an octave down and an octave up are the same distance from normal.
            var logMultiplier = distortion >= 0f ? distortion * logMax : -distortion * logMin;
            var multiplier = MathF.Exp(logMultiplier * strength);

            SetSourcePitch(audioComponent, MathF.Max(MinimumPitch, audioComponent.Params.Pitch * multiplier));
        }
    }

    public override void Shutdown()
    {
        base.Shutdown();
        UntagAllAudio();
    }

    /// <summary>
    ///     Sets the pitch the sound is playing at right now, leaving <c>Params.Pitch</c> - its normal pitch - alone.
    /// </summary>
    /// <remarks>
    ///     Through <see cref="IAudioSource"/> because the engine exposes no pitch setter, and
    ///         <see cref="AudioComponent.Pitch"/> is restricted to its own audio system. That restriction is only the
    ///         component's blanket <c>[Access]</c>: <see cref="AudioComponent.Volume"/> is the same kind of pass-through
    ///         to the OpenAL source and is opened up to everyone, so this is no more than what content already does
    ///         with volume. If the engine ever gains a SetPitch, use it here instead.
    /// </remarks>
    private static void SetSourcePitch(IAudioSource audioSource, float pitch)
    {
        audioSource.Pitch = pitch;
    }

    /// <summary>
    ///     Picks the active pitch effect on the local player with the most of its duration left.
    /// </summary>
    /// <param name="strength">
    ///     How much of its distortion to apply, from 0 to 1: the time left ratio for effects that fade, 1 otherwise.
    /// </param>
    private bool TryGetStrongestEffect(out KsAudioPitchStatusEffectComponent pitchEffectComponent, out float strength)
    {
        pitchEffectComponent = default!;
        strength = 0f;

        if (_playerManager.LocalEntity is not { } localUid)
            return false;

        var found = false;
        var curTime = _gameTiming.CurTime;

        foreach (var effect in _statusEffectsSystem.EnumerateStatusEffects(localUid, _pitchEffectQuery))
        {
            // Delayed and not started yet.
            if (effect.Comp1.StartEffectTime > curTime)
                continue;

            var effectStrength = effect.Comp2.FadeWithTimeLeft
                ? KsStatusEffectTimeLeft.GetRatio(effect.Comp1, curTime)
                : 1f;

            if (found && effectStrength <= strength)
                continue;

            found = true;
            pitchEffectComponent = effect.Comp2;
            strength = effectStrength;
        }

        return found;
    }

    /// <summary>
    ///     Tags every sound not tagged yet, which picks up sounds that started since the last frame.
    /// </summary>
    private void TagAllAudio()
    {
        var audioEnumerator = AllEntityQuery<AudioComponent>();
        while (audioEnumerator.MoveNext(out var audioUid, out _))
        {
            if (!_warpedAudioQuery.HasComponent(audioUid))
                AddComp<KsPitchWarpedAudioComponent>(audioUid);
        }
    }

    private void UntagAllAudio()
    {
        // Deferred, so that removing the component doesn't disturb the enumeration of it. Its shutdown - and with it
        //      the pitch restore - still runs right away; only taking it out of storage waits for the end of the tick.
        var warpedEnumerator = AllEntityQuery<KsPitchWarpedAudioComponent>();
        while (warpedEnumerator.MoveNext(out var audioUid, out var warpedAudioComponent))
            RemCompDeferred(audioUid, warpedAudioComponent);
    }

    /// <summary>
    ///     Smooth 1D value noise, from -1 to 1.
    /// </summary>
    private static float SmoothNoise(float position)
    {
        var cell = MathF.Floor(position);
        var fraction = position - cell;
        var smoothed = fraction * fraction * (3f - 2f * fraction);

        return Lerp(Hash(cell), Hash(cell + 1f), smoothed);
    }

    /// <summary>
    ///     A fixed pseudo-random value from -1 to 1 for each whole number.
    /// </summary>
    private static float Hash(float cell)
    {
        var value = MathF.Sin(cell * 127.1f) * 43758.547f;
        return (value - MathF.Floor(value)) * 2f - 1f;
    }

    private static float Lerp(float from, float to, float amount)
    {
        return from + (to - from) * amount;
    }
}
