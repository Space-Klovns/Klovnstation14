using Robust.Shared.Prototypes;
using Content.Shared._KS14.TTS;

namespace Content.Server._KS14.TTS;

[RegisterComponent]
public sealed partial class TtsVoiceComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField]
    public ProtoId<TtsVoicePrototype>? Id = null;
}
