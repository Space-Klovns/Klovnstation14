// KS14: added in this fork
using Content.Server._KS14.NPC.Voices;
using Content.Shared.Dataset;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.Server.NPC.HTN.PrimitiveTasks.Operators;

public sealed partial class SpeakOperator
{
    [Dependency] private NpcVoiceSystem _npcVoiceSystem = default!;

    /// <summary>
    ///     Whether the speaker's voice set leaves this task's line set unsaid altogether. The task still succeeds:
    ///         saying nothing is not failing to speak. See <see cref="NpcVoiceSystem.IsSilenced"/>.
    /// </summary>
    internal bool KsIsSilenced(NPCBlackboard blackboard)
    {
        return Speech is SpeakOperatorSpeech.LocalizedSetSpeakOperatorSpeech localizedSet &&
            _npcVoiceSystem.IsSilenced(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), localizedSet.LineSet);
    }

    /// <summary>
    ///     The line set the speaker actually speaks from: see <see cref="NpcVoiceSystem.ResolveLineSet"/>.
    /// </summary>
    internal ProtoId<LocalizedDatasetPrototype> KsResolveLineSet(NPCBlackboard blackboard, ProtoId<LocalizedDatasetPrototype> lineSet)
    {
        return _npcVoiceSystem.ResolveLineSet(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), lineSet);
    }

    /// <summary>
    ///     A line for a callout, if the speaker's voice makes it: see <see cref="NpcVoiceSystem.TryPickCalloutLine"/>.
    /// </summary>
    internal bool KsTryPickCalloutLine(NPCBlackboard blackboard, SpeakOperatorSpeech.VoiceCalloutSpeakOperatorSpeech callout, out LocId line)
    {
        return _npcVoiceSystem.TryPickCalloutLine(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner),
            callout.Callout,
            TimeSpan.FromSeconds(callout.Cooldown),
            out line);
    }

    /// <summary>
    ///     Plays the clip of the speaker's recorded voice for <paramref name="locId"/>, if it has one. See
    ///         <see cref="NpcVoiceSystem"/>.
    /// </summary>
    internal void KsPlayRecording(EntityUid speakerUid, LocId locId)
    {
        _npcVoiceSystem.TryPlayLine(speakerUid, locId);
    }

    public abstract partial class SpeakOperatorSpeech
    {
        /// <summary>
        ///     A callout (<see cref="NpcCallout"/>), in the speaker's voice: only an NPC whose voice set makes it says
        ///         anything, and no more often than <see cref="Cooldown"/> seconds. The task succeeds either way.
        /// </summary>
        public sealed partial class VoiceCalloutSpeakOperatorSpeech : SpeakOperatorSpeech
        {
            [DataField(required: true)]
            public NpcCallout Callout;

            [DataField]
            public float Cooldown;
        }
    }
}
