using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.NPC.Voices;

/// <summary>
///     One recorded speaker: for the locale strings an NPC speaks, the clip of this speaker saying that exact string. An
///         NPC that speaks one of the strings plays its clip along with the chat message. A mob opts in through
///         <see cref="NpcVoiceSetComponent.Speakers"/>; the clips are played at <see cref="Volume"/> and the mob's own
///         pitch.
/// </summary>
/// <remarks>
///     The clips are private, so a public prototype declares the speaker and a private partial prototype of the same id
///         adds <see cref="Lines"/> to it. Without the partial the speaker is mute and everything else works.
/// </remarks>
/// <example>
///     <code>
///     - type: npcVoiceRecording
///       id: KsRedactedSpeakerA
///       volume: 0
///
///     - type: !PartialOnly npcVoiceRecording
///       id: KsRedactedSpeakerA
///       lines:
///         ks-operative-redacted-line-execution-1: /Audio/_KsModule/Effects/Voice/Redacted/SpeakerA/fight_19.ogg
///     </code>
/// </example>
[Prototype]
public sealed partial class NpcVoiceRecordingPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    ///     Gain of every clip of this speaker in decibels, for evening out recordings made at different levels.
    /// </summary>
    [DataField]
    public float Volume;

    /// <summary>
    ///     The clip of this speaker for each locale string, by the string's id. One string has at most one clip.
    /// </summary>
    [DataField]
    public Dictionary<string, SoundSpecifier> Lines = new();
}
