using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Input;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     tgui-core's <c>Input</c> ends every Enter with <c>blur()</c>: the box lets go of the keyboard once its <c>onEnter</c>
///         has run. Here that is every <see cref="LineEdit"/> in a window with the tgui chrome. Chat, the lobby, the console
///         and anything else outside such a window keeps the caret.
/// </summary>
public sealed partial class KsTguiFeelUIController
{
    /// <summary>
    ///     Who the last key bind went to, as the UI manager worked it out. It is told after the control has handled the key
    ///         and this is not, which is the order that lets a line edit's own Enter handlers run before it is blurred.
    /// </summary>
    private Control? _lastKeyBindTarget;

    private void InitializeBlurOnEnter()
    {
        UIManager.OnKeyBindDown += control => _lastKeyBindTarget = control;
        _inputManager.UIKeyBindStateChanged += OnBlurOnEnterKeyBind;
    }

    /// <summary>
    ///     Runs after the UI manager has given a key to its control: if that was Enter in a line edit of a tgui window, the
    ///         line edit's <c>OnTextEntered</c> handlers have run, and now the keyboard is let go. Never takes the key.
    /// </summary>
    public bool OnBlurOnEnterKeyBind(BoundKeyEventArgs args)
    {
        // Set by the UI manager for this very key or not at all, so it is never carried over to the next one.
        var target = _lastKeyBindTarget;
        _lastKeyBindTarget = null;

        if (args.State != BoundKeyState.Down || args.Function != EngineKeyFunctions.TextSubmit)
            return false;

        if (target is not LineEdit input || UIManager.KeyboardFocused != input || !IsInTguiWindow(input))
            return false;

        input.ReleaseKeyboardFocus();
        return false;
    }

    /// <summary>
    ///     Whether a control is in a window that the tgui chrome is on, which is what makes it one of tgui's.
    /// </summary>
    public static bool IsInTguiWindow(Control control)
    {
        for (var current = control.Parent; current != null; current = current.Parent)
        {
            if (current is BaseWindow window)
                return KsTguiChromeUIController.HasChrome(window);
        }

        return false;
    }
}
