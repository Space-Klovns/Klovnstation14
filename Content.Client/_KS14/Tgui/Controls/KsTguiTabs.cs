using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
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

    /// <summary>
    ///     tgui's <c>--tabs-container-background</c>: the Section colour, black at .33, flattened over the window.
    /// </summary>
    private static readonly Color StripColor = KsTguiPalettes.Translucent(Color.Black, 0.33f, KsTguiPalettes.Base);

    private Control? _watchedParent;
    private Thickness _padding;
    private Thickness _appliedMargin;
    private bool _inSection;

    public KsTguiTabs()
    {
        Orientation = LayoutOrientation.Horizontal;

        _chrome = new ScrollChrome(this);
        _leftButton = new KsTguiButton { Icon = KsTguiIcons.AngleLeft, Color = KsTguiColor.Transparent };
        _rightButton = new KsTguiButton { Icon = KsTguiIcons.AngleRight, Color = KsTguiColor.Transparent };
        _leftButton.OnPressed += _ => ScrollByHalf(-1f);
        _rightButton.OnPressed += _ => ScrollByHalf(1f);

        // Each alone in a host, so neither is a button with a sibling after it and gets tgui's 2px margin: the scroll buttons
        //      have none (.scroll-left, .scroll-right).
        _leftHost = new Control { Visible = false };
        _leftHost.AddChild(_leftButton);
        _rightHost = new Control { Visible = false };
        _rightHost.AddChild(_rightButton);
        _chrome.AddChild(_leftHost);
        _chrome.AddChild(_rightHost);

        RefreshPadding();
    }

    /// <summary>
    ///     tgui's <c>--mask-width</c>, 3.25em of the Tabs' 12px.
    /// </summary>
    public const float MaskWidth = 39f;

    /// <summary>
    ///     Where the fade is fully opaque, and where it starts to be: <c>transparent calc(var(--mask-left) * 0.66)</c> up to
    ///         <c>black var(--mask-left)</c>.
    /// </summary>
    public const float MaskOpaqueFraction = 0.66f;

    /// <summary>
    ///     tgui's <c>--space-s</c>, the padding inside the scrolling strip.
    /// </summary>
    private const float StripPadding = 3f;

    /// <summary>
    ///     A browser's wheel notch.
    /// </summary>
    private const float WheelNotch = 100f;

    private readonly ScrollChrome _chrome;
    private readonly KsTguiButton _leftButton;
    private readonly KsTguiButton _rightButton;
    private readonly Control _leftHost;
    private readonly Control _rightHost;

    private bool _scrollable;
    private float _scrollOffset;
    private float _scrollTarget;
    private float _viewportWidth;
    private float _contentWidth;
    private KsTguiTabItem? _centredTab;
    private bool _centredOnce;

    /// <summary>
    ///     Whether the strip behind the tabs is drawn: it is not inside a Section, whose own colour shows instead.
    /// </summary>
    public bool StripVisible => !_inSection;

    /// <summary>
    ///     The margin the tabs asked for itself, outside the 3px of padding the strip covers. For tests.
    /// </summary>
    public Thickness OuterMargin => new(
        _appliedMargin.Left - _padding.Left,
        _appliedMargin.Top - _padding.Top,
        _appliedMargin.Right - _padding.Right,
        _appliedMargin.Bottom - _padding.Bottom);

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
            if (child is not KsTguiTabItem { Visible: true })
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

        if (!_inSection)
        {
            // The padding is part of the strip; Margin holds both it and the gap outside.
            var scale = UIScale;
            var outset = Margin == _appliedMargin ? _padding : new Thickness(
                MathF.Min(_padding.Left, MathF.Max(0f, Margin.Left)),
                MathF.Min(_padding.Top, MathF.Max(0f, Margin.Top)),
                MathF.Min(_padding.Right, MathF.Max(0f, Margin.Right)),
                MathF.Min(_padding.Bottom, MathF.Max(0f, Margin.Bottom)));
            handle.DrawRect(
                new UIBox2(-outset.Left * scale, -outset.Top * scale, PixelWidth + outset.Right * scale, PixelHeight + outset.Bottom * scale),
                StripColor);
        }

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

    /// <summary>
    ///     tgui's <c>scrollable</c>: one row that scrolls sideways with no bar. It fades out at whichever edge has more, with
    ///         a button there to scroll by half the width, the wheel scrolls it, and the selected tab is brought to the middle.
    ///         Not for <see cref="Vertical"/> tabs, as tgui's.
    /// </summary>
    public bool Scrollable
    {
        get => _scrollable;
        set
        {
            if (_scrollable == value)
                return;

            _scrollable = value;
            RefreshScrollable();
        }
    }

    /// <summary>
    ///     Removes every tab. <c>RemoveAllChildren</c> would take the scroll fade and buttons of a <see cref="Scrollable"/>
    ///         strip with them.
    /// </summary>
    public void ClearTabs()
    {
        var tabs = new List<Control>();
        foreach (var child in Children)
        {
            if (child is KsTguiTabItem)
                tabs.Add(child);
        }

        foreach (var tab in tabs)
            RemoveChild(tab);
    }

    private bool Scrolling => _scrollable && !_vertical;

    public bool Vertical
    {
        get => _vertical;
        set
        {
            _vertical = value;
            Orientation = value ? LayoutOrientation.Vertical : LayoutOrientation.Horizontal;
            RefreshScrollable();
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
        // tgui: 3px around the tabs, except on the side they run into the content. A scrollable strip has none (padding: 0)
        //      and pads the tabs inside the scrolled area instead.
        _padding = _vertical ? new Thickness(3f, 3f, 0f, 3f) : Scrolling ? default : new Thickness(3f, 3f, 3f, 0f);
        RefreshMargin();
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

        RefreshMargin();
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
        RefreshMargin();
    }

    /// <summary>
    ///     The padding, plus tgui's margins: 6px under a row of tabs (none when it is the last child), and inside a Section
    ///         that is not fitted, -6px at the sides and, as the first child, at the top (<c>Tabs.scss</c>, "Interoperability
    ///         with sections"). A margin that something else set is left alone.
    /// </summary>
    private void RefreshMargin()
    {
        if (Margin != _appliedMargin && Margin != default)
            return;

        _inSection = false;
        var fittedOnly = true;
        for (var ancestor = Parent; ancestor != null; ancestor = ancestor.Parent)
        {
            if (ancestor is not KsTguiSection section)
                continue;

            _inSection = true;
            fittedOnly &= section.Fitted;
        }

        var isFirst = Parent is { ChildCount: > 0 } parent && parent.GetChild(0) == this;
        var isLast = Parent is { ChildCount: > 0 } parent2 && parent2.GetChild(parent2.ChildCount - 1) == this;

        var outer = default(Thickness);
        if (_inSection && !fittedOnly)
            outer = new Thickness(-6f, isFirst ? -6f : 0f, -6f, 6f);
        else if (!_vertical)
            outer = new Thickness(0f, 0f, 0f, isLast ? 0f : 6f);

        _appliedMargin = new Thickness(
            _padding.Left + outer.Left,
            _padding.Top + outer.Top,
            _padding.Right + outer.Right,
            _padding.Bottom + outer.Bottom);
        Margin = _appliedMargin;
    }

    protected override void ChildAdded(Control newChild)
    {
        base.ChildAdded(newChild);
        if (newChild is not KsTguiTabItem item)
            return;

        // Over the tabs, under nothing: the fade and the buttons come after them.
        if (_chrome.Parent == this)
            _chrome.SetPositionLast();

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

    /// <summary>
    ///     How far the strip is scrolled, for tests.
    /// </summary>
    public float ScrollOffset => _scrollOffset;

    /// <summary>
    ///     Where it is scrolling to; the smooth scroll of a button or a newly selected tab gets there over a few frames.
    /// </summary>
    public float ScrollTarget => _scrollTarget;

    /// <summary>
    ///     How much of the left edge fades out: tgui's <c>--mask-left</c>, set while there is more to the left.
    /// </summary>
    public float LeftFade => Scrolling && _scrollOffset > 0.01f ? MaskWidth : 0f;

    /// <summary>
    ///     How much of the right edge fades out: <c>--mask-right</c>, set while there is more to the right.
    /// </summary>
    public float RightFade => Scrolling && _scrollOffset + _viewportWidth < _contentWidth - 0.5f ? MaskWidth : 0f;

    public KsTguiButton ScrollLeftButton => _leftButton;

    public KsTguiButton ScrollRightButton => _rightButton;

    /// <summary>
    ///     Whether the scroll buttons are showing, for tests: left, then right.
    /// </summary>
    public (bool Left, bool Right) ScrollButtonsShown => (_leftHost.Visible, _rightHost.Visible);

    private void RefreshScrollable()
    {
        var scrolling = Scrolling;
        RectClipContent = scrolling;

        // A child only while scrolling, so that nothing counting or clearing the tabs meets it otherwise.
        if (scrolling && _chrome.Parent == null)
            AddChild(_chrome);
        else if (!scrolling && _chrome.Parent != null)
            RemoveChild(_chrome);

        if (!scrolling)
        {
            _scrollOffset = 0f;
            _scrollTarget = 0f;
            _centredTab = null;
            _centredOnce = false;
            _leftHost.Visible = false;
            _rightHost.Visible = false;
        }

        RefreshPadding();
        InvalidateMeasure();
    }

    private void ClampScroll()
    {
        var max = MathF.Max(0f, _contentWidth - _viewportWidth);
        _scrollOffset = Math.Clamp(_scrollOffset, 0f, max);
        _scrollTarget = Math.Clamp(_scrollTarget, 0f, max);
    }

    /// <summary>
    ///     tgui's scroll buttons: <c>scrollBy</c> half the visible width, smoothly.
    /// </summary>
    private void ScrollByHalf(float direction)
    {
        _scrollTarget = Math.Clamp(_scrollOffset + direction * 0.5f * _viewportWidth, 0f, MathF.Max(0f, _contentWidth - _viewportWidth));
        InvalidateArrange();
    }

    /// <summary>
    ///     The buttons show on a side while its fade does.
    /// </summary>
    private void UpdateScrollButtons()
    {
        var left = LeftFade > 0f;
        var right = RightFade > 0f;
        if (_leftHost.Visible != left)
            _leftHost.Visible = left;

        if (_rightHost.Visible != right)
            _rightHost.Visible = right;
    }

    /// <summary>
    ///     tgui's <c>scrollIntoView({ inline: 'center' })</c> of the selected tab, once there is something to scroll and each
    ///         time the selection moves. The first is immediate and later ones smooth.
    /// </summary>
    private void CentreSelected()
    {
        if (_viewportWidth <= 0f || _contentWidth <= _viewportWidth)
            return;

        KsTguiTabItem? selected = null;
        var x = StripPadding;
        var centre = 0f;
        foreach (var child in Children)
        {
            if (child is not KsTguiTabItem { Visible: true } tab)
                continue;

            if (tab.Selected && selected == null)
            {
                selected = tab;
                centre = x + tab.DesiredSize.X / 2f;
            }

            x += tab.DesiredSize.X;
        }

        if (selected == null || selected == _centredTab)
            return;

        _centredTab = selected;
        _scrollTarget = Math.Clamp(centre - _viewportWidth / 2f, 0f, _contentWidth - _viewportWidth);
        if (!_centredOnce)
            _scrollOffset = _scrollTarget;

        _centredOnce = true;
        InvalidateArrange();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (!Scrolling)
            return;

        CentreSelected();

        var difference = _scrollTarget - _scrollOffset;
        if (difference == 0f)
            return;

        // An ease-out, as a browser's smooth scroll.
        _scrollOffset = MathF.Abs(difference) < 0.5f ? _scrollTarget : _scrollOffset + difference * (1f - MathF.Exp(-15f * args.DeltaSeconds));
        InvalidateArrange();
    }

    /// <summary>
    ///     tgui's wheel handler: a wheel that moves mostly up and down scrolls the strip sideways, a notch at a time, at once.
    /// </summary>
    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);

        if (!Scrolling || _contentWidth <= _viewportWidth)
            return;

        var delta = MathF.Abs(args.Delta.Y) > MathF.Abs(args.Delta.X) ? -args.Delta.Y : args.Delta.X;
        _scrollOffset += delta * WheelNotch;
        ClampScroll();
        _scrollTarget = _scrollOffset;
        InvalidateArrange();
        args.Handle();
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        if (!Scrolling)
            return base.MeasureOverride(availableSize);

        var width = 0f;
        var height = 0f;
        foreach (var child in Children)
        {
            if (child is not KsTguiTabItem { Visible: true } tab)
                continue;

            tab.Measure(Vector2Helpers.Infinity);
            width += tab.DesiredSize.X;
            height = MathF.Max(height, tab.DesiredSize.Y);
        }

        _contentWidth = 2f * StripPadding + width;
        _chrome.Measure(availableSize);

        // The strip can be given less than the tabs need; that is what scrolling is for.
        return new Vector2(MathF.Min(_contentWidth, availableSize.X), StripPadding + height);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        if (!Scrolling)
            return base.ArrangeOverride(finalSize);

        _viewportWidth = finalSize.X;
        ClampScroll();

        var x = StripPadding - _scrollOffset;
        foreach (var child in Children)
        {
            if (child is not KsTguiTabItem { Visible: true } tab)
                continue;

            tab.Arrange(UIBox2.FromDimensions(x, StripPadding, tab.DesiredSize.X, MathF.Max(0f, finalSize.Y - StripPadding)));
            x += tab.DesiredSize.X;
        }

        UpdateScrollButtons();
        _chrome.Arrange(UIBox2.FromDimensions(Vector2.Zero, finalSize));
        return finalSize;
    }

    /// <summary>
    ///     What sits over the scrolled tabs: the fade at each edge, and above that the scroll buttons. The fade is the strip's
    ///         colour at the mask's alpha, which is what tgui's <c>mask-image</c> lets show through.
    /// </summary>
    private sealed class ScrollChrome : Control
    {
        private readonly KsTguiTabs _owner;
        private readonly DrawVertexUV2DColor[] _vertices = new DrawVertexUV2DColor[6];

        public ScrollChrome(KsTguiTabs owner)
        {
            _owner = owner;
            MouseFilter = MouseFilterMode.Ignore;
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            foreach (var child in Children)
                child.Measure(Vector2Helpers.Infinity);

            return Vector2.Zero;
        }

        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            // position: absolute; top: 0; height: 100% at the left and the right.
            var left = _owner._leftHost;
            var right = _owner._rightHost;
            var leftWidth = left.DesiredSize.X;
            var rightWidth = right.DesiredSize.X;
            left.Arrange(UIBox2.FromDimensions(0f, 0f, leftWidth, finalSize.Y));
            right.Arrange(UIBox2.FromDimensions(finalSize.X - rightWidth, 0f, rightWidth, finalSize.Y));
            return finalSize;
        }

        protected override void Draw(DrawingHandleScreen handle)
        {
            var scale = UIScale;
            var colour = _owner._inSection ? KsTguiPalettes.Section : StripColor;
            var width = PixelWidth;
            var height = PixelHeight;

            var left = _owner.LeftFade * scale;
            if (left > 0f)
            {
                var opaque = left * MaskOpaqueFraction;
                handle.DrawRect(new UIBox2(0f, 0f, opaque, height), colour);
                DrawRamp(handle, opaque, left, height, colour, 1f, 0f);
            }

            var right = _owner.RightFade * scale;
            if (right > 0f)
            {
                var opaque = right * MaskOpaqueFraction;
                handle.DrawRect(new UIBox2(width - opaque, 0f, width, height), colour);
                DrawRamp(handle, width - right, width - opaque, height, colour, 0f, 1f);
            }
        }

        private void DrawRamp(DrawingHandleScreen handle, float x0, float x1, float height, Color colour, float alpha0, float alpha1)
        {
            var start = colour.WithAlpha(alpha0);
            var end = colour.WithAlpha(alpha1);
            _vertices[0] = new DrawVertexUV2DColor(new Vector2(x0, 0f), start);
            _vertices[1] = new DrawVertexUV2DColor(new Vector2(x1, 0f), end);
            _vertices[2] = new DrawVertexUV2DColor(new Vector2(x1, height), end);
            _vertices[3] = new DrawVertexUV2DColor(new Vector2(x0, 0f), start);
            _vertices[4] = new DrawVertexUV2DColor(new Vector2(x1, height), end);
            _vertices[5] = new DrawVertexUV2DColor(new Vector2(x0, height), start);
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, Texture.White, _vertices);
        }
    }
}
