using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui-core's MenuBar (<c>MenuBar.js</c>, <c>MenuBar.scss</c>): a row of <see cref="KsTguiMenuBarDropdown"/> buttons
///         that open menus. The bar holds the open menu and whether hovering over the others should open them, which tgui
///         keeps in the window's state: a click on a button opens its menu (or closes it if it is the one open) and turns
///         hover-opening on or off, a hover over another button opens that one while hover-opening is on, and a click outside
///         closes the menu and turns it off.
/// </summary>
public sealed class KsTguiMenuBar : BoxContainer
{
    /// <summary>
    ///     <c>--menu-bar-background</c>: <c>--color-base</c>.
    /// </summary>
    public static readonly Color Background = KsTguiPalettes.Base;

    /// <summary>
    ///     The hovered wash: the background at lightness +20. The background is a grey, so that is 0.2 on every channel.
    /// </summary>
    public static readonly Color Hover = new(Background.R + 0.2f, Background.G + 0.2f, Background.B + 0.2f);

    /// <summary>
    ///     <c>.MenuBar__font</c>: <c>line-height: 17px</c>, which is all the room the text has.
    /// </summary>
    public const float LineHeight = 17f;

    /// <summary>
    ///     <c>.MenuBar__disabled</c>: the text at 20% opacity.
    /// </summary>
    public static readonly Color DisabledText = KsTguiPalettes.Translucent(Color.White, 0.2f, Background);

    private KsTguiMenuBarDropdown? _openDropdown;
    private bool _openOnHover;

    public KsTguiMenuBar()
    {
        Orientation = LayoutOrientation.Horizontal;
    }

    /// <summary>
    ///     The button whose menu is open.
    /// </summary>
    public KsTguiMenuBarDropdown? OpenDropdown => _openDropdown;

    /// <summary>
    ///     Whether hovering over a button opens its menu.
    /// </summary>
    public bool OpenOnHover => _openOnHover;

    /// <summary>
    ///     Adds a button that opens a menu.
    /// </summary>
    public KsTguiMenuBarDropdown AddDropdown(string text, float openWidth = 0f)
    {
        var dropdown = new KsTguiMenuBarDropdown { Text = text };
        dropdown.Menu.OpenWidth = openWidth;
        AddChild(dropdown);
        return dropdown;
    }

    /// <summary>
    ///     tgui's dropdown <c>onClick</c>: <c>setOpenMenuBar(open === entry ? null : entry); setOpenOnHover(!openOnHover)</c>.
    /// </summary>
    public void ClickDropdown(KsTguiMenuBarDropdown dropdown)
    {
        if (dropdown.Disabled)
            return;

        SetOpen(_openDropdown == dropdown ? null : dropdown);
        _openOnHover = !_openOnHover;
    }

    /// <summary>
    ///     tgui's dropdown <c>onMouseOver</c>: opens this one while hover-opening is on.
    /// </summary>
    public void HoverDropdown(KsTguiMenuBarDropdown dropdown)
    {
        if (dropdown.Disabled || !_openOnHover)
            return;

        SetOpen(dropdown);
    }

    /// <summary>
    ///     tgui's <c>onOutsideClick</c>: closes the menu and turns hover-opening off.
    /// </summary>
    public void DismissOutside()
    {
        SetOpen(null);
        _openOnHover = false;
    }

    private void SetOpen(KsTguiMenuBarDropdown? dropdown)
    {
        if (_openDropdown == dropdown)
            return;

        var previous = _openDropdown;
        _openDropdown = dropdown;
        previous?.CloseMenu();
        dropdown?.OpenMenu();
    }
}

/// <summary>
///     tgui's MenuBar.Dropdown: the button in the bar (<c>padding: 3px 6px</c>, a 17px line of text) and the menu it opens
///         under its left edge. Hovered, the button takes the lighter wash with no fade.
/// </summary>
public sealed class KsTguiMenuBarDropdown : Control
{
    private const float PaddingX = 6f;
    private const float PaddingY = 3f;

