using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui's MenuBar.MenuItem, and with <see cref="IsToggle"/> its MenuItemToggle: a line of the menu. It is 25px tall (4px
///         of padding round a 17px line) with 36px before the text and 12px after it, or for a toggle a 30px cell holding a
///         check when <see cref="Checked"/>. The pointer lights it with the lighter wash at once and the wash fades out over
///         0.2 seconds (<c>--menu-bar-transition</c>). Clicking does not close the menu.
/// </summary>
[Virtual]
public class KsTguiMenuBarItem : Control
{
    /// <summary>
    ///     <c>padding-left: calc(var(--space-xxl) + 1rem)</c>.
    /// </summary>
    public const float PaddingLeft = 36f;

    public const float PaddingRight = 12f;

    /// <summary>
    ///     <c>--space-sm</c>, above and below.
    /// </summary>
    public const float PaddingY = 4f;

    /// <summary>
    ///     A toggle's check cell, <c>min-width: 2.5rem</c>.
    /// </summary>
    public const float CheckCellWidth = 30f;

    /// <summary>
    ///     The cell's own <c>padding-left</c>.
    /// </summary>
    public const float CheckCellPadding = 4f;

    /// <summary>
    ///     <c>--menu-bar-transition</c>.
    /// </summary>
    public const float FadeSeconds = 0.2f;

    internal const string CheckGlyph = "";
    internal const string ChevronRightGlyph = "";

    private readonly Label _label = new() { ClipText = true };
    private readonly KsTguiIcon _check = new() { Glyph = CheckGlyph, IconSize = 1.3f, Visible = false };

    private bool _disabled;
    private bool _hovered;
    private bool _toggle;
    private float _fade;

    public KsTguiMenuBarItem()
    {
        MouseFilter = MouseFilterMode.Stop;
        AddChild(_label);
        AddChild(_check);
        RefreshText();
    }

    /// <summary>
    ///     Raised with <see cref="Value"/> when it is clicked, unless it is disabled.
    /// </summary>
    public event Action<object?>? Clicked;

    public object? Value { get; set; }

    public string? Text
    {
        get => _label.Text;
        set => _label.Text = value;
    }

    public bool Disabled
    {
        get => _disabled;
        set
        {
            _disabled = value;
            RefreshText();
        }
    }

    /// <summary>
    ///     A toggle item, with a cell for the check in front.
    /// </summary>
    public bool IsToggle
    {
        get => _toggle;
        set
        {
            _toggle = value;
            _check.Visible = _toggle && _checked;
            InvalidateMeasure();
        }
    }

    private bool _checked;

    /// <summary>
    ///     Whether a toggle shows its check.
    /// </summary>
    public bool Checked
    {
        get => _checked;
        set
        {
            _checked = value;
            _check.Visible = _toggle && value;
        }
    }

    /// <summary>
    ///     Where the text starts: after the 36px of an ordinary item, or the check cell of a toggle.
    /// </summary>
    public float TextLeft => _toggle ? CheckCellWidth : PaddingLeft;

    /// <summary>
    ///     How much of the lighter wash is showing, 1 right after the pointer moves on and fading to 0.
    /// </summary>
    public float Fade => _fade;

    public bool Hovered => _hovered && !_disabled;

    /// <summary>
    ///     What a click does; also for tests and keyboards.
    /// </summary>
    public void Activate()
    {
        if (_disabled)
            return;

        Clicked?.Invoke(Value);
    }

    /// <summary>
    ///     The pointer moving on or off. The wash comes on at once and goes off over <see cref="FadeSeconds"/>.
    /// </summary>
    public void SetHovered(bool hovered)
    {
        _hovered = hovered;
        if (hovered && !_disabled)
            _fade = 1f;
    }

    /// <summary>
    ///     Runs the wash's fade-out for this long.
    /// </summary>
    public void AdvanceFade(float seconds)
    {
        if (Hovered || _fade <= 0f)
            return;

        _fade = MathF.Max(0f, _fade - seconds / FadeSeconds);
    }

    /// <summary>
    ///     Whether the row should be drawn with the wash, as the submenu's open state does too.
    /// </summary>
    protected virtual bool Lit => Hovered;

    private void RefreshText()
    {
        _label.FontColorOverride = _disabled ? KsTguiMenuBar.DisabledText : null;
        if (_disabled)
            _fade = 0f;
    }

    /// <summary>
    ///     Room wanted after the text, before the right padding.
    /// </summary>
    protected virtual float TrailingWidth => 0f;

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        _label.Measure(new Vector2(float.PositiveInfinity, KsTguiMenuBar.LineHeight));
        _check.Measure(Vector2Helpers.Infinity);
        return new Vector2(TextLeft + _label.DesiredSize.X + TrailingWidth + PaddingRight, KsTguiMenuBar.LineHeight + PaddingY * 2f);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var labelHeight = MathF.Min(_label.DesiredSize.Y, KsTguiMenuBar.LineHeight);
        _label.Arrange(UIBox2.FromDimensions(
            TextLeft,
            PaddingY + (KsTguiMenuBar.LineHeight - labelHeight) / 2f,
            MathF.Max(0f, finalSize.X - TextLeft - PaddingRight - TrailingWidth),
            labelHeight));

