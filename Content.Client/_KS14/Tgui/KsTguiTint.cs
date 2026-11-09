using System.Numerics;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     A CSS colour at a CSS opacity, as written in tgui's styles (<c>hsl(from var(--color-base) h s l / 0.85)</c>). A browser
///         blends it with what is behind it in sRGB: <c>result = opacity * color + (1 - opacity) * behind</c>, per channel, on
///         the encoded values. This engine blends in linear light, so a plain alpha gives a different colour; the backdrop
///         pass (<see cref="Backdrop.KsTguiBackdropBlur"/>) does the sum above in its shader instead, with these numbers.
/// </summary>
public readonly record struct KsTguiTint(Color Color, float Opacity)
{
    /// <summary>
    ///     What CSS paints where this lies over <paramref name="backdrop"/>, both as sRGB values, which is what a
    ///         <see cref="Color"/> holds. The backdrop pass's shader does exactly this.
    /// </summary>
    public Color BlendOver(Color backdrop)
    {
        return new Color(
            Color.R * Opacity + backdrop.R * (1f - Opacity),
            Color.G * Opacity + backdrop.G * (1f - Opacity),
            Color.B * Opacity + backdrop.B * (1f - Opacity),
            1f);
    }

    /// <summary>
    ///     A plain translucent colour that comes closest to this in the engine's linear blending over one assumed backdrop. For
    ///         the draws that cannot be composited, such as a popup drawn inside another's backdrop pass.
    /// </summary>
    public Color Approximate(Color backdrop)
    {
        return KsTguiPalettes.Translucent(Color, Opacity, backdrop);
    }

    /// <summary>
    ///     How the backdrop shaders take it: sRGB channels, then the opacity.
    /// </summary>
    public Vector4 ShaderValue => new(Color.R, Color.G, Color.B, Opacity);
}
