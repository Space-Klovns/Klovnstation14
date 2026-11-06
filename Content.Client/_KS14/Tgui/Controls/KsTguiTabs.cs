using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Robust.Client.UserInterface.Controls.BaseButton;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui's Tabs: a row (or column, when <see cref="Vertical"/>) of <see cref="KsTguiTabItem"/>s. Add them in XAML or with
///         <see cref="AddTab"/>. Clicking one selects it and raises <see cref="OnTabSelected"/>, unless
///         <see cref="AutoSelect"/> is off, in which case the owner calls <see cref="Select"/> itself, as a tgui tab
///         that is controlled by its state.
/// </summary>
public sealed class KsTguiTabs : BoxContainer
{
    private bool _vertical;
    private bool _fluid;
    private bool _candystripe;
    private Color? _stripeColor;

    public KsTguiTabs()
    {
        Orientation = LayoutOrientation.Horizontal;
        RefreshPadding();
    }

    /// <summary>
    ///     Raised when the player clicks a tab.
    /// </summary>
    public event Action<KsTguiTabItem>? OnTabSelected;

    /// <summary>
    ///     Selects the clicked tab itself. Turn off to decide in <see cref="OnTabSelected"/>.
    /// </summary>
    public bool AutoSelect { get; set; } = true;

    /// <summary>
    ///     tgui's <c>candystripe</c> class on every tab: the first, third and so on of the visible tabs are darkened across
    ///         the whole width.
    /// </summary>
    public bool Candystripe
    {
        get => _candystripe;
        set
        {
            _candystripe = value;
            if (value)
                AddStyleClass(KsTguiSheetlet.StyleClassCandystripe);
            else
                RemoveStyleClass(KsTguiSheetlet.StyleClassCandystripe);
        }
    }

    /// <summary>
    ///     Whether a tab is drawn dark: striping is on and it is an odd tab among the visible ones.
    /// </summary>
    public bool IsStriped(KsTguiTabItem tab)
    {
        if (!_candystripe)
            return false;

        var index = 0;
        foreach (var child in Children)
        {
            if (!child.Visible)
                continue;

            if (child == tab)
                return index % 2 == 0;

            index++;
        }

        return false;
    }

    protected override void StylePropertiesChanged()
    {
        base.StylePropertiesChanged();
        _stripeColor = TryGetStyleProperty<Color>(KsTguiSheetlet.StylePropertyCandystripe, out var color) ? color : null;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (!_candystripe || _stripeColor is not { } color)
            return;

        foreach (var child in Children)
        {
            if (child is not KsTguiTabItem { Visible: true } tab || !IsStriped(tab))
                continue;

            var box = Vertical
                ? new UIBox2(0f, tab.PixelPosition.Y, PixelWidth, tab.PixelPosition.Y + tab.PixelHeight)
                : new UIBox2(tab.PixelPosition.X, 0f, tab.PixelPosition.X + tab.PixelWidth, PixelHeight);
            handle.DrawRect(box, color);
        }
    }

    public bool Vertical
    {
        get => _vertical;
        set
        {
            _vertical = value;
            Orientation = value ? LayoutOrientation.Vertical : LayoutOrientation.Horizontal;
            RefreshPadding();
            foreach (var child in Children)
            {
                if (child is KsTguiTabItem item)
                    item.Vertical = value;
            }
        }
    }

    /// <summary>
    ///     Tabs share the width between them.
    /// </summary>
    public bool Fluid
    {
        get => _fluid;
        set
        {
            _fluid = value;
            foreach (var child in Children)
            {
                if (child is KsTguiTabItem item)
                    item.HorizontalExpand = value && !_vertical;
            }
        }
    }

    public int SelectedIndex
    {
        get
        {
            var index = 0;
            foreach (var child in Children)
            {
                if (child is not KsTguiTabItem item)
                    continue;

                if (item.Selected)
                    return index;

                index++;
            }

            return -1;
        }
    }

    public KsTguiTabItem AddTab(string text, string? icon = null, string? count = null, bool selected = false)
    {
        var item = new KsTguiTabItem { Text = text, Icon = icon, Count = count };
        AddChild(item);
        if (selected)
            Select(item);

        return item;
    }

    /// <summary>
    ///     Marks this tab selected and every other not.
    /// </summary>
    public void Select(KsTguiTabItem selected)
    {
        foreach (var child in Children)
        {
            if (child is KsTguiTabItem item)
                item.Selected = item == selected;
        }
    }

    private void RefreshPadding()
    {
        // tgui: 3px around the tabs, except on the side they run into the content.
        Margin = _vertical ? new Thickness(3f, 3f, 0f, 3f) : new Thickness(3f, 3f, 3f, 0f);
    }

    protected override void ChildAdded(Control newChild)
    {
        base.ChildAdded(newChild);
        if (newChild is not KsTguiTabItem item)
            return;

        item.Vertical = _vertical;
        item.HorizontalExpand = _fluid && !_vertical;
        item.OnPressed += OnItemPressed;
    }

    protected override void ChildRemoved(Control child)
    {
        base.ChildRemoved(child);
        if (child is KsTguiTabItem item)
            item.OnPressed -= OnItemPressed;
    }

    private void OnItemPressed(ButtonEventArgs args)
    {
        if (args.Button is not KsTguiTabItem item)
            return;

        if (AutoSelect)
            Select(item);

        OnTabSelected?.Invoke(item);
    }
}
