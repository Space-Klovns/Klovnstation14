using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     A <see cref="ScrollContainer"/> laid out as a browser lays out a scrolled box. Its bars take their own 15px column (or
///         row) and the content is measured and arranged in what is left, so text wraps at the width it is drawn at.
/// </summary>
/// <remarks>
///     <para>
///         tgui has two kinds of scrolled box. <c>overflow-y: auto</c> (<c>Section scrollable</c>, an
///             <c>overflowY="auto"</c> stack item) shows the bar, and takes its column, only while the content overflows.
///             That is the default here. <c>overflow-y: scroll</c> (<c>Window.Content scrollable</c>, which is
///             <c>.Layout__content--scrollable</c>) always takes the column and draws the bar's track and arrows even with
///             nothing to scroll: set <see cref="ReserveVertical"/> for that. A bar with nothing to scroll has no thumb and
///             dimmed arrows (<see cref="StyleClassDisabled"/>) and does not take the wheel, so it scrolls the box behind it,
///             as a browser's does.
///     </para>
///     <para>
///         The engine's own container leaves its content measured at one width and arranged at another when the bar comes
///             and goes. This one settles the bars first, then measures the content once, at the width it is arranged at.
///             It replaces the engine's layout completely, and only the scroll values and the bars are shared with it.
///     </para>
///     <para>
///         Scrolled content is not clipped at the bars' column or row: it runs under them, as the engine's does.
///     </para>
/// </remarks>
public sealed class KsTguiScrollContainer : ScrollContainer
{
    /// <summary>
    ///     On a bar that has nothing to scroll but is shown anyway (<see cref="ReserveVertical"/>).
    /// </summary>
    public const string StyleClassDisabled = "KsTguiScrollBarDisabled";

    /// <summary>
    ///     How much taller than its room the content has to be to overflow, as the engine's container counts it.
    /// </summary>
    private const float Epsilon = 1e-3f;

    private readonly VScrollBar _verticalBar;
    private readonly HScrollBar _horizontalBar;

    private bool _reserveVertical;
    private bool _reserveHorizontal;

    private bool _hasLayout;
    private Vector2 _layoutFor;
    private Layout _layout;

    private bool _verticalDisabled;
    private bool _horizontalDisabled;

    /// <summary>
    ///     Where the bars and the content sit for one size.
    /// </summary>
    private struct Layout
    {
        public Vector2 Content;
        public Vector2 Viewport;
        public bool ShowVertical;
        public bool ShowHorizontal;
        public bool OverflowVertical;
        public bool OverflowHorizontal;
        public float VerticalThickness;
        public float HorizontalThickness;
    }

    public KsTguiScrollContainer()
    {
        // Bars measure at their real size whether or not they are showing, so the thickness is known before they appear.
        ReserveScrollbarSpace = true;

        VScrollBar? vertical = null;
        HScrollBar? horizontal = null;
        foreach (var child in Children)
        {
            if (child is VScrollBar v)
                vertical = v;
            else if (child is HScrollBar h)
                horizontal = h;
        }

        _verticalBar = vertical!;
        _horizontalBar = horizontal!;
    }

    /// <summary>
    ///     tgui's <c>overflow-y: scroll</c>: the vertical bar is always there, taking its column.
    /// </summary>
    public bool ReserveVertical
    {
        get => _reserveVertical;
        set
        {
            if (_reserveVertical == value)
                return;

            _reserveVertical = value;
            InvalidateMeasure();
        }
    }

    /// <summary>
    ///     tgui's <c>overflow-x: scroll</c>: the horizontal bar is always there, taking its row.
    /// </summary>
    public bool ReserveHorizontal
    {
        get => _reserveHorizontal;
        set
        {
            if (_reserveHorizontal == value)
                return;

            _reserveHorizontal = value;
            InvalidateMeasure();
        }
    }

    /// <summary>
    ///     Whether the vertical bar is showing, and with room to scroll.
    /// </summary>
    public bool VerticalBarShown => _hasLayout && _layout.ShowVertical;

    public bool HorizontalBarShown => _hasLayout && _layout.ShowHorizontal;

    /// <summary>
    ///     Whether the content is taller than the room it is given, as of the last arrange.
    /// </summary>
    public bool VerticalOverflows => _hasLayout && _layout.OverflowVertical;

    public bool HorizontalOverflows => _hasLayout && _layout.OverflowHorizontal;

    /// <summary>
    ///     The room the content is arranged in, less the bars: what a browser calls the client size.
    /// </summary>
    public Vector2 ViewportSize => _hasLayout ? _layout.Viewport : Vector2.Zero;

    private Layout Compute(Vector2 available)
    {
        var layout = default(Layout);

        _verticalBar.Measure(Vector2Helpers.Infinity);
        _horizontalBar.Measure(Vector2Helpers.Infinity);
        layout.VerticalThickness = VScrollEnabled ? _verticalBar.DesiredSize.X : 0f;
        layout.HorizontalThickness = HScrollEnabled ? _horizontalBar.DesiredSize.Y : 0f;

        layout.ShowVertical = VScrollEnabled && _reserveVertical;
        layout.ShowHorizontal = HScrollEnabled && _reserveHorizontal;

        // A bar that appears narrows the content, which can make it taller, so settle them together. Two rounds are enough:
        //      each bar can only appear once.
        for (var round = 0; round < 3; round++)
        {
            layout.Viewport = new Vector2(
                MathF.Max(0f, available.X - (layout.ShowVertical ? layout.VerticalThickness : 0f)),
                MathF.Max(0f, available.Y - (layout.ShowHorizontal ? layout.HorizontalThickness : 0f)));

            var constraint = new Vector2(
                HScrollEnabled ? float.PositiveInfinity : layout.Viewport.X,
                VScrollEnabled ? float.PositiveInfinity : layout.Viewport.Y);

            layout.Content = Vector2.Zero;
            foreach (var child in Children)
            {
                if (child == _verticalBar || child == _horizontalBar)
                    continue;

                child.Measure(constraint);
                layout.Content = Vector2.Max(layout.Content, child.DesiredSize);
            }

            var needVertical = VScrollEnabled && !layout.ShowVertical && layout.Content.Y > layout.Viewport.Y + Epsilon;
            var needHorizontal = HScrollEnabled && !layout.ShowHorizontal && layout.Content.X > layout.Viewport.X + Epsilon;
            if (!needVertical && !needHorizontal)
                break;

            layout.ShowVertical |= needVertical;
            layout.ShowHorizontal |= needHorizontal;
        }

        layout.OverflowVertical = VScrollEnabled && layout.Content.Y > layout.Viewport.Y + Epsilon;
        layout.OverflowHorizontal = HScrollEnabled && layout.Content.X > layout.Viewport.X + Epsilon;
        return layout;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        _layout = Compute(availableSize);
        _layoutFor = availableSize;
        _hasLayout = true;

        // Like the engine's, this reports nothing unless asked to: a scroll box has no size of its own.
        if (!ReturnMeasure)
            return Vector2.Zero;

        return _layout.Content + new Vector2(
            _layout.ShowVertical ? _layout.VerticalThickness : 0f,
            _layout.ShowHorizontal ? _layout.HorizontalThickness : 0f);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        if (!_hasLayout || _layoutFor != finalSize)
        {
            _layout = Compute(finalSize);
            _layoutFor = finalSize;
            _hasLayout = true;
        }

        var layout = _layout;

        // The engine keeps its bar-hiding style properties private, so read them as it does.
        var verticalHidden = VScrollBarHidden ?? (TryGetStyleProperty<bool>(nameof(VScrollBarHidden), out var vHidden) && vHidden);
        var horizontalHidden = HScrollBarHidden ?? (TryGetStyleProperty<bool>(nameof(HScrollBarHidden), out var hHidden) && hHidden);

        UpdateBar(_verticalBar, layout.ShowVertical, verticalHidden, layout.OverflowVertical, layout.Viewport.Y, layout.Content.Y, ref _verticalDisabled);
        UpdateBar(_horizontalBar, layout.ShowHorizontal, horizontalHidden, layout.OverflowHorizontal, layout.Viewport.X, layout.Content.X, ref _horizontalDisabled);

        if (layout.ShowVertical)
        {
            _verticalBar.Arrange(UIBox2.FromDimensions(
                Vector2.Zero,
                new Vector2(finalSize.X, MathF.Max(0f, finalSize.Y - (layout.ShowHorizontal ? layout.HorizontalThickness : 0f)))));
        }

        if (layout.ShowHorizontal)
        {
            _horizontalBar.Arrange(UIBox2.FromDimensions(
                Vector2.Zero,
                new Vector2(MathF.Max(0f, finalSize.X - (layout.ShowVertical ? layout.VerticalThickness : 0f)), finalSize.Y)));
        }

        var contentSize = new Vector2(
            HScrollEnabled ? MathF.Max(layout.Content.X, layout.Viewport.X) : layout.Viewport.X,
            VScrollEnabled ? MathF.Max(layout.Content.Y, layout.Viewport.Y) : layout.Viewport.Y);
        var position = -GetScrollValue();

        foreach (var child in Children)
        {
            if (child == _verticalBar || child == _horizontalBar)
                continue;

            child.Arrange(UIBox2.FromDimensions(position, contentSize));
        }

        return finalSize;
    }

    /// <summary>
    ///     Shows or hides a bar, gives it the room it scrolls, and dims it when there is nothing to scroll.
    /// </summary>
    private static void UpdateBar(ScrollBar bar, bool shown, bool hidden, bool overflows, float page, float max, ref bool disabled)
    {
        var visible = shown && !hidden;
        if (bar.Visible != visible)
            bar.Visible = visible;

        if (!shown)
            return;

        var newMax = overflows ? max : page;
        if (!MathHelper.CloseToPercent(bar.MaxValue, newMax) || !MathHelper.CloseToPercent(bar.Page, page))
        {
            // The value clamps to MaxValue - Page whichever setter runs, so order them to not lose the scroll position
            //      on the way to the new range: clamping to a wider range first and the final one last is the same as
            //      clamping to the final one.
            bar.Page = 0f;
            bar.MaxValue = newMax;
            bar.Page = page;
        }

        var nowDisabled = !overflows;
        if (nowDisabled == disabled)
            return;

        disabled = nowDisabled;
        if (nowDisabled)
        {
            bar.AddStyleClass(StyleClassDisabled);
            bar.MouseFilter = Control.MouseFilterMode.Ignore;
        }
        else
        {
            bar.RemoveStyleClass(StyleClassDisabled);
            bar.MouseFilter = Control.MouseFilterMode.Pass;
        }
    }

    /// <summary>
    ///     How far the content is scrolled. Nothing while it fits, as a browser has it.
    /// </summary>
    public new Vector2 GetScrollValue(bool ignoreVisible = false)
    {
        if (ignoreVisible)
            return new Vector2(_horizontalBar.Value, _verticalBar.Value);

        return new Vector2(
            _hasLayout && _layout.OverflowHorizontal ? _horizontalBar.Value : 0f,
            _hasLayout && _layout.OverflowVertical ? _verticalBar.Value : 0f);
    }

    /// <summary>
    ///     Whether the bars' column is drawn in the Section colour: true unless the container is inside a Section, as tgui's
    ///         <c>scrollbar-color</c> is inherited from the page and only a Section sets its track transparent.
    /// </summary>
    public bool FilledTrack => _verticalBar.HasStyleClass(KsTguiSheetlet.StyleClassFilledScrollBar);

    protected override void EnteredTree()
    {
        base.EnteredTree();

        var filled = !InsideSection();
        SetFilled(_verticalBar, filled);
        SetFilled(_horizontalBar, filled);
    }

    private static void SetFilled(ScrollBar bar, bool filled)
    {
        if (filled)
            bar.AddStyleClass(KsTguiSheetlet.StyleClassFilledScrollBar);
        else
            bar.RemoveStyleClass(KsTguiSheetlet.StyleClassFilledScrollBar);
    }

    private bool InsideSection()
    {
        for (var parent = Parent; parent != null; parent = parent.Parent)
        {
            if (parent is KsSectionPanel || parent.HasStyleClass(KsSectionPanel.StyleClassSectionPanel))
                return true;
        }

        return false;
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        // Not the engine's, which reads layout state this container never sets.
        if (VScrollEnabled)
            _verticalBar.ValueTarget -= args.Delta.Y * ScrollSpeedY;

        if (HScrollEnabled)
        {
            var delta = args.Delta.X == 0f && !VScrollEnabled && FallbackDeltaScroll ? -args.Delta.Y : args.Delta.X;
            _horizontalBar.ValueTarget += delta * ScrollSpeedX;
        }

        // Nothing to scroll: the wheel goes on to whatever is behind, as a browser's does.
        if (!_hasLayout || (!_layout.OverflowVertical && !_layout.OverflowHorizontal))
            return;

        args.Handle();
    }
}
