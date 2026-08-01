using System.Numerics;
using Content.Shared._KS14.Sensors.Prototypes;
using Robust.Client.Graphics;

namespace Content.Client._KS14.Shuttles.UI;

/// <summary>
///     Shared instrument-face drawing primitives for the KS console screens
///         (<see cref="KsBearingPlot"/>, <see cref="KsRadarControl"/>). Pure
///         geometry over a screen-space handle; colours and cadence stay with the
///         callers so each screen keeps its own palette and anims.
/// </summary>
public static class KsInstrumentDraw
{
    /// <summary>
    ///     Draws the ring circle plus degree ticks: minor ticks every
    ///         <paramref name="stepDeg"/>, longer accent ticks every
    ///         <paramref name="majorEveryDeg"/>, all pointing inward from the rim.
    /// </summary>
    public static void DrawRingTicks(DrawingHandleScreen handle, Vector2 centre, float radius, float uiScale,
        Color ring, Color minor, Color major, int stepDeg = 15, int majorEveryDeg = 45)
    {
        handle.DrawCircle(centre, radius, ring, false);

        for (var deg = 0; deg < 360; deg += stepDeg)
        {
            // Compass angle to screen direction: 0 = up, clockwise.
            var dir = new Vector2(MathF.Sin(MathHelper.DegreesToRadians(deg)), -MathF.Cos(MathHelper.DegreesToRadians(deg)));
            var isMajor = deg % majorEveryDeg == 0;
            var len = (isMajor ? 7f : 4f) * uiScale;
            handle.DrawLine(centre + dir * (radius - len), centre + dir * radius, isMajor ? major : minor);
        }
    }

    /// <summary>
    ///     The instrument dead screen: the stock NO SIGNAL layout redrawn in an
    ///         instrument palette and the face font. The fork counterpart of
    ///         <c>MapGridControl.DrawNoSignal</c>, which otherwise flashes grey
    ///         NotoSans in the middle of an amber instrument.
    /// </summary>
    public static void DrawDeadScreen(DrawingHandleScreen handle, UIBox2 pixelBox, Vector2 midPoint, float width,
        string text, Font font, KsInstrumentPalette palette)
    {
        handle.DrawRect(pixelBox, palette.Background);

        const int lineCount = 4;
        for (var i = 0; i < lineCount; i++)
        {
            var angle = Angle.FromDegrees(45 + i * 360f / lineCount);
            var distance = width / 2f;
            var start = midPoint + angle.RotateVec(new Vector2(0f, 2.5f * distance / 4f));
            var end = midPoint + angle.RotateVec(new Vector2(0f, 4f * distance / 4f));
            handle.DrawLine(start, end, palette.AccentDim);
        }

        var dimensions = handle.GetDimensions(font, text, 2f);
        handle.DrawString(font, midPoint - dimensions / 2f, text, 2f, palette.TextDim);
    }

    /// <summary>
    ///     One frame of a contact ping: a ring expanding and fading as
    ///         <paramref name="progress"/> runs 0..1. No-op outside that range, so
    ///         callers can pass raw elapsed/duration without clamping.
    /// </summary>
    public static void DrawPing(DrawingHandleScreen handle, Vector2 pos, float progress, Color colour, float uiScale, float maxAlpha)
    {
        if (progress is <= 0f or >= 1f)
            return;

        var radius = (4f + 18f * progress) * uiScale;
        handle.DrawCircle(pos, radius, colour.WithAlpha(maxAlpha * (1f - progress)), false);
    }
}
