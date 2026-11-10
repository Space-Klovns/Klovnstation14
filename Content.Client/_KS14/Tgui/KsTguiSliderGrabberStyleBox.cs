using System.Numerics;
using Robust.Client.Graphics;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     The marker on a tgui Slider: a 2px white line with rounded ends, and a triangle at its foot.
/// </summary>
/// <remarks>
///     tgui's triangle is a 2px square scaled by 4 and turned 45 degrees, its top half showing. That is a triangle whose base
///         is the square's scaled diagonal, 11.3px, and it is half as tall, with the base 3px below
///         the bar's bottom edge and the tip 2.66px inside it.
/// </remarks>
public sealed class KsTguiSliderGrabberStyleBox : StyleBox
{
    /// <summary>
    ///     Half the triangle's base, and its height. A square scaled by 4 has a half diagonal of 4 * sqrt(2) = 5.66.
    /// </summary>
    public const float TriangleHalfWidth = 5.6568542f;
    public const float TriangleHeight = 5.6568542f;

    /// <summary>
    ///     How far the base hangs below the bar.
    /// </summary>
    public const float TriangleBaseBelow = 3f;

    private readonly DrawVertexUV2DColor[] _vertices = new DrawVertexUV2DColor[3];
    private readonly KsTguiStyleBox _line = new(Color.White, 1f);

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        _line.Draw(handle, box, uiScale);

        var halfWidth = TriangleHalfWidth * uiScale;
        var center = box.Center.X;
        var baseY = box.Bottom + TriangleBaseBelow * uiScale;
        var color = handle.Modulate;
        _vertices[0] = new DrawVertexUV2DColor(new Vector2(center, baseY - TriangleHeight * uiScale), color);
        _vertices[1] = new DrawVertexUV2DColor(new Vector2(center + halfWidth, baseY), color);
        _vertices[2] = new DrawVertexUV2DColor(new Vector2(center - halfWidth, baseY), color);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, Texture.White, _vertices);
    }
}
