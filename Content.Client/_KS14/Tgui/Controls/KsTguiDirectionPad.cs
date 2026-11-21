using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui's eight-way arrow pad: a 3x3 grid of icon buttons, north up, with an optional button in the middle. Ported from
///         the launchpad console's <c>LaunchpadButtonPad</c> (tgstation
///         <c>tgui/packages/tgui/interfaces/LaunchpadConsole.tsx</c> at e6c9da8511c11d6bb696a306685885e972a079ca): the
///         same Font Awesome arrows, the diagonals being the straight ones turned 45 degrees, and 6px between buttons.
/// </summary>
/// <remarks>
///     A direction is a <see cref="Vector2i"/>: X east, Y north, each -1, 0 or 1; the middle is (0, 0). Made of
///         <see cref="KsTguiButton"/>s, so it follows both themes through their button rules.
/// </remarks>
public sealed class KsTguiDirectionPad : GridContainer
{
    /// <summary>
    ///     The gap between buttons, before each button's own 2px tgui margin: 6px in all, tg's <c>mb={1}</c> and Stack gap.
    /// </summary>
    public const int ButtonGap = 4;

    /// <summary>
    ///     Each cell, row by row from the top left: its direction, glyph and rotation, as tg's <c>buttonConfigs</c>.
    /// </summary>
    private static readonly (Vector2i Direction, string Icon, float Rotation)[] Cells =
    {
        (new Vector2i(-1, 1), KsTguiIcons.ArrowLeft, 45f),
        (new Vector2i(0, 1), KsTguiIcons.ArrowUp, 0f),
        (new Vector2i(1, 1), KsTguiIcons.ArrowUp, 45f),
        (new Vector2i(-1, 0), KsTguiIcons.ArrowLeft, 0f),
        (Vector2i.Zero, string.Empty, 0f),
        (new Vector2i(1, 0), KsTguiIcons.ArrowRight, 0f),
        (new Vector2i(-1, -1), KsTguiIcons.ArrowDown, 45f),
        (new Vector2i(0, -1), KsTguiIcons.ArrowDown, 0f),
        (new Vector2i(1, -1), KsTguiIcons.ArrowRight, 45f),
    };

    private readonly Dictionary<Vector2i, KsTguiButton> _buttons = new();
    private readonly KsTguiButton _centre;
    private readonly Control _centreSpace;

    /// <summary>
    ///     A button was pressed: its direction, or (0, 0) for the middle one.
    /// </summary>
    public event Action<Vector2i>? OnDirectionPressed;

    public KsTguiDirectionPad()
    {
        Columns = 3;
        HSeparationOverride = ButtonGap;
        VSeparationOverride = ButtonGap;

        _centre = new KsTguiButton { HorizontalAlignment = HAlignment.Stretch, Visible = false };
        _centreSpace = new Control();

        foreach (var (direction, icon, rotation) in Cells)
        {
            if (direction == Vector2i.Zero)
            {
                _centre.OnPressed += _ => OnDirectionPressed?.Invoke(Vector2i.Zero);
                AddChild(_centre);
                AddChild(_centreSpace);
                continue;
            }

            var button = new KsTguiButton
            {
                Icon = icon,
                IconRotation = rotation,
                HorizontalAlignment = HAlignment.Stretch,
            };
            button.OnPressed += _ => OnDirectionPressed?.Invoke(direction);
            _buttons[direction] = button;
            AddChild(button);
        }

        // Not laid out: only there so that no button is the last child, which tgui's margins leave without one, so that
        //      all eight are the same size.
        AddChild(new Control { Visible = false });
    }

    /// <summary>
    ///     Text on the middle button, as tg's launchpad's "R"; null or empty with no <see cref="CentreIcon"/> leaves the
    ///         middle empty.
    /// </summary>
    public string? CentreText
    {
        get => _centre.Text;
        set
        {
            _centre.Text = value;
            RefreshCentre();
        }
    }

    public string? CentreIcon
    {
        get => _centre.Icon;
        set
        {
            _centre.Icon = value;
            RefreshCentre();
        }
    }

    /// <summary>
    ///     The button for a direction, or the middle one for (0, 0); null for anything else.
    /// </summary>
    public KsTguiButton? GetButton(Vector2i direction)
    {
        if (direction == Vector2i.Zero)
            return _centre;

        return _buttons.GetValueOrDefault(direction);
    }

    public void SetDirectionDisabled(Vector2i direction, bool disabled)
    {
        if (GetButton(direction) is { } button)
            button.Disabled = disabled;
    }

    /// <summary>
    ///     Disables or enables every button at once.
    /// </summary>
    public void SetAllDisabled(bool disabled)
    {
        foreach (var button in _buttons.Values)
        {
            button.Disabled = disabled;
        }

        _centre.Disabled = disabled;
    }

    private void RefreshCentre()
    {
        var shown = !string.IsNullOrEmpty(_centre.Text) || !string.IsNullOrEmpty(_centre.Icon);
        _centre.Visible = shown;
        _centreSpace.Visible = !shown;
    }
}
