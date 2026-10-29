using Content.Client._KS14.McqDialogue.UI;

namespace Content.Client._KS14.McqDialogue;

/// <summary>
///     On a dialogue's source, on the user's client only, from when their client started the dialogue until the
///         server's dialogue arrives and its UI takes over the window, or <see cref="ExpiresAt"/>.
/// </summary>
[RegisterComponent]
[Access(typeof(McqDialoguePredictionSystem))]
public sealed partial class McqDialoguePredictedComponent : Component
{
    /// <summary>
    ///     The window shown meanwhile. Null once it's been handed over.
    /// </summary>
    [ViewVariables]
    public McqDialogueWindow? Window;

    /// <summary>
    ///     The option picked before the server's dialogue arrived, to be sent once it does.
    /// </summary>
    [ViewVariables]
    public string? ChosenId;

    /// <summary>
    ///     Whether the window was closed, with or without an answer, before the server's dialogue arrived.
    /// </summary>
    [ViewVariables]
    public bool Closed;

    /// <summary>
    ///     When to give up on the server's dialogue: it was mispredicted, and the server never opened one.
    /// </summary>
    [ViewVariables]
    public TimeSpan ExpiresAt;
}
