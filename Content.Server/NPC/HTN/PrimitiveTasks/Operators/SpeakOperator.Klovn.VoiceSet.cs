// KS14: added in this fork
using Content.Server._KS14.NPC.Voices;
using Content.Shared.Dataset;
using Robust.Shared.Prototypes;

namespace Content.Server.NPC.HTN.PrimitiveTasks.Operators;

public sealed partial class SpeakOperator
{
    /// <summary>
    ///     The line set the speaker actually speaks from: its own voice set's replacement for
    ///         <paramref name="lineSet"/>, if it has one, otherwise <paramref name="lineSet"/> itself. See
    ///         <see cref="NpcVoiceSetComponent"/>.
    /// </summary>
    /// <summary>
    ///     Whether the speaker's voice set leaves this task's line set unsaid altogether. The task still succeeds:
    ///         saying nothing is not failing to speak.
    /// </summary>
    internal bool KsIsSilenced(NPCBlackboard blackboard)
    {
        return Speech is SpeakOperatorSpeech.LocalizedSetSpeakOperatorSpeech localizedSet &&
            _entMan.TryGetComponent<NpcVoiceSetComponent>(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), out var voiceSetComponent) &&
            _proto.TryIndex(voiceSetComponent.Set, out var voiceSet) &&
            voiceSet.Silenced.Contains(localizedSet.LineSet);
    }

    internal ProtoId<LocalizedDatasetPrototype> KsResolveLineSet(NPCBlackboard blackboard, ProtoId<LocalizedDatasetPrototype> lineSet)
    {
        var speakerUid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (_entMan.TryGetComponent<NpcVoiceSetComponent>(speakerUid, out var voiceSetComponent) &&
            _proto.TryIndex(voiceSetComponent.Set, out var voiceSet) &&
            voiceSet.Replacements.TryGetValue(lineSet, out var replacement))
            return replacement;

        return lineSet;
    }
}
