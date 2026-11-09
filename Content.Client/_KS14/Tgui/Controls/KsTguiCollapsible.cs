using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui's Collapsible: a fluid <see cref="KsTguiButton"/> whose chevron points right when closed and down when open, with
///         the content 6px below it, and 6px of margin under the lot. XAML children go to the content. Clicking the header
///         flips <see cref="Open"/> and raises <see cref="OpenChanged"/>. There is no animation, as in tgui.
/// </summary>
public sealed class KsTguiCollapsible : BoxContainer
{
    /// <summary>
    ///     tgui's <c>mb={1}</c> on the Box around the lot, and the default <c>child_mt</c>.
    /// </summary>
    private const float Gap = 6f;

    private readonly KsTguiButton _header;
    private readonly Control _buttonsSlot;
    private readonly BoxContainer _content;

    private Control? _buttons;
    private bool _open;
    private string? _icon;

    public KsTguiCollapsible()
    {
        Orientation = LayoutOrientation.Vertical;
        Margin = new Thickness(0f, 0f, 0f, Gap);

        // The header is the only child of its table cell, so tgui's last-child rule leaves it no margin.
        _header = new KsTguiButton { Fluid = true, InlineIcon = true };
        _header.OnPressed += _ => Open = !_open;

        var cell = new Control { HorizontalExpand = true };
        cell.AddChild(_header);

        _buttonsSlot = new Control { Visible = false };

        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal };
        row.AddChild(cell);
        row.AddChild(_buttonsSlot);
        AddChild(row);

        _content = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            Margin = new Thickness(0f, Gap, 0f, 0f),
            Visible = false,
        };
        AddChild(_content);

        XamlChildren = _content.Children;
        RefreshHeader();
    }

    /// <summary>
    ///     Raised with the new state when the player opens or closes it.
    /// </summary>
    public event Action<bool>? OpenChanged;

    public string? Title
    {
        get => _header.Text;
        set => _header.Text = value;
    }

    public KsTguiColor Color
    {
        get => _header.Color;
        set => _header.Color = value;
    }

    /// <summary>
    ///     Replaces the chevron, as tgui's <c>icon</c>.
    /// </summary>
    public string? Icon
    {
        get => _icon;
        set
        {
            _icon = value;
            RefreshHeader();
        }
    }

    public bool Open
    {
        get => _open;
        set
        {
            if (_open == value)
                return;

            _open = value;
            _content.Visible = value;
            RefreshHeader();
            OpenChanged?.Invoke(value);
        }
    }

    /// <summary>
    ///     tgui's <c>child_mt</c> in pixels, the gap between the header and the content.
    /// </summary>
    public float ChildMarginTop
    {
        get => _content.Margin.Top;
        set => _content.Margin = new Thickness(0f, value, 0f, 0f);
    }

    /// <summary>
    ///     Controls at the right of the header.
    /// </summary>
    public Control? Buttons
    {
        get => _buttons;
        set
        {
            if (_buttons != null)
                _buttonsSlot.RemoveChild(_buttons);

            _buttons = value;
            if (value != null)
            {
                value.Orphan();
                _buttonsSlot.AddChild(value);
            }

            _buttonsSlot.Visible = value != null;
        }
    }

    /// <summary>
    ///     The header, for tests.
    /// </summary>
    public KsTguiButton Header => _header;

    public void ClearBody()
    {
        _content.RemoveAllChildren();
    }

    /// <summary>
    ///     <c>AddChild</c> from code goes to the content, as XAML children do.
    /// </summary>
    protected override void ChildAdded(Control newChild)
    {
        base.ChildAdded(newChild);

        if (newChild.Parent != this || newChild == _content || newChild == _header.Parent?.Parent)
            return;

        newChild.Orphan();
        _content.AddChild(newChild);
    }

    private void RefreshHeader()
    {
        _header.Icon = _icon ?? (_open ? KsTguiIcons.ChevronDown : KsTguiIcons.ChevronRight);
    }
}
