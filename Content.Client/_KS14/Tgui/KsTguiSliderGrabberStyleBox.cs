using System.Numerics;
using Robust.Client.Graphics;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     The marker on a tgui Slider: a thin white line with a small triangle at its foot.
/// </summary>
public sealed class KsTguiSliderGrabberStyleBox : StyleBox
{
    private const float TriangleHalfWidth = 4f;

    private readonly DrawVertexUV2DColor[] _vertices = new DrawVertexUV2DColor[3];

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        handle.DrawRect(box, Color.White);

        var halfWidth = TriangleHalfWidth * uiScale;
        var center = box.Center.X;
        var color = handle.Modulate;
        _vertices[0] = new DrawVertexUV2DColor(new Vector2(center, box.Bottom - halfWidth), color);
        _vertices[1] = new DrawVertexUV2DColor(new Vector2(center + halfWidth, box.Bottom), color);
        _vertices[2] = new DrawVertexUV2DColor(new Vector2(center - halfWidth, box.Bottom), color);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, Texture.White, _vertices);
    }
}
