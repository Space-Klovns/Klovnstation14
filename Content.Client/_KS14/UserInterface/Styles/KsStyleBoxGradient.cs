using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Maths;

namespace Content.Client._KS14.UserInterface.Styles;

public enum KsGradientDirection : byte
{
    LeftToRight,
    RightToLeft,
    TopToBottom,
    BottomToTop,
}

/// <summary>
///     A multi-stop linear gradient <see cref="StyleBox"/>, drawn as one triangle strip with per-vertex colours.
///     It works anywhere a stylebox does: sheetlet rules, <c>PanelOverride</c>, button boxes.
/// </summary>
/// <example>
///     In a sheetlet rule:
///     <code>
///     E&lt;PanelContainer&gt;().Class("MyFade").Panel(
///         new KsStyleBoxGradient(Color.Black.WithAlpha(0.75f), Color.Transparent, KsGradientDirection.LeftToRight))
///     </code>
///     From code, with several stops:
///     <code>
///     panel.PanelOverride = new KsStyleBoxGradient(KsGradientDirection.TopToBottom,
///         (0f, Color.Transparent), (0.6f, Color.Black.WithAlpha(0.3f)), (1f, Color.Black));
///     </code>
///     Then use a plain <c>&lt;PanelContainer StyleClasses="..." MouseFilter="Ignore"/&gt;</c> in xaml.
/// </example>
public sealed class KsStyleBoxGradient : StyleBox
{
    /// <summary>
    ///     Stops as (offset 0..1 along the direction, colour). At least two, ascending by offset.
    /// </summary>
    public List<(float Offset, Color Color)> Stops;

    public KsGradientDirection Direction;

    /// <summary>
    ///     Extra distance, in virtual pixels, to draw past each edge of the box. Use it for edge-to-edge chrome: pixel
    ///         positions and sizes are truncated separately, so a box laid out to the window edge can stop a pixel
    ///         short of it, and <see cref="StyleBox.Padding"/> cannot be negative. Doesn't change layout.
    /// </summary>
    public Thickness Bleed;

    private DrawVertexUV2DColor[] _vertices = [];

    public KsStyleBoxGradient(KsGradientDirection direction, params (float Offset, Color Color)[] stops)
    {
        Direction = direction;
        Stops = new List<(float Offset, Color Color)>(stops);
    }

    public KsStyleBoxGradient(Color start, Color end, KsGradientDirection direction)
        : this(direction, (0f, start), (1f, end))
    {
    }

    public KsStyleBoxGradient(KsStyleBoxGradient other)
        : base(other)
    {
        Direction = other.Direction;
        Stops = new List<(float Offset, Color Color)>(other.Stops);
        Bleed = other.Bleed;
    }

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        box = new UIBox2(
            box.Left - Bleed.Left * uiScale,
            box.Top - Bleed.Top * uiScale,
            box.Right + Bleed.Right * uiScale,
            box.Bottom + Bleed.Bottom * uiScale);

        if (Stops.Count < 2 || box.Width <= 0f || box.Height <= 0f)
            return;

        var horizontal = Direction is KsGradientDirection.LeftToRight or KsGradientDirection.RightToLeft;
        var reversed = Direction is KsGradientDirection.RightToLeft or KsGradientDirection.BottomToTop;

        // Reused between draws; resized whenever the stop count changes.
        var vertexCount = Stops.Count * 2;
        if (_vertices.Length != vertexCount)
            _vertices = new DrawVertexUV2DColor[vertexCount];

        var vertices = _vertices;
        for (var i = 0; i < Stops.Count; i++)
        {
            var (offset, color) = Stops[i];
            var t = reversed ? 1f - offset : offset;
            var realColor = Color.FromSrgb(color * handle.Modulate);

            Vector2 a;
            Vector2 b;
            if (horizontal)
            {
                var x = box.Left + t * box.Width;
                a = new Vector2(x, box.Top);
                b = new Vector2(x, box.Bottom);
            }
            else
            {
                var y = box.Top + t * box.Height;
                a = new Vector2(box.Left, y);
                b = new Vector2(box.Right, y);
            }

            vertices[i * 2] = new DrawVertexUV2DColor(a, realColor);
            vertices[i * 2 + 1] = new DrawVertexUV2DColor(b, realColor);
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, Texture.White, vertices);
    }
}
