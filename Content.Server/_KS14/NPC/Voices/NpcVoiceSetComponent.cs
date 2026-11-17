using Robust.Shared.Prototypes;

namespace Content.Server._KS14.NPC.Voices;

/// <summary>
///     Makes this NPC speak its HTN's voicelines in its own voice: <c>SpeakOperator</c> swaps each line set it is asked
///         for through <see cref="Set"/>. See <see cref="NpcVoiceSetPrototype"/>. It can also give the NPC a recorded
///         voice (<see cref="Speakers"/>) and a pitch of its own (<see cref="PitchSpread"/>), both rolled once when it
///         spawns and kept for its life.
/// </summary>
[RegisterComponent]
public sealed partial class NpcVoiceSetComponent : Component
{
    [DataField(required: true)]
    public ProtoId<NpcVoiceSetPrototype> Set;

    /// <summary>
    ///     The recorded speakers this NPC may be given. One is picked at random when it spawns, unless
    ///         <see cref="Speaker"/> is already set. Empty for an NPC whose lines are text only.
    /// </summary>
    [DataField]
    public List<ProtoId<NpcVoiceRecordingPrototype>> Speakers = new();

    /// <summary>
    ///     The speaker whose clips play along with this NPC's lines; null for none. The same speaker for the whole of
    ///         its life, never mixed per line.
    /// </summary>
    [DataField]
    public ProtoId<NpcVoiceRecordingPrototype>? Speaker;

    /// <summary>
    ///     How far this NPC's pitch may sit from normal, as a fraction: 0.05 rolls a pitch of 0.95 to 1.05 at spawn.
    ///         0 leaves every NPC at normal pitch.
    /// </summary>
    [DataField]
    public float PitchSpread;

    /// <summary>
    ///     This NPC's own pitch scale for every clip, rolled once from <see cref="PitchSpread"/> at spawn.
    /// </summary>
    [DataField]
    public float Pitch = 1f;

    /// <summary>
    ///     Until when each callout is held back for this NPC, by <see cref="NpcCallout"/>: it says one no more often than
    ///         the caller asks.
    /// </summary>
    [ViewVariables]
    public readonly TimeSpan[] CalloutHeldUntil = new TimeSpan[Enum.GetValues<NpcCallout>().Length];
}
