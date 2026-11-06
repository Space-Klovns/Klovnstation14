using System.Numerics;
using Robust.Client.Graphics;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     A flat box with tgui's rounded corners and per-side borders. No engine stylebox has a corner radius.
/// </summary>
public sealed class KsTguiStyleBox : StyleBox
{
    /// <summary>
    ///     How far the corners' soft edge reaches either side of the true outline, in device pixels.
    /// </summary>
    private const float CornerSoftness = 0.5f;

    public Color BackgroundColor = Color.White;
    public Color BorderColor;

    /// <summary>
    ///     Border widths in virtual pixels. Like CSS, the background runs underneath the border.
    /// </summary>
    public Thickness BorderThickness;

    public float RadiusTopLeft;
    public float RadiusTopRight;
    public float RadiusBottomRight;
    public float RadiusBottomLeft;

    private DrawVertexUV2DColor[] _vertices = [];
    private Vector2[] _solidLoop = [];
    private Vector2[] _softLoop = [];
    private Vector2[] _innerLoop = [];

    public KsTguiStyleBox()
    {
    }

    public KsTguiStyleBox(Color backgroundColor, float radius = 0f)
    {
        BackgroundColor = backgroundColor;
        Radius = radius;
    }

    /// <summary>
    ///     Sets all four corners.
    /// </summary>
    public float Radius
    {
        set
        {
            RadiusTopLeft = value;
            RadiusTopRight = value;
            RadiusBottomRight = value;
            RadiusBottomLeft = value;
        }
    }

    private bool IsSquare => RadiusTopLeft <= 0f && RadiusTopRight <= 0f && RadiusBottomRight <= 0f && RadiusBottomLeft <= 0f;

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        if (box.Width <= 0f || box.Height <= 0f)
            return;

        var thickness = BorderThickness.Scale(uiScale);
        var hasBorder = BorderColor.A > 0f
            && (thickness.Left > 0f || thickness.Top > 0f || thickness.Right > 0f || thickness.Bottom > 0f);

        if (IsSquare)
        {
            DrawSquare(handle, box, thickness, hasBorder);
            return;
        }

        var largestRadius = MathF.Max(
            MathF.Max(RadiusTopLeft, RadiusTopRight),
            MathF.Max(RadiusBottomRight, RadiusBottomLeft)) * uiScale;
        var segments = largestRadius <= 4f ? 3 : 6;
        var loopLength = KsTguiGeometry.RoundedLoopLength(segments);

        EnsureCapacity(loopLength);

        KsTguiGeometry.RoundedLoop(box, RadiusTopLeft * uiScale, RadiusTopRight * uiScale, RadiusBottomRight * uiScale,
            RadiusBottomLeft * uiScale, segments, -CornerSoftness, _solidLoop);
        KsTguiGeometry.RoundedLoop(box, RadiusTopLeft * uiScale, RadiusTopRight * uiScale, RadiusBottomRight * uiScale,
            RadiusBottomLeft * uiScale, segments, CornerSoftness, _softLoop);

        var written = 0;
        if (BackgroundColor.A > 0f)
        {
            // Raw vertices don't get the handle's modulate. Their colour stays sRGB: despite what DrawVertexUV2DColor's docs
            //      say, the vertex shader converts it (base-default.vert), so a FromSrgb here would convert it twice.
            var background = BackgroundColor * handle.Modulate;
            WriteFan(box.Center, background, loopLength, ref written);
            WriteStrip(_solidLoop, background, _softLoop, background.WithAlpha(0f), loopLength, ref written);
        }

