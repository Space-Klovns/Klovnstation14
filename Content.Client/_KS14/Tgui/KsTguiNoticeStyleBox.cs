using System.Numerics;
using Robust.Client.Graphics;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     A tgui NoticeBox's striped background: <c>repeating-linear-gradient(-45deg, transparent 0 10px, black .1 10px 20px)</c>.
///         Clipped geometry and not a tiled texture, because tiles can only start from the top left, whereas the CSS
///         starts from the bottom right corner, and each tile would be its own draw.
/// </summary>
public sealed class KsTguiNoticeStyleBox : StyleBox
{
    private const float StripeWidth = 10f;

    private static readonly float Diagonal = MathF.Sqrt(2f);

    public Color BackgroundColor;

    private DrawVertexUV2DColor[] _vertices = [];
    private readonly Vector2[] _polygon = new Vector2[6];
    private readonly Vector2[] _scratch = new Vector2[6];

    private readonly Color _stripeColor;

    /// <param name="backgroundColor">The box's colour.</param>
    /// <param name="whiteStripes">tgui's <c>black</c> box has white stripes at .1, where the rest have black.</param>
    public KsTguiNoticeStyleBox(Color backgroundColor, bool whiteStripes = false)
    {
        BackgroundColor = backgroundColor;
        _stripeColor = KsTguiPalettes.Translucent(whiteStripes ? Color.White : Color.Black, 0.1f, backgroundColor);
    }

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        handle.DrawRect(box, BackgroundColor);

        // In x + y from the corner, a width measured across a 45 degree stripe comes out longer by the diagonal.
        var stripe = StripeWidth * uiScale * Diagonal;
        var size = box.Size;
        var stripeCount = (int)MathF.Ceiling((size.X + size.Y) / (stripe * 2f));

        // A clipped stripe has at most 6 points, so at most 4 triangles.
        var maxVertices = stripeCount * 4 * 3;
        if (_vertices.Length < maxVertices)
            _vertices = new DrawVertexUV2DColor[maxVertices];

        var color = _stripeColor * handle.Modulate;
        var written = 0;
        for (var i = 0; i < stripeCount; i++)
        {
            var low = stripe * (i * 2 + 1);
            var count = KsTguiGeometry.ClipStripe(size, low, low + stripe, _polygon, _scratch);

            for (var point = 1; point < count - 1; point++)
            {
                _vertices[written++] = new DrawVertexUV2DColor(box.BottomRight - _polygon[0], color);
                _vertices[written++] = new DrawVertexUV2DColor(box.BottomRight - _polygon[point], color);
                _vertices[written++] = new DrawVertexUV2DColor(box.BottomRight - _polygon[point + 1], color);
            }
        }

        if (written > 0)
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, Texture.White, _vertices.AsSpan(0, written));
    }
}
