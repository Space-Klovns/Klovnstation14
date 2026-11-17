using System.Numerics;
using Robust.Client.Graphics;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     The measurements of one size of tgui's scrollbar, which is Chromium's own with <c>scrollbar-color</c> set, as measured
///         from tg screenshots at 100%. A square cell at each end of the column holds a triangle; the thumb is a pill centred
///         in the column, kept out of the cells by a small gap.
/// </summary>
public sealed class KsTguiScrollBarMetrics
{
    /// <summary>
    ///     The ordinary bar: 15px across, a 9px thumb, and 9 by 5 arrows. Measured from tg's ChemMaster.
    /// </summary>
    public static readonly KsTguiScrollBarMetrics Normal = new(
        barWidth: 15f,
        thumbWidth: 9f,
        arrowGap: 2f,
        arrowBase: 9f,
        arrowRows: 5,
        startTipInset: 6f,
        endTipInset: 7f);

    /// <summary>
    ///     <c>scrollbar-width: thin</c>, as tgui's text area has: 10px across, a 6px thumb, and 6 by 3 arrows. Measured from
    ///         tg's Adminhelp window.
    /// </summary>
    public static readonly KsTguiScrollBarMetrics Thin = new(
        barWidth: 10f,
        thumbWidth: 6f,
        arrowGap: 1f,
        arrowBase: 6f,
        arrowRows: 3,
        startTipInset: 4f,
        endTipInset: 4f);

    private KsTguiScrollBarMetrics(
        float barWidth,
        float thumbWidth,
        float arrowGap,
        float arrowBase,
        int arrowRows,
        float startTipInset,
        float endTipInset)
    {
        BarWidth = barWidth;
        ThumbWidth = thumbWidth;
        ArrowGap = arrowGap;
        ArrowBase = arrowBase;
        ArrowRows = arrowRows;
        StartTipInset = startTipInset;
        EndTipInset = endTipInset;
    }

    /// <summary>
    ///     Across the column.
    /// </summary>
    public float BarWidth { get; }

    /// <summary>
    ///     Across the thumb, which leaves an equal gap either side of it.
    /// </summary>
    public float ThumbWidth { get; }

    /// <summary>
    ///     Each arrow's cell is a square as wide as the column.
    /// </summary>
    public float ArrowCell => BarWidth;

    /// <summary>
    ///     Between an arrow's cell and the thumb's furthest reach.
    /// </summary>
    public float ArrowGap { get; }

    /// <summary>
    ///     The width of the arrow's widest row. Each row towards the tip is 2px narrower: 9 over 5 rows is 1, 3, 5, 7 and 9
    ///         pixels, and 6 over 3 rows is 2, 4 and 6.
    /// </summary>
    public float ArrowBase { get; }

    /// <summary>
    ///     How many rows of pixels the arrow has.
    /// </summary>
    public int ArrowRows { get; }

    /// <summary>
    ///     The width of a row of the arrow, counted from the tip.
    /// </summary>
    public float ArrowWidth(int row)
    {
        return ArrowBase - 2f * (ArrowRows - 1 - row);
    }

    /// <summary>
    ///     How far the start (top or left) arrow's tip is from that end of the column. Chromium sits each triangle a pixel
    ///         or so off its cell's centre, towards the thumb, and not quite symmetrically.
    /// </summary>
    public float StartTipInset { get; }

    /// <summary>
    ///     How far the end (bottom or right) arrow's tip is from that end of the column.
    /// </summary>
    public float EndTipInset { get; }

    /// <summary>
    ///     How far the bar is pulled in from each end of its column, so that the arrows have room outside it.
    /// </summary>
    public float EndInset => ArrowCell + ArrowGap;

