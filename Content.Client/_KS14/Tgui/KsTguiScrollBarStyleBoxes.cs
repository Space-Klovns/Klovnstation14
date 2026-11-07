using System.Numerics;
using Robust.Client.Graphics;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     The measurements of tgui's scrollbar, which is the browser's own with <c>scrollbar-color</c> set: 15px across, a pill
///         inset 3px each side, and a 15px square at each end holding a triangle.
/// </summary>
public static class KsTguiScrollBarGeometry
{
    public const float BarWidth = 15f;
    public const float ThumbWidth = 9f;

    /// <summary>
    ///     <c>scrollbar-width: thin</c>, as tgui's text area has: a narrower bar with no arrows, its thumb keeping the
    ///         normal bar's 3px either side.
    /// </summary>
    public const float ThinBarWidth = 11f;
    public const float ThinThumbWidth = ThinBarWidth - (BarWidth - ThumbWidth);
    public const float ThumbMinLength = 20f;

    /// <summary>
    ///     Each arrow's cell is a square this big.
    /// </summary>
    public const float ArrowCell = 15f;

    /// <summary>
    ///     Between an arrow's cell and the thumb's furthest reach.
    /// </summary>
    public const float ArrowGap = 2f;

    /// <summary>
    ///     How far the bar is pulled in from each end of its scroll container, so that the arrows have room outside it.
    /// </summary>
    public const float EndInset = ArrowCell + ArrowGap;

    /// <summary>
    ///     A triangle 9 rows wide at its base and 5 tall, whose rows are 1, 3, 5, 7, 9 pixels.
    /// </summary>
    public const float ArrowHeight = 5f;
    public const float ArrowBase = 10f;

    /// <summary>
    ///     How far one arrow press scrolls, a browser's line.
    /// </summary>
    public const float ArrowStep = 40f;

    public const float RepeatDelay = 0.25f;
    public const float RepeatInterval = 0.05f;

    /// <summary>
    ///     The arrow cell at the start (top or left) or end of a bar, in the bar's own pixel box.
    /// </summary>
    public static UIBox2 Cell(UIBox2 bar, bool horizontal, bool end, float scale)
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
}

/// <summary>
///     The thumb: a pill 9px thick in the middle of the bar's 15px, whatever length the engine gives it.
/// </summary>
public sealed class KsTguiScrollThumbStyleBox : StyleBox
{
    private readonly KsTguiStyleBox _pill;
    private readonly bool _horizontal;
    private readonly float _barWidth;
    private readonly float _thumbWidth;

    public KsTguiScrollThumbStyleBox(Color color, bool horizontal, bool thin = false)
    {
        _horizontal = horizontal;
        _barWidth = thin ? KsTguiScrollBarGeometry.ThinBarWidth : KsTguiScrollBarGeometry.BarWidth;
        _thumbWidth = thin ? KsTguiScrollBarGeometry.ThinThumbWidth : KsTguiScrollBarGeometry.ThumbWidth;
        _pill = new KsTguiStyleBox(color, _thumbWidth / 2f);

        // The engine reads the bar's own size, and the thumb's shortest length, from these.
        if (horizontal)
        {
            SetContentMarginOverride(Margin.Top, _barWidth);
            SetContentMarginOverride(Margin.Left, KsTguiScrollBarGeometry.ThumbMinLength);
        }
        else
        {
            SetContentMarginOverride(Margin.Left, _barWidth);
            SetContentMarginOverride(Margin.Top, KsTguiScrollBarGeometry.ThumbMinLength);
        }
    }

    public Color Color => _pill.BackgroundColor;

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        _pill.Draw(handle, ThumbBox(box, uiScale, _horizontal, _barWidth, _thumbWidth), uiScale);
    }

    /// <summary>
    ///     The pill inside the box the engine gives the thumb: <see cref="KsTguiScrollBarGeometry.ThumbWidth"/> across,
    ///         centred in the bar.
    /// </summary>
    public static UIBox2 ThumbBox(UIBox2 box, float uiScale, bool horizontal,
        float barWidth = KsTguiScrollBarGeometry.BarWidth, float thumbWidth = KsTguiScrollBarGeometry.ThumbWidth)
    {
        var inset = (barWidth - thumbWidth) / 2f * uiScale;

        // A bar laid out narrower than its style asks (a dropdown's list, mid-layout) would invert the box; the thumb then
        //      just fills what there is.
        var across = horizontal ? box.Height : box.Width;
        inset = MathF.Max(0f, MathF.Min(inset, (across - thumbWidth * uiScale) / 2f));

        return horizontal
            ? new UIBox2(box.Left, box.Top + inset, box.Right, box.Bottom - inset)
            : new UIBox2(box.Left + inset, box.Top, box.Right - inset, box.Bottom);
    }
}

/// <summary>
///     What is drawn behind the thumb: nothing in the bar itself, as tgui's track is transparent, and the arrows in the
///         room the bar was pulled in to leave. They are drawn outside the box this is given, which a scroll container
///         doesn't clip.
/// </summary>
public sealed class KsTguiScrollTrackStyleBox : StyleBox
{
    private readonly bool _horizontal;
    private readonly Color _color;
    private readonly DrawVertexUV2DColor[] _vertices = new DrawVertexUV2DColor[3];

    public KsTguiScrollTrackStyleBox(Color color, bool horizontal)
    {
        _color = color;
        _horizontal = horizontal;
    }

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        DrawArrow(handle, KsTguiScrollBarGeometry.Cell(box, _horizontal, end: false, uiScale), pointsToEnd: false, uiScale);
        DrawArrow(handle, KsTguiScrollBarGeometry.Cell(box, _horizontal, end: true, uiScale), pointsToEnd: true, uiScale);
    }

    private void DrawArrow(DrawingHandleScreen handle, UIBox2 cell, bool pointsToEnd, float uiScale)
    {
        var height = KsTguiScrollBarGeometry.ArrowHeight * uiScale;
        var halfBase = KsTguiScrollBarGeometry.ArrowBase / 2f * uiScale;
        var color = _color * handle.Modulate;
        var center = cell.Center;

        // Across the cell and along it, then which way is "forward".
        var along = _horizontal ? new Vector2(1f, 0f) : new Vector2(0f, 1f);
        var across = _horizontal ? new Vector2(0f, 1f) : new Vector2(1f, 0f);
        var direction = pointsToEnd ? along : -along;

        // The triangle is as tall as its tip is far from its base, and sits in the middle of the cell.
        var tip = center + direction * (height / 2f);
        var baseCenter = center - direction * (height / 2f);

        _vertices[0] = new DrawVertexUV2DColor(tip, color);
        _vertices[1] = new DrawVertexUV2DColor(baseCenter + across * halfBase, color);
        _vertices[2] = new DrawVertexUV2DColor(baseCenter - across * halfBase, color);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, Texture.White, _vertices);
    }
}
