using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.TTS;

[Prototype]
public sealed partial class TtsVoicePrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public string Voice = default!;

    [DataField]
    public float Speed = 1f;

    [DataField]
    public float Pitch = 1f;

    [DataField]
    public float Volume = 0f;

    [DataField]
    public bool RadioFilter;
}
