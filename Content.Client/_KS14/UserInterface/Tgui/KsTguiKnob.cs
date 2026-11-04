using System.Numerics;
using Robust.Client.Graphics;

namespace Content.Client._KS14.UserInterface.Tgui;

/// <summary>
///     tgui's Knob: a round dial with a pale ring showing how far it's turned, through 270 degrees from the lower left.
///         Drag it like a <see cref="KsTguiNumberInput"/>.
/// </summary>
public sealed class KsTguiKnob : KsTguiDragNumber
{
    private const float StartDegrees = 135f;
    private const float SweepDegrees = 270f;

    private static readonly Color Face = Color.FromHex("#4A4343");
    private static readonly Color FaceRim = Color.FromHex("#363030");
    private static readonly Color Cursor = Color.FromHex("#DBD7D7");
    private static readonly Color RingTrack = KsTguiTheme.BorderPrimary.WithAlpha(0.15f);

    private readonly KsTguiPrimitives _shapes = new();

    public KsTguiKnob()
    {
        // tgui's knob is 2.6em at its size 1, and the canister uses size 1.25.
        SetSize = new Vector2(39f, 39f);
        RingColor = KsTguiTheme.BorderPrimary;
    }

    /// <summary>
    ///     The value ring's colour. The canister turns it yellow while its valve is open.
    /// </summary>
    public Color RingColor { get; set; }

    protected override void ValueUpdated()
    {
        ToolTip = FormatValue();
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var radius = MathF.Min(PixelSize.X, PixelSize.Y) / 2f;
        if (radius <= 0f)
            return;

        var center = (Vector2)PixelSize / 2f;
        var ringWidth = radius * 0.16f;
        var ringRadius = radius - ringWidth / 2f;
        var faceRadius = radius * 0.72f;

        _shapes.Begin(Vector2.Zero, 1f);
        _shapes.Arc(center, ringRadius, StartDegrees, StartDegrees + SweepDegrees, ringWidth, segments: 30);
        _shapes.Draw(handle, RingTrack);

        if (Fraction > 0f)
        {
            _shapes.Begin(Vector2.Zero, 1f);
            _shapes.Arc(center, ringRadius, StartDegrees, StartDegrees + SweepDegrees * Fraction, ringWidth,
                segments: Math.Max(2, (int)MathF.Ceiling(30 * Fraction)));
            _shapes.Draw(handle, RingColor);
        }

        handle.DrawCircle(center, faceRadius, FaceRim);
        handle.DrawCircle(center, faceRadius * 0.86f, Face);

        var angle = MathHelper.DegreesToRadians(StartDegrees + SweepDegrees * Fraction);
        var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        _shapes.Begin(Vector2.Zero, 1f);
        _shapes.Line(center + direction * faceRadius * 0.25f, center + direction * faceRadius * 0.8f, MathF.Max(2f, radius * 0.1f));
        _shapes.Draw(handle, Cursor);
    }
}
