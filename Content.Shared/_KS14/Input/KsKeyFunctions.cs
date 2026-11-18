using Robust.Shared.Input;

namespace Content.Shared._KS14.Input;

/// <summary>
///     Klovnstation's key functions, kept apart from upstream's <see cref="Content.Shared.Input.ContentKeyFunctions"/>.
///     A new one is also added to an input context (<c>ContentContexts</c>) and the controls options
///     (<c>KeyRebindTab</c>), and gets a default binding in <c>Resources/keybinds.yml</c> if it should have one.
/// </summary>
[KeyFunctions]
public static class KsKeyFunctions
{
    public static readonly BoundKeyFunction OpenLanguageMenu = "KsOpenLanguageMenu";
    public static readonly BoundKeyFunction VoicePushToTalk = "KsVoicePushToTalk";
    public static readonly BoundKeyFunction ToggleThrowMode = "KsToggleThrowMode";
}
