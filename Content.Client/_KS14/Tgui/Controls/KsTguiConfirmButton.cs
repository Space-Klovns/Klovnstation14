using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui's <c>Button.Confirm</c>: the first press arms it (it takes <see cref="ConfirmColor"/>,
///         <see cref="ConfirmIcon"/> and <see cref="ConfirmText"/>), the second raises <see cref="Confirmed"/> and puts it back.
///         Losing keyboard focus, which clicking anywhere else does, puts it back without confirming, as tgui's blur.
/// </summary>
public sealed class KsTguiConfirmButton : KsTguiButton
{
    private bool _armed;
    private string? _restText;
    private string? _restIcon;
    private KsTguiColor _restColor;

    public KsTguiConfirmButton()
    {
        EllipsisText = true; // tgui's default for Confirm
        CanKeyboardFocus = true;
        KeyboardFocusOnClick = true;
        OnPressed += _ => Press();
    }

    /// <summary>
    ///     Raised by the second press.
    /// </summary>
    public event Action? Confirmed;

    public KsTguiColor ConfirmColor { get; set; } = KsTguiColor.Bad;

    public string? ConfirmIcon { get; set; }

    public string ConfirmText { get; set; } = "Confirm?";

    public bool Armed => _armed;

    /// <summary>
    ///     What a press does: arms the button, or confirms when it is armed.
    /// </summary>
    public void Press()
    {
        if (_armed)
        {
            Disarm();
            Confirmed?.Invoke();
            return;
        }

        _armed = true;
        _restText = FullText;
        _restIcon = Icon;
        _restColor = Color;
        Color = ConfirmColor;
        Icon = ConfirmIcon;
        Text = ConfirmText;
    }

    /// <summary>
    ///     Puts the button back to what it was.
    /// </summary>
    public void Disarm()
    {
        if (!_armed)
            return;

        _armed = false;
        Color = _restColor;
        Icon = _restIcon;
        Text = _restText;
    }

    protected override void KeyboardFocusExited()
    {
        base.KeyboardFocusExited();
        Disarm();
    }
}
