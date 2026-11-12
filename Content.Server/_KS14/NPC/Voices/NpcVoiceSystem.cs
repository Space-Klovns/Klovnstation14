using Content.Shared.Random.Helpers;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._KS14.NPC.Voices;

/// <summary>
///     Gives NPCs with an <see cref="NpcVoiceSetComponent"/> a recorded speaker and a pitch of their own when they
///         spawn, and plays the speaker's clip for a line the NPC speaks.
/// </summary>
public sealed partial class NpcVoiceSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NpcVoiceSetComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<NpcVoiceSetComponent> voiceEntity, ref MapInitEvent args)
    {
        var voice = voiceEntity.Comp;

        if (voice.Speaker is null && voice.Speakers.Count > 0)
            voice.Speaker = _random.Pick(voice.Speakers);

        if (voice.PitchSpread > 0f)
            voice.Pitch = 1f + _random.NextFloat(-voice.PitchSpread, voice.PitchSpread);
    }

    /// <summary>
    ///     Plays <paramref name="speakerUid"/>'s speaker's clip for the locale string <paramref name="locId"/>, at the
    ///         speaker's gain and the NPC's own pitch.
    /// </summary>
    /// <returns>Whether a clip was played: false when the NPC has no speaker or the speaker has no clip for the string.</returns>
    public bool TryPlayLine(Entity<NpcVoiceSetComponent?> speakerEntity, LocId locId)
    {
        if (!Resolve(speakerEntity, ref speakerEntity.Comp, logMissing: false) ||
            speakerEntity.Comp.Speaker is not { } speakerId ||
            !_prototypeManager.TryIndex(speakerId, out var recording) ||
            !recording.Lines.TryGetValue(locId, out var clip))
            return false;

        _audio.PlayPvs(clip, speakerEntity.Owner, AudioParams.Default.WithVolume(recording.Volume).WithPitchScale(speakerEntity.Comp.Pitch));
        return true;
    }
}