        // The check is centred in the cell, inside its 4px of padding.
        var checkSize = _check.DesiredSize;
        _check.Arrange(UIBox2.FromDimensions(
            CheckCellPadding + (CheckCellWidth - CheckCellPadding - checkSize.X) / 2f,
            (finalSize.Y - checkSize.Y) / 2f,
            checkSize.X,
            checkSize.Y));

        return finalSize;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var color = Lit ? KsTguiMenuBar.Hover : Color.InterpolateBetween(KsTguiMenuBar.Background, KsTguiMenuBar.Hover, _fade);
        handle.DrawRect(PixelSizeBox, color);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        AdvanceFade(args.DeltaSeconds);
    }

    protected override void MouseEntered()
    {
        base.MouseEntered();

        SetHovered(true);
    }

    protected override void MouseExited()
    {
        base.MouseExited();

        SetHovered(false);
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        Activate();
        args.Handle();
    }
}

/// <summary>
///     tgui's MenuBar.Submenu: an item with a chevron at its right that opens another menu beside it, to the right and top
///         aligned. The menu opens once the pointer has rested on the item for 0.25 seconds, and closes once it has been off
///         both the item and the menu for as long. While it is open the item keeps the lighter wash.
/// </summary>
public sealed class KsTguiMenuBarSubmenu : KsTguiMenuBarItem
{
    /// <summary>
    ///     <c>hoverDelay: 250</c>.
    /// </summary>
    public const float HoverDelaySeconds = 0.25f;

    private readonly KsTguiMenuBarMenu _menu = new();
    private readonly KsTguiIcon _chevron = new() { Glyph = ChevronRightGlyph };

    private float _delay;

    public KsTguiMenuBarSubmenu()
    {
        AddChild(_chevron);
        _menu.Closed += () => _delay = 0f;
    }

    public KsTguiMenuBarMenu Menu => _menu;

    public bool IsOpen => _menu.IsOpen;

    public KsTguiMenuBarItem AddItem(string text, Action<object?>? onClicked = null, object? value = null, bool disabled = false)
    {
        var item = new KsTguiMenuBarItem { Text = text, Value = value, Disabled = disabled };
        if (onClicked != null)
            item.Clicked += onClicked;

        _menu.AddChild(item);
        return item;
    }

    protected override bool Lit => Hovered || IsOpen;

    protected override float TrailingWidth => _chevron.DesiredSize.X;

    /// <summary>
    ///     Moves the hover timers on. The menu opens after <see cref="HoverDelaySeconds"/> over the item, and closes after as
    ///         long away from the item and the menu; being over either keeps what is there.
    /// </summary>
    public void AdvanceHover(float seconds, bool overItem, bool overMenu)
    {
        if (Disabled)
            return;

        var wanted = overItem || (IsOpen && overMenu);
        if (wanted == IsOpen)
        {
            _delay = 0f;
            return;
        }

        _delay += seconds;
        if (_delay < HoverDelaySeconds)
            return;

        _delay = 0f;
        if (wanted)
            _menu.OpenAt(GlobalPosition + new Vector2(Width, 0f));
        else
            _menu.Close();
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        _chevron.Measure(Vector2Helpers.Infinity);
        return base.MeasureOverride(availableSize);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        base.ArrangeOverride(finalSize);

        var chevronSize = _chevron.DesiredSize;
        _chevron.Arrange(UIBox2.FromDimensions(
            finalSize.X - PaddingRight - chevronSize.X,
            (finalSize.Y - chevronSize.Y) / 2f,
            chevronSize.X,
            chevronSize.Y));
        return finalSize;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        var mouse = UserInterfaceManager.MousePositionScaled.Position;
        AdvanceHover(args.DeltaSeconds, GlobalRect.Contains(mouse), _menu.IsOpen && _menu.GlobalRect.Contains(mouse));
    }
}

/// <summary>
///     tgui's MenuBar.Separator: <c>margin: var(--space-sm)</c> (4px) round a 1px <c>--color-border</c> line.
/// </summary>
public sealed class KsTguiMenuBarSeparator : Control
{
    public const float SeparatorMargin = 4f;
    public const float Thickness = 1f;

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        return new Vector2(SeparatorMargin * 2f, SeparatorMargin * 2f + Thickness);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        handle.DrawRect(PixelSizeBox, KsTguiMenuBar.Background);
        var thickness = MathF.Max(1f, MathF.Round(Thickness * UIScale));
        handle.DrawRect(
            UIBox2.FromDimensions(SeparatorMargin * UIScale, SeparatorMargin * UIScale, MathF.Max(0f, PixelWidth - SeparatorMargin * 2f * UIScale), thickness),
            KsTguiPalettes.Border);
    }
}
