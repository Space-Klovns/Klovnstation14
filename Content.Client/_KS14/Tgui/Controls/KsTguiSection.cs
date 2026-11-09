using System.Numerics;
using Content.Client.Stylesheets;
using Content.Client.Stylesheets.Fonts;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui's Section: a title row with a blue line under it, over a darkened body. XAML children go to the body. Sections
///         stacked in a window are 6 apart in tgui (<see cref="Gap"/>): give the box holding them that
///         <c>SeparationOverride</c>.
/// </summary>
public sealed class KsTguiSection : KsSectionPanel
{
    /// <summary>
    ///     tgui's <c>--space-m</c>, the margin under a section.
    /// </summary>
    public const int Gap = 6;

    /// <summary>
    ///     On the title row of a section inside another.
    /// </summary>
    public const string StyleClassNestedTitle = "KsTguiSectionNestedTitle";

    private static readonly Thickness BodyPadding = new(6f, 8f, 6f, 8f);

    private readonly BoxContainer _root;
    private readonly KsSectionTitle _titlePanel;
    private readonly TitleRow _titleRow;
    private readonly Label _titleLabel;
    private readonly Control _buttonsSlot;
    private readonly Control _rest;
    private readonly BoxContainer _content;
    private readonly KsTguiScrollContainer _scroll;

    private Control? _buttons;
    private bool _scrollable;
    private bool _fill;
    private bool _fitted;
    private bool _noTopPadding;
    private bool _scrollableHorizontal;
    private bool _stretchContents;
    private int _depth;
    private Control? _watchedParent;

    public KsTguiSection()
    {
        BaronColor = Color.FromHex("#1B1B1E");

        _titleLabel = new Label();
        _titleLabel.AddStyleClass(StyleClass.LabelHeading);

        _buttonsSlot = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        _titleRow = new TitleRow(_titleLabel, _buttonsSlot);

        _titlePanel = new KsSectionTitle { Visible = false };
        _titlePanel.AddChild(_titleRow);

        _content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = BodyPadding,
            HorizontalExpand = true,
        };

        _scroll = new KsTguiScrollContainer
        {
            HScrollEnabled = false,
            ReturnMeasure = true,
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        _rest = new Control { RectClipContent = true };
        _rest.AddChild(_content);

        _root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        _root.AddChild(_titlePanel);
        _root.AddChild(_rest);
        AddChild(_root);

        XamlChildren = _content.Children;
    }

    /// <summary>
    ///     The title; no title and no <see cref="Buttons"/> means no title row.
    /// </summary>
    public string? Title
    {
        get => _titleLabel.Text;
        set
        {
            _titleLabel.Text = value;
            RefreshTitleVisibility();
        }
    }

    /// <summary>
    ///     A control at the right of the title, normally a row of <see cref="KsTguiButton"/>s.
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

