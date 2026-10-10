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

    /// <summary>
    ///     What the character editor calls this voice.
    /// </summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>
    ///     Whether a player can pick this voice for their character, and whether it can be handed out at random.
    ///         Voices reserved for particular entities turn this off and are then only reachable by setting
    ///         them on that entity directly.
    /// </summary>
    [DataField]
    public bool Selectable = true;
}