        if (hasBorder)
        {
            var border = BorderColor * handle.Modulate;

            var innerBox = KsTguiGeometry.Deflate(box, thickness.Left, thickness.Top, thickness.Right, thickness.Bottom);
            KsTguiGeometry.RoundedLoop(innerBox,
                MathF.Max(RadiusTopLeft * uiScale - MathF.Max(thickness.Left, thickness.Top), 0f),
                MathF.Max(RadiusTopRight * uiScale - MathF.Max(thickness.Right, thickness.Top), 0f),
                MathF.Max(RadiusBottomRight * uiScale - MathF.Max(thickness.Right, thickness.Bottom), 0f),
                MathF.Max(RadiusBottomLeft * uiScale - MathF.Max(thickness.Left, thickness.Bottom), 0f),
                segments, 0f, _innerLoop);

            WriteStrip(_innerLoop, border, _solidLoop, border, loopLength, ref written);

            // A border on only some sides stops at the corners, so it has no outer edge of its own to soften.
            if (thickness.Left > 0f && thickness.Top > 0f && thickness.Right > 0f && thickness.Bottom > 0f)
                WriteStrip(_solidLoop, border, _softLoop, border.WithAlpha(0f), loopLength, ref written);
        }

        if (written > 0)
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, Texture.White, _vertices.AsSpan(0, written));
    }

    private void DrawSquare(DrawingHandleScreen handle, UIBox2 box, Thickness thickness, bool hasBorder)
    {
        if (BackgroundColor.A > 0f)
            handle.DrawRect(box, BackgroundColor);

        if (!hasBorder)
            return;

        if (thickness.Left > 0f)
            handle.DrawRect(new UIBox2(box.Left, box.Top, box.Left + thickness.Left, box.Bottom), BorderColor);

        if (thickness.Right > 0f)
            handle.DrawRect(new UIBox2(box.Right - thickness.Right, box.Top, box.Right, box.Bottom), BorderColor);

        // The top and bottom stop short of the sides, or a translucent border doubles up in the corners.
        var inner = KsTguiGeometry.Deflate(box, thickness.Left, 0f, thickness.Right, 0f);
        if (thickness.Top > 0f)
            handle.DrawRect(new UIBox2(inner.Left, box.Top, inner.Right, box.Top + thickness.Top), BorderColor);

        if (thickness.Bottom > 0f)
            handle.DrawRect(new UIBox2(inner.Left, box.Bottom - thickness.Bottom, inner.Right, box.Bottom), BorderColor);
    }

    private void EnsureCapacity(int loopLength)
    {
        if (_solidLoop.Length != loopLength)
        {
            _solidLoop = new Vector2[loopLength];
            _softLoop = new Vector2[loopLength];
            _innerLoop = new Vector2[loopLength];
        }

        // A fan, and up to three strips around the loop.
        var vertexCount = loopLength * (3 + 6 * 3);
        if (_vertices.Length != vertexCount)
            _vertices = new DrawVertexUV2DColor[vertexCount];
    }

    private void WriteFan(Vector2 center, Color color, int loopLength, ref int written)
    {
        for (var i = 0; i < loopLength; i++)
        {
            _vertices[written++] = new DrawVertexUV2DColor(center, color);
            _vertices[written++] = new DrawVertexUV2DColor(_solidLoop[i], color);
            _vertices[written++] = new DrawVertexUV2DColor(_solidLoop[(i + 1) % loopLength], color);
        }
    }

    private void WriteStrip(Vector2[] innerLoop, Color innerColor, Vector2[] outerLoop, Color outerColor, int loopLength, ref int written)
    {
        for (var i = 0; i < loopLength; i++)
        {
            var next = (i + 1) % loopLength;
            _vertices[written++] = new DrawVertexUV2DColor(innerLoop[i], innerColor);
            _vertices[written++] = new DrawVertexUV2DColor(outerLoop[i], outerColor);
            _vertices[written++] = new DrawVertexUV2DColor(outerLoop[next], outerColor);
            _vertices[written++] = new DrawVertexUV2DColor(innerLoop[i], innerColor);
            _vertices[written++] = new DrawVertexUV2DColor(outerLoop[next], outerColor);
            _vertices[written++] = new DrawVertexUV2DColor(innerLoop[next], innerColor);
        }
    }
}
