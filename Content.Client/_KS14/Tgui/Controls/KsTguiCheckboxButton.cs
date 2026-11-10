using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui's <c>Button.Checkbox</c>: a transparent button with a hollow or ticked square in front of its text, whose text
///         is green while it is checked. Pressing flips <see cref="Checked"/> and raises <see cref="Toggled"/>, unless
///         <see cref="AutoToggle"/> is off, for an owner that decides itself, as a tgui checkbox does from its state.
/// </summary>
public sealed class KsTguiCheckboxButton : KsTguiButton
{
    private bool _checked;

    public KsTguiCheckboxButton()
    {
        Color = KsTguiColor.Transparent;
        IconRegular = true;
        Icon = KsTguiIcons.SquareO;
        OnPressed += _ => Press();
    }

    /// <summary>
    ///     Raised with the new value when the player presses the button.
    /// </summary>
    public event Action<bool>? Toggled;

    public bool AutoToggle { get; set; } = true;

    public bool Checked
    {
        get => _checked;
        set
        {
            _checked = value;
            Selected = value;
            Icon = value ? KsTguiIcons.CheckSquareO : KsTguiIcons.SquareO;
        }
    }

    /// <summary>
    ///     What a press does.
    /// </summary>
    public void Press()
    {
        var next = !_checked;
        if (AutoToggle)
            Checked = next;

        Toggled?.Invoke(next);
    }
}
