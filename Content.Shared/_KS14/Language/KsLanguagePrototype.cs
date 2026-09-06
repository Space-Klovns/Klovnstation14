using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Language;

/// <summary>
///     An in-character language. Non-understanders receive deterministically scrambled text.
///     IDs are mechanical; flavor lives in locale under ks-language-{ID}-name / -description.
/// </summary>
[Prototype]
public sealed partial class KsLanguagePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    ///     How text renders for listeners who don't understand this language.
    /// </summary>
    [DataField(required: true)]
    public KsObfuscationMethod Obfuscation = default!;

    /// <summary>
    ///     Message-body tint for every listener, so the language is recognizable even unreadable.
    /// </summary>
    [DataField]
    public Color? Color;

    /// <summary>
    ///     Font override for the message body. Null keeps the speech verb's font.
    /// </summary>
    [DataField]
    public string? FontId;

    [DataField]
    public int? FontSize;

    /// <summary>
    ///     Prepend a "(Name)" chip to chat lines. Off by default; color and font are the normal
    ///     tell.
    /// </summary>
    [DataField]
    public bool ShowTag;

    [DataField]
    public bool AllowRadio = true;

    /// <summary>
    ///     Menu and command-index ordering. Lower sorts first; ties break on ID.
    /// </summary>
    [DataField]
    public int SortOrder;

    public string LocalizedName => Loc.GetString($"ks-language-{ID}-name");

    public string LocalizedDescription => Loc.GetString($"ks-language-{ID}-description");
}
