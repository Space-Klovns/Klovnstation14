using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Cinematics;

/// <summary>
///     A sound that was already playing when a cinematic started, and keeps playing through it.
/// </summary>
/// <param name="Offset">How far into the sound playback is when the cinematic starts.</param>
/// <param name="Params">Used to play the rest of it to a client that never heard it start.</param>
[Serializable, NetSerializable]
public readonly record struct KsCinematicContinuedSound(ResolvedSoundSpecifier Sound, TimeSpan Offset, AudioParams Params);

/// <summary>
///     Sent by the server to start a cinematic. The client plays all of it locally, timed from when it receives this.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsPlayCinematicEvent : EntityEventArgs
{
    public ProtoId<KsCinematicPrototype> Cinematic;

    /// <summary>
    ///     How long each of the cinematic's scenes is shown, with any stretching already applied.
    /// </summary>
    public TimeSpan[] SceneDurations;

    public KsCinematicContinuedSound? ContinuedSound;

    public KsPlayCinematicEvent(
        ProtoId<KsCinematicPrototype> cinematic,
        TimeSpan[] sceneDurations,
        KsCinematicContinuedSound? continuedSound)
    {
        Cinematic = cinematic;
        SceneDurations = sceneDurations;
        ContinuedSound = continuedSound;
    }
}
