using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Nuke.UI;

/// <summary>
///     The radiation trefoil, drawn as three 60° blades around a centre dot and centred in the control.
/// </summary>
public sealed class KsTrefoilGlyph : Control
{
    private const int SegmentsPerBlade = 12;
    private const float BladeSpan = MathF.PI / 3f;

    // Fractions of the glyph's radius.
    private const float DotRadius = 0.2f;
    private const float BladeInnerRadius = 0.3f;

    // Blade centres, clockwise from the x axis with y pointing down: top-left, top-right, bottom.
    private static readonly float[] BladeAngles = [MathF.PI * 7f / 6f, MathF.PI * 11f / 6f, MathF.PI / 2f];

    // Reused every draw; stackalloc doesn't pass the client sandbox's IL verifier.
    private readonly Vector2[] _vertices = new Vector2[BladeAngles.Length * SegmentsPerBlade * 6];

    /// <summary>
    ///     The pixel size <see cref="_vertices"/> were built for; the blades only change on resize.
    /// </summary>
    private Vector2i _builtForSize = Vector2i.Zero;

    public Color Color { get; set; } = Color.FromHex("#9C9987");

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var center = PixelSize / 2f;
        var radius = MathF.Min(PixelSize.X, PixelSize.Y) / 2f;

        if (_builtForSize != PixelSize)
        {
            _builtForSize = PixelSize;
            BuildBlades(center, radius);
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _vertices, Color);
        handle.DrawCircle(center, radius * DotRadius, Color);
    }

    private void BuildBlades(Vector2 center, float radius)
    {
        var innerRadius = radius * BladeInnerRadius;
        var vertices = _vertices;
        var i = 0;

        foreach (var bladeAngle in BladeAngles)
        {
            for (var segment = 0; segment < SegmentsPerBlade; segment++)
            {
                var from = bladeAngle - BladeSpan / 2f + BladeSpan * segment / SegmentsPerBlade;
                var to = from + BladeSpan / SegmentsPerBlade;
                var fromDirection = new Vector2(MathF.Cos(from), MathF.Sin(from));
                var toDirection = new Vector2(MathF.Cos(to), MathF.Sin(to));

                var innerFrom = center + fromDirection * innerRadius;
                var outerFrom = center + fromDirection * radius;
                var innerTo = center + toDirection * innerRadius;
                var outerTo = center + toDirection * radius;

                vertices[i++] = innerFrom;
                vertices[i++] = outerFrom;
                vertices[i++] = outerTo;
                vertices[i++] = innerFrom;
                vertices[i++] = outerTo;
                vertices[i++] = innerTo;
            }
        }
    }
}
