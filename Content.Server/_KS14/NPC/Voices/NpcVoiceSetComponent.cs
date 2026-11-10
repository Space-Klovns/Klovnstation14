using Robust.Shared.Prototypes;

namespace Content.Server._KS14.NPC.Voices;

/// <summary>
///     Makes this NPC speak its HTN's voicelines in its own voice: <c>SpeakOperator</c> swaps each line set it is asked
///         for through <see cref="Set"/>. See <see cref="NpcVoiceSetPrototype"/>.
/// </summary>
[RegisterComponent]
public sealed partial class NpcVoiceSetComponent : Component
{
    [DataField(required: true)]
    public ProtoId<NpcVoiceSetPrototype> Set;
}