    private readonly Label _label = new() { ClipText = true };
    private readonly KsTguiMenuBarMenu _menu = new();

    private bool _disabled;
    private bool _hovered;
    private bool _closingFromBar;

    public KsTguiMenuBarDropdown()
    {
        MouseFilter = MouseFilterMode.Stop;
        AddChild(_label);
        _menu.Closed += OnMenuClosed;
        RefreshText();
    }

    public KsTguiMenuBarMenu Menu => _menu;

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
    ///     Whether the pointer is over the button.
    /// </summary>
    public bool Hovered => _hovered && !_disabled;

    private KsTguiMenuBar? Bar => Parent as KsTguiMenuBar;

    private void RefreshText()
    {
        _label.FontColorOverride = _disabled ? KsTguiMenuBar.DisabledText : null;
    }

    public KsTguiMenuBarItem AddItem(string text, Action<object?>? onClicked = null, object? value = null, bool disabled = false)
    {
        var item = new KsTguiMenuBarItem { Text = text, Value = value, Disabled = disabled };
        if (onClicked != null)
            item.Clicked += onClicked;

        _menu.AddChild(item);
        return item;
    }

    public KsTguiMenuBarItem AddToggle(string text, bool isChecked, Action<object?>? onClicked = null, object? value = null)
    {
        var item = AddItem(text, onClicked, value);
        item.IsToggle = true;
        item.Checked = isChecked;
        return item;
    }

    public KsTguiMenuBarSubmenu AddSubmenu(string text, float openWidth = 0f)
    {
        var submenu = new KsTguiMenuBarSubmenu { Text = text };
        submenu.Menu.OpenWidth = openWidth;
        _menu.AddChild(submenu);
        return submenu;
    }

    public void AddSeparator()
    {
        _menu.AddChild(new KsTguiMenuBarSeparator());
    }

    /// <summary>
    ///     Shows the menu under the button, left edges together.
    /// </summary>
    public void OpenMenu()
    {
        _menu.OpenAt(GlobalPosition + new Vector2(0f, Height));
    }

    /// <summary>
    ///     Closes the menu because the bar moved on, not because of a click outside it.
    /// </summary>
    public void CloseMenu()
    {
        _closingFromBar = true;
        _menu.Close();
        _closingFromBar = false;
    }

    private void OnMenuClosed()
    {
        if (_closingFromBar || Bar is not { } bar || bar.OpenDropdown != this)
            return;

        // A press outside closed it. One on this very button is the click that toggles it, which tgui's Floating allows
        //      because the button is an allowed outside class, so leave the bar as it is for that click.
        if (GlobalRect.Contains(UserInterfaceManager.MousePositionScaled.Position))
            return;

        bar.DismissOutside();
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        _label.Measure(new Vector2(float.PositiveInfinity, KsTguiMenuBar.LineHeight));
        return new Vector2(_label.DesiredSize.X + PaddingX * 2f, KsTguiMenuBar.LineHeight + PaddingY * 2f);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var labelHeight = MathF.Min(_label.DesiredSize.Y, KsTguiMenuBar.LineHeight);
        _label.Arrange(UIBox2.FromDimensions(
            PaddingX,
            PaddingY + (KsTguiMenuBar.LineHeight - labelHeight) / 2f,
            MathF.Max(0f, finalSize.X - PaddingX * 2f),
            labelHeight));
        return finalSize;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        handle.DrawRect(PixelSizeBox, Hovered ? KsTguiMenuBar.Hover : KsTguiMenuBar.Background);
    }

    protected override void MouseEntered()
    {
        base.MouseEntered();

        _hovered = true;
        Bar?.HoverDropdown(this);
    }

    protected override void MouseExited()
    {
        base.MouseExited();

        _hovered = false;
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        Bar?.ClickDropdown(this);
        args.Handle();
    }
}
