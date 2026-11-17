using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.RadioLocalization;

/// <summary>
///     Localized radio shortcut characters. Wire/channel keycodes remain language-independent.
/// </summary>
[Prototype]
public sealed partial class KsRadioLocalePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public string Culture { get; private set; } = default!;

    /// <summary>
    ///     Canonical radio keycode to its localized character, including the default/department key.
    /// </summary>
    [DataField(required: true)]
    public Dictionary<char, char> Keys { get; private set; } = new();
}
