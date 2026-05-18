using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.TTS;

[Prototype]
public sealed partial class TtsVoicePrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    ///     Actual back-end name of the voice.
    /// </summary>
    [DataField(required: true)]
    public string Voice = default!;
}