    /// <summary>
    ///     The arrow cell at the start (top or left) or end of a bar, in the bar's own pixel box.
    /// </summary>
    public UIBox2 Cell(UIBox2 bar, bool horizontal, bool end, float scale)
    {
        var inset = EndInset * scale;
        var cell = ArrowCell * scale;
        var gap = ArrowGap * scale;

        if (horizontal)
        {
            return end
                ? UIBox2.FromDimensions(new Vector2(bar.Right + gap, bar.Top), new Vector2(cell, bar.Height))
                : UIBox2.FromDimensions(new Vector2(bar.Left - inset, bar.Top), new Vector2(cell, bar.Height));
        }

        return end
            ? UIBox2.FromDimensions(new Vector2(bar.Left, bar.Bottom + gap), new Vector2(bar.Width, cell))
            : UIBox2.FromDimensions(new Vector2(bar.Left, bar.Top - inset), new Vector2(bar.Width, cell));
    }

    /// <summary>
    ///     The whole column, cells included: the bar's box grown by <see cref="EndInset"/> at each end, on whole pixels.
    /// </summary>
    public UIBox2 Column(UIBox2 bar, bool horizontal, float scale)
    {
        var inset = EndInset * scale;
        return Snap(horizontal
            ? new UIBox2(bar.Left - inset, bar.Top, bar.Right + inset, bar.Bottom)
            : new UIBox2(bar.Left, bar.Top - inset, bar.Right, bar.Bottom + inset));
    }

    /// <summary>
    ///     A box moved onto whole pixels. Layout can leave a bar half a pixel over, which turns tg's solid 1, 3, 5, 7, 9 pixel
    ///         arrow rows into grey-edged 2, 4, 6, 8, 10 ones and blurs the thumb's sides.
    /// </summary>
    public static UIBox2 Snap(UIBox2 box)
    {
        // The corner is rounded and the size kept, as rounding both edges can grow a 15px bar to 16 (.5 rounds to even).
        var left = MathF.Floor(box.Left + 0.5f);
        var top = MathF.Floor(box.Top + 0.5f);
        return UIBox2.FromDimensions(left, top, MathF.Round(box.Width), MathF.Round(box.Height));
    }
}

/// <summary>
///     What every size of tgui's scrollbar shares.
/// </summary>
public static class KsTguiScrollBarGeometry
{
    public const float ThumbMinLength = 20f;

    /// <summary>
    ///     How far one arrow press scrolls, a browser's line.
    /// </summary>
    public const float ArrowStep = 40f;

    public const float RepeatDelay = 0.25f;
    public const float RepeatInterval = 0.05f;
}

/// <summary>
///     The thumb: a pill as thick as its metrics say, in the middle of the column, whatever length the engine gives it.
/// </summary>
public sealed class KsTguiScrollThumbStyleBox : StyleBox
{
    private readonly KsTguiStyleBox _pill;
    private readonly bool _horizontal;
    private readonly KsTguiScrollBarMetrics _metrics;

    public KsTguiScrollThumbStyleBox(Color color, bool horizontal, bool thin = false)
    {
        _horizontal = horizontal;
        _metrics = thin ? KsTguiScrollBarMetrics.Thin : KsTguiScrollBarMetrics.Normal;
        _pill = new KsTguiStyleBox(color, _metrics.ThumbWidth / 2f);

        // The engine reads the bar's own size, and the thumb's shortest length, from these.
        if (horizontal)
        {
            SetContentMarginOverride(Margin.Top, _metrics.BarWidth);
            SetContentMarginOverride(Margin.Left, KsTguiScrollBarGeometry.ThumbMinLength);
        }
        else
        {
            SetContentMarginOverride(Margin.Left, _metrics.BarWidth);
            SetContentMarginOverride(Margin.Top, KsTguiScrollBarGeometry.ThumbMinLength);
        }
    }

