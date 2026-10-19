using Robust.Shared.Prototypes;
using Content.Shared._KS14.TTS;

namespace Content.Server._KS14.TTS;

[RegisterComponent]
public sealed partial class TtsVoiceComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField]
    public ProtoId<TtsVoicePrototype>? Id = null;

    /// <summary>
    ///     Until when this entity's lines go unvoiced, so a burst of messages doesn't become a burst of clips. Set per
    ///         line, longer for longer lines.
    /// </summary>
    [ViewVariables, Access(typeof(TtsSystem))]
    public TimeSpan CooldownEnd;
}
