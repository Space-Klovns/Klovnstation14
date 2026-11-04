using Robust.Client.UserInterface;

namespace Content.Client._KS14.UserInterface.Tgui;

/// <summary>
///     A row of tgui buttons where the current choice shows selected, like the dispenser's amounts. It fills a
///         container it is given and raises <see cref="OnButtonPressed"/> with the pressed choice.
/// </summary>
public sealed class KsTguiChoiceButtons
{
    private readonly Dictionary<string, KsTguiButton> _buttons = new();

    public KsTguiChoiceButtons(Control container, IEnumerable<string> choices, KsTguiIconKind icon = KsTguiIconKind.None)
    {
        foreach (var choice in choices)
        {
            var button = new KsTguiButton { Text = choice, Icon = icon };
            button.OnPressed += _ => OnButtonPressed?.Invoke(choice);
            _buttons.Add(choice, button);
            container.AddChild(button);
        }
    }

    public event Action<string>? OnButtonPressed;

    /// <summary>
    ///     The choice shown selected, or null for none.
    /// </summary>
    public string? Selected
    {
        set
        {
            foreach (var (choice, button) in _buttons)
            {
                button.Selected = choice == value;
            }
        }
    }
}