    public Color Color => _pill.BackgroundColor;

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        _pill.Draw(handle, ThumbBox(KsTguiScrollBarMetrics.Snap(box), uiScale, _horizontal, _metrics), uiScale);
    }

    /// <summary>
    ///     The pill inside the box the engine gives the thumb: the metrics' thumb width across, centred in the bar.
    /// </summary>
    public static UIBox2 ThumbBox(UIBox2 box, float uiScale, bool horizontal, KsTguiScrollBarMetrics metrics)
    {
        var inset = (metrics.BarWidth - metrics.ThumbWidth) / 2f * uiScale;

        // A bar laid out narrower than its style asks (a dropdown's list, mid-layout) would invert the box; the thumb then
        //      just fills what there is.
        var across = horizontal ? box.Height : box.Width;
        inset = MathF.Max(0f, MathF.Min(inset, (across - metrics.ThumbWidth * uiScale) / 2f));

        return horizontal
            ? new UIBox2(box.Left, box.Top + inset, box.Right, box.Bottom - inset)
            : new UIBox2(box.Left + inset, box.Top, box.Right - inset, box.Bottom);
    }
}

/// <summary>
///     What is drawn behind the thumb: the column's track, and the arrows in the room the bar was pulled in to leave. Both
///         reach outside the box this is given, which a scroll container doesn't clip.
/// </summary>
/// <remarks>
///     tgui's track is <c>--color-scrollbar-base</c>, the Section colour, everywhere but inside a <c>Section</c>, a text area
///         or a dropdown, which set it transparent. With nothing to scroll the track and arrows stay as they are; only the
///         thumb goes.
/// </remarks>
public sealed class KsTguiScrollTrackStyleBox : StyleBox
{
    private readonly bool _horizontal;
    private readonly Color _arrowColor;
    private readonly Color? _trackColor;

    /// <param name="arrowColor">The arrows' colour, which is the thumb's.</param>
    /// <param name="horizontal">Whether this is a horizontal bar.</param>
    /// <param name="thin">Whether this is <c>scrollbar-width: thin</c>.</param>
    /// <param name="trackColor">The column's colour, or null for a transparent one.</param>
    public KsTguiScrollTrackStyleBox(Color arrowColor, bool horizontal, bool thin = false, Color? trackColor = null)
    {
        _arrowColor = arrowColor;
        _horizontal = horizontal;
        _trackColor = trackColor;
        Metrics = thin ? KsTguiScrollBarMetrics.Thin : KsTguiScrollBarMetrics.Normal;
    }

    public KsTguiScrollBarMetrics Metrics { get; }

    public Color ArrowColor => _arrowColor;

    public Color? TrackColor => _trackColor;

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        if (_trackColor is { } track)
            handle.DrawRect(Metrics.Column(box, _horizontal, uiScale), track * handle.Modulate);

        DrawArrow(handle, box, end: false, uiScale);
        DrawArrow(handle, box, end: true, uiScale);
    }

    /// <summary>
    ///     The arrow as rows of whole pixels, from the tip: Chromium's are solid, and a triangle's edges come out
    ///         antialiased, grey-edged and a pixel wider.
    /// </summary>
    private void DrawArrow(DrawingHandleScreen handle, UIBox2 bar, bool end, float uiScale)
    {
        var column = Metrics.Column(bar, _horizontal, uiScale);
        var color = _arrowColor * handle.Modulate;

        var middle = _horizontal ? column.Center.Y : column.Center.X;
        var start = _horizontal ? column.Left : column.Top;
        var finish = _horizontal ? column.Right : column.Bottom;

        // The tip's row, and which way the rows go from it: the start arrow points back towards the start.
        var tip = end
            ? finish - Metrics.EndTipInset * uiScale - uiScale
            : start + Metrics.StartTipInset * uiScale;
        var step = end ? -uiScale : uiScale;

        for (var row = 0; row < Metrics.ArrowRows; row++)
        {
            var width = Metrics.ArrowWidth(row) * uiScale;
            var along = tip + row * step;
            var box = _horizontal
                ? new UIBox2(along, middle - width / 2f, along + uiScale, middle + width / 2f)
                : new UIBox2(middle - width / 2f, along, middle + width / 2f, along + uiScale);
            handle.DrawRect(box, color);
        }
    }
}
