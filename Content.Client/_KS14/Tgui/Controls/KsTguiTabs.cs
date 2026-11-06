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