            RefreshTitleVisibility();
        }
    }

    /// <summary>
    ///     The body scrolls vertically when it is given less room than it needs.
    /// </summary>
    public bool Scrollable
    {
        get => _scrollable;
        set
        {
            if (_scrollable == value)
                return;

            _scrollable = value;
            RebuildBody();
        }
    }

    /// <summary>
    ///     tgui's <c>scrollableHorizontal</c>: the body scrolls sideways when it is given less width than it needs. Together
    ///         with <see cref="Scrollable"/> it scrolls both ways.
    /// </summary>
    public bool ScrollableHorizontal
    {
        get => _scrollableHorizontal;
        set
        {
            if (_scrollableHorizontal == value)
                return;

            _scrollableHorizontal = value;
            RebuildBody();
        }
    }

    /// <summary>
    ///     tgui's <c>noTopPadding</c>: the body starts at the title's line.
    /// </summary>
    public bool NoTopPadding
    {
        get => _noTopPadding;
        set
        {
            _noTopPadding = value;
            RefreshBodyPadding();
        }
    }

    /// <summary>
    ///     tgui's <c>stretchContents</c> (<c>height: calc(100% - 3rem)</c>): the body takes the height the section has,
    ///         which is what it is given when the section expands.
    /// </summary>
    public bool StretchContents
    {
        get => _stretchContents;
        set
        {
            _stretchContents = value;
            VerticalExpand = value || _fill;
            _rest.VerticalExpand = value || _fill;
        }
    }

    /// <summary>
    ///     Expands to take the space its parent has left.
    /// </summary>
    public bool Fill
    {
        get => _fill;
        set
        {
            _fill = value;
            VerticalExpand = value || _stretchContents;
            _rest.VerticalExpand = value || _stretchContents;
        }
    }

    /// <summary>
    ///     No padding around the body, as tgui's <c>fitted</c>.
    /// </summary>
    public bool Fitted
    {
        get => _fitted;
        set
        {
            _fitted = value;
            RefreshBodyPadding();
        }
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();

        _watchedParent = Parent;
        if (_watchedParent != null)
        {
            _watchedParent.OnChildAdded += OnSiblingsChanged;
            _watchedParent.OnChildRemoved += OnSiblingsChanged;
        }

        var depth = 0;
        for (var parent = Parent; parent != null; parent = parent.Parent)
        {
            if (parent is KsTguiSection)
                depth++;
        }

        RefreshNesting();
        if (depth == _depth)
            return;

        _depth = depth;
        Transparent = depth > 0;
        if (depth > 0)
            _titlePanel.AddStyleClass(StyleClassNestedTitle);
        else
            _titlePanel.RemoveStyleClass(StyleClassNestedTitle);

        // Level 2 titles are 13px and level 3 and deeper 12px.
        var fonts = IoCManager.Resolve<KsTguiFonts>();
        _titleLabel.FontOverride = depth == 0 ? null : fonts.GetFont(depth == 1 ? 13 : 12, FontKind.Bold);
        RefreshNesting();
        RefreshBodyPadding();
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();

        if (_watchedParent != null)
        {
            _watchedParent.OnChildAdded -= OnSiblingsChanged;
            _watchedParent.OnChildRemoved -= OnSiblingsChanged;
            _watchedParent = null;
        }
    }

    private void OnSiblingsChanged(Control _)
    {
        RefreshNesting();
    }

    /// <summary>
    ///     tgui's <c>.Section .Section { margin: 0 -6px }</c>, which pulls a nested section out over the outer body's
    ///         padding so its title line runs edge to edge, and <c>:first-child { margin-top: -6px }</c>. The margin shorthand
    ///         also clears the 6px under a section, so nested ones touch.
    /// </summary>
    private void RefreshNesting()
    {
        var nested = false;
        for (var parent = Parent; parent != null; parent = parent.Parent)
        {
            if (parent is KsTguiSection)
                nested = true;
        }

        if (!nested)
        {
            if (_nestedMargin)
            {
                _nestedMargin = false;
                Margin = default;
            }

            return;
        }

        var isFirst = Parent is { ChildCount: > 0 } siblings && siblings.GetChild(0) == this;
        _nestedMargin = true;
        Margin = new Thickness(-Gap, isFirst ? -Gap : 0f, -Gap, 0f);
    }

    private bool _nestedMargin;

    /// <summary>
    ///     Removes everything in the body. <c>RemoveAllChildren</c> would take the section's own parts with it.
    /// </summary>
    public void ClearBody()
    {
        _content.RemoveAllChildren();
    }

    /// <summary>
    ///     <c>AddChild</c> from code goes to the body, as XAML children do. Left on the panel, a child would sit over the title
    ///         row with no padding.
    /// </summary>
    protected override void ChildAdded(Control newChild)
    {
        base.ChildAdded(newChild);

        if (newChild == _root)
            return;

        newChild.Orphan();
        _content.AddChild(newChild);
    }

    private void RefreshBodyPadding()
    {
        // A nested section is pulled out over the outer padding (RefreshNesting), so its own padding is the usual one.
        _content.Margin = _fitted ? default : new Thickness(BodyPadding.Left, _noTopPadding ? 0f : BodyPadding.Top, BodyPadding.Right, BodyPadding.Bottom);
    }

    private void RefreshTitleVisibility()
    {
        _titlePanel.Visible = !string.IsNullOrEmpty(_titleLabel.Text) || _buttons != null;
    }

    private void RebuildBody()
    {
        _content.Orphan();
        _scroll.Orphan();

        _scroll.VScrollEnabled = _scrollable;
        _scroll.HScrollEnabled = _scrollableHorizontal;
        if (_scrollable || _scrollableHorizontal)
        {
            _scroll.AddChild(_content);
            _rest.AddChild(_scroll);
        }
        else
        {
            _rest.AddChild(_content);
        }
    }

    /// <summary>
    ///     The title with its buttons at the right. The buttons take no part in the row's height, as tgui positions them
    ///         absolutely, 1px up from where the text starts.
    /// </summary>
    private sealed class TitleRow : Control
    {
        private readonly Control _label;
        private readonly Control _buttons;

        public TitleRow(Control label, Control buttons)
        {
            _label = label;
            _buttons = buttons;
            AddChild(label);
            AddChild(buttons);
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            _buttons.Measure(new Vector2(availableSize.X, float.PositiveInfinity));
            _label.Measure(new Vector2(MathF.Max(0f, availableSize.X - _buttons.DesiredSize.X), availableSize.Y));
            return new Vector2(_label.DesiredSize.X + _buttons.DesiredSize.X, _label.DesiredSize.Y);
        }

        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            var buttonsWidth = MathF.Min(_buttons.DesiredSize.X, finalSize.X);
            _label.Arrange(UIBox2.FromDimensions(0f, 0f, MathF.Max(0f, finalSize.X - buttonsWidth), finalSize.Y));
            _buttons.Arrange(UIBox2.FromDimensions(finalSize.X - buttonsWidth, -1f, buttonsWidth, _buttons.DesiredSize.Y));
            return finalSize;
        }
    }
}
