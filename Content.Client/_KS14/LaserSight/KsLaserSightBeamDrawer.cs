using System.Numerics;
using Content.Shared._KS14.LaserSight;
using Robust.Client.Graphics;

namespace Content.Client._KS14.LaserSight;

/// <summary>
///     Draws one beam as a solid-then-fading triangle strip with per-vertex alpha. Used by both laser overlays so the
///         world pass and the emissive pass fade identically.
/// </summary>
public sealed class KsLaserSightBeamDrawer
{
    private readonly Vector2[] _positions = new Vector2[KsLaserSightMath.FadeStripVertexCount];
    private readonly float[] _alphas = new float[KsLaserSightMath.FadeStripVertexCount];
    private readonly DrawVertexUV2DColor[] _vertices = new DrawVertexUV2DColor[KsLaserSightMath.FadeStripVertexCount];

    public void Draw(DrawingHandleWorld handle, in KsLaserSegment segment, float width, Color color)
    {
        KsLaserSightMath.GetFadeStrip(
            segment.Origin,
            segment.Rotation,
            segment.Length,
            width,
            segment.MaxLength,
            segment.FadeStart,
            _positions,
            _alphas);

        for (var i = 0; i < _vertices.Length; i++)
        {
            // DrawPrimitives ignores Modulate and wants linear colour.
            var vertexColor = Color.FromSrgb(color.WithAlpha(color.A * _alphas[i]));
            _vertices[i] = new DrawVertexUV2DColor(_positions[i], vertexColor);
        }

        // UV defaults to the middle of the texture, which is flat white.
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, Texture.White, _vertices);
    }
}
