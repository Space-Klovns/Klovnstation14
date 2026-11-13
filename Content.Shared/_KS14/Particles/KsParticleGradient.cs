using System.Numerics;

namespace Content.Shared._KS14.Particles;

/// <summary>
///     The colour space a <see cref="KsParticleGradient"/> blends in.
/// </summary>
public enum KsColorSpace : byte
{
    Rgb,
    Hsl,
    Hsv,
}

/// <summary>
///     A run of colours a particle moves along over its life: BYOND's <c>gradient</c>. With one set, a prototype's
///         <c>color</c> and <c>colorChange</c> are an index into it and its rate of change.
/// </summary>
[DataDefinition]
public sealed partial class KsParticleGradient
{
    /// <summary>
    ///     The colours, in order.
    /// </summary>
    [DataField(required: true)]
    public List<Color> Colors = new();

    /// <summary>
    ///     Where each colour sits, one per colour, ascending. Left out, the colours are spread evenly from 0 to 1,
    ///         as BYOND does for a gradient given no positions.
    /// </summary>
    [DataField]
    public List<float>? Positions;

    /// <summary>
    ///     Whether an index past the end wraps round to the start, rather than holding the last colour.
    /// </summary>
    [DataField]
    public bool Loop;

    /// <summary>
    ///     The space the colours are blended in. BYOND's <c>"space" = COLORSPACE_*</c>.
    /// </summary>
    [DataField]
    public KsColorSpace Space = KsColorSpace.Rgb;

    public float PositionOf(int index)
    {
        if (Positions != null && index < Positions.Count)
            return Positions[index];

        return Colors.Count <= 1 ? 0f : (float)index / (Colors.Count - 1);
    }

    public float Start => Colors.Count == 0 ? 0f : PositionOf(0);
    public float End => Colors.Count == 0 ? 0f : PositionOf(Colors.Count - 1);

    /// <summary>
    ///     The colour at <paramref name="index"/>.
    /// </summary>
    public Color Evaluate(float index)
    {
        if (Colors.Count == 0)
            return Color.White;

        if (Colors.Count == 1)
            return Colors[0];

        var start = Start;
        var end = End;
        var span = end - start;

        if (Loop && span > 0f)
        {
            index = (index - start) % span;
            if (index < 0f)
                index += span;
            index += start;
        }
        else
        {
            index = Math.Clamp(index, start, end);
        }

        // Few stops, so a linear walk beats any cleverness.
        for (var i = 1; i < Colors.Count; i++)
        {
            var at = PositionOf(i);
            if (index > at)
                continue;

            var previousAt = PositionOf(i - 1);
            var segment = at - previousAt;
            var fraction = segment <= 0f ? 1f : (index - previousAt) / segment;
            return Blend(Colors[i - 1], Colors[i], fraction, Space);
        }

        return Colors[^1];
    }

    public static Color Blend(Color from, Color to, float fraction, KsColorSpace space)
    {
        switch (space)
        {
            case KsColorSpace.Hsl:
            {
                var a = Color.ToHsl(from);
                var b = Color.ToHsl(to);
                return Color.FromHsl(new Vector4(LerpHue(a.X, b.X, fraction), float.Lerp(a.Y, b.Y, fraction), float.Lerp(a.Z, b.Z, fraction), float.Lerp(a.W, b.W, fraction)));
            }
            case KsColorSpace.Hsv:
            {
                var a = Color.ToHsv(from);
                var b = Color.ToHsv(to);
                return Color.FromHsv(new Vector4(LerpHue(a.X, b.X, fraction), float.Lerp(a.Y, b.Y, fraction), float.Lerp(a.Z, b.Z, fraction), float.Lerp(a.W, b.W, fraction)));
            }
            default:
                return Color.InterpolateBetween(from, to, fraction);
        }
    }

    /// <summary>
    ///     Hue is a circle (0..1 wraps), so take the shorter way round.
    /// </summary>
    private static float LerpHue(float from, float to, float fraction)
    {
        var delta = to - from;
        if (delta > 0.5f)
            delta -= 1f;
        else if (delta < -0.5f)
            delta += 1f;

        var hue = from + delta * fraction;
        if (hue < 0f)
            hue += 1f;
        else if (hue >= 1f)
            hue -= 1f;
        return hue;
    }
}
