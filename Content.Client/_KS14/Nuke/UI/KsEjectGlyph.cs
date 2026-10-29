using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Nuke.UI;

/// <summary>
///     The ⏏ eject symbol: an upward triangle over a bar, centred in the control.
/// </summary>
public sealed class KsEjectGlyph : Control
{
    // Reused every draw; stackalloc and span collection expressions don't pass the client sandbox's IL verifier.
    private readonly Vector2[] _triangle = new Vector2[3];

    public Color Color { get; set; } = Color.FromHex("#2F2E26");

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var size = MathF.Min(PixelSize.X, PixelSize.Y);
        var topLeft = (PixelSize - new Vector2(size)) / 2f;

        var triangleBottom = topLeft.Y + size * 0.6f;
        _triangle[0] = new Vector2(topLeft.X + size / 2f, topLeft.Y);
        _triangle[1] = new Vector2(topLeft.X + size, triangleBottom);
        _triangle[2] = new Vector2(topLeft.X, triangleBottom);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _triangle, Color);

        var barTop = topLeft.Y + size * 0.75f;
        handle.DrawRect(new UIBox2(topLeft.X, barTop, topLeft.X + size, topLeft.Y + size), Color);
    }
}
