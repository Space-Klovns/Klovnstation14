using Content.Server.Chat.Systems;
using Content.Shared.Chat;
using Content.Shared.Dataset;
using Content.Shared.Random.Helpers;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Voices;

/// <summary>
///     Gives NPCs with an <see cref="NpcVoiceSetComponent"/> a recorded speaker and a pitch of their own when they
///         spawn, and plays the speaker's clip for a line the NPC speaks. Also where an NPC's voice set is applied to what
///         it says: which line sets it leaves unsaid, which it swaps for its own, and which callouts it makes at all.
/// </summary>
public sealed partial class NpcVoiceSystem : EntitySystem
{
    [Dependency] private ChatSystem _chatSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    [Dependency] private EntityQuery<NpcVoiceSetComponent> _voiceSetQuery = default!;

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

    /// <summary>
    ///     Whether <paramref name="uid"/>'s voice set leaves <paramref name="lineSet"/> unsaid altogether.
    /// </summary>
    public bool IsSilenced(EntityUid uid, ProtoId<LocalizedDatasetPrototype> lineSet)
    {
        return TryGetVoiceSet(uid, out _, out var voiceSet) && voiceSet.Silenced.Contains(lineSet);
    }

    /// <summary>
    ///     The line set <paramref name="uid"/> actually speaks from when asked for <paramref name="lineSet"/>: its voice
    ///         set's replacement, if it has one, otherwise <paramref name="lineSet"/> itself.
    /// </summary>
    public ProtoId<LocalizedDatasetPrototype> ResolveLineSet(EntityUid uid, ProtoId<LocalizedDatasetPrototype> lineSet)
    {
        return TryGetVoiceSet(uid, out _, out var voiceSet) && voiceSet.Replacements.TryGetValue(lineSet, out var replacement)
            ? replacement
            : lineSet;
    }

    /// <summary>
    ///     Whether <paramref name="uid"/>'s voice set makes <paramref name="callout"/> at all.
    /// </summary>
    public bool MakesCallout(EntityUid uid, NpcCallout callout)
    {
        return TryGetVoiceSet(uid, out _, out var voiceSet) && voiceSet.Callouts.ContainsKey(callout);
    }

    /// <summary>
    ///     A line for <paramref name="callout"/> in <paramref name="uid"/>'s voice, if its voice set makes that callout and
    ///         it has not made it within <paramref name="cooldown"/>; picking one holds the callout back for that long.
    /// </summary>
    public bool TryPickCalloutLine(EntityUid uid, NpcCallout callout, TimeSpan cooldown, out LocId line)
    {
        line = default;

        if (!TryGetVoiceSet(uid, out var voiceSetComponent, out var voiceSet) ||
            !voiceSet.Callouts.TryGetValue(callout, out var lineSetId) ||
            !_prototypeManager.TryIndex(lineSetId, out var lineSet) ||
            lineSet.Values.Count == 0)
            return false;

        var now = _gameTiming.CurTime;
        ref var heldUntil = ref voiceSetComponent.CalloutHeldUntil[(int) callout];

        if (now < heldUntil)
            return false;

        heldUntil = now + cooldown;
        line = _random.Pick(lineSet.Values);
        return true;
    }

    /// <summary>
    ///     Has <paramref name="uid"/> say a line for <paramref name="callout"/>, aloud if it has a recorded voice: see
    ///         <see cref="TryPickCalloutLine"/>.
    /// </summary>
    /// <returns>Whether it said anything.</returns>
    public bool TrySpeakCallout(EntityUid uid, NpcCallout callout, TimeSpan cooldown = default)
    {
        if (!TryPickCalloutLine(uid, callout, cooldown, out var line))
            return false;

        SpeakLine(uid, line);
        return true;
    }

    /// <summary>
    ///     Has <paramref name="uid"/> say <paramref name="line"/>, and plays its speaker's clip of it if it has one.
    /// </summary>
    public void SpeakLine(EntityUid uid, LocId line)
    {
        _chatSystem.TrySendInGameICMessage(uid, Loc.GetString(line), InGameICChatType.Speak, hideChat: false, hideLog: false);
        TryPlayLine(uid, line);
    }

    private bool TryGetVoiceSet(EntityUid uid, out NpcVoiceSetComponent voiceSetComponent, out NpcVoiceSetPrototype voiceSet)
    {
        voiceSet = default!;
        return _voiceSetQuery.TryComp(uid, out voiceSetComponent!) && _prototypeManager.TryIndex(voiceSetComponent.Set, out voiceSet!);
    }
}
