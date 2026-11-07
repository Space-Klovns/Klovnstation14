using System.Numerics;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Tgui.Backdrop;

/// <summary>
///     The numbers and selection rules behind tgui's <c>backdrop-filter: blur(12px)</c>, apart from the drawing, so that
///         they can be checked headless. The blur shader (<c>backdrop_blur.swsl</c>) works the same weights out on the GPU
///         from <see cref="TapStep"/> and <see cref="SigmaTaps"/>, which are given to it from here.
/// </summary>
public static class KsTguiBackdropMath
{
    /// <summary>
    ///     <c>--blur-medium</c> is <c>blur(12px)</c>, and CSS takes that length as the Gaussian's standard deviation.
    /// </summary>
    public const float SigmaPixels = 12f;

    /// <summary>
    ///     The pixels between two samples. Three keeps the 25 taps of a pass reaching 3 sigma either side, and the texture is
    ///         sampled bilinearly, so the pixels between two taps are averaged and not skipped.
    /// </summary>
    public const float TapStep = 3f;

    /// <summary>
    ///     Taps either side of the middle one. Must match the loop in <c>backdrop_blur.swsl</c>.
    /// </summary>
    public const int TapRadius = 12;

    /// <summary>
    ///     The standard deviation in taps.
    /// </summary>
    public const float SigmaTaps = SigmaPixels / TapStep;

    /// <summary>
    ///     The largest side of a backdrop the blur will take, in pixels. Nothing in tgui's popups comes near it.
    /// </summary>
    public const int MaxSide = 2048;

    /// <summary>
    ///     The weight of each tap of a pass, from <c>-TapRadius</c> to <c>+TapRadius</c>, normalised to sum to one.
    /// </summary>
    public static float[] Weights()
    {
        var weights = new float[TapRadius * 2 + 1];
        var total = 0f;
        for (var i = -TapRadius; i <= TapRadius; i++)
        {
            var weight = MathF.Exp(-0.5f * i * i / (SigmaTaps * SigmaTaps));
            weights[i + TapRadius] = weight;
            total += weight;
        }

        for (var i = 0; i < weights.Length; i++)
            weights[i] /= total;

        return weights;
    }

    /// <summary>
    ///     The standard deviation, in pixels, that <see cref="Weights"/> spaced <see cref="TapStep"/> apart has.
    /// </summary>
    public static float EffectiveSigmaPixels()
    {
        var weights = Weights();
        var variance = 0f;
        for (var i = -TapRadius; i <= TapRadius; i++)
        {
            var distance = i * TapStep;
            variance += weights[i + TapRadius] * distance * distance;
        }

        return MathF.Sqrt(variance);
    }

    /// <summary>
    ///     Reflects a texture coordinate back into 0 to 1 as a triangle wave, which is how the shader treats the edges: Chromium
    ///         blurs a backdrop with its edge mirrored, so nothing outside the panel's box leaks in.
    /// </summary>
    public static float Mirror(float coordinate)
    {
        var wave = coordinate - 2f * MathF.Floor(coordinate * 0.5f);
        return wave >= 1f ? 2f - wave : wave;
    }

    /// <summary>
    ///     The part of the screen to render and blur for a panel: the panel's size at its top left corner, rounded to whole
    ///         pixels. The blur is drawn over the panel's box, so this is the whole region, with no margin. A panel with no
    ///         area, or one bigger than <see cref="MaxSide"/>, has none.
    /// </summary>
    /// <param name="topLeft">Where the panel's top left corner is on screen, in pixels.</param>
    /// <param name="size">The panel's size in pixels.</param>
    public static UIBox2i? Region(Vector2 topLeft, Vector2i size)
    {
        if (size.X <= 0 || size.Y <= 0 || size.X > MaxSide || size.Y > MaxSide)
            return null;

        var origin = new Vector2i((int)MathF.Round(topLeft.X), (int)MathF.Round(topLeft.Y));
        return UIBox2i.FromDimensions(origin, size);
    }

    /// <summary>
    ///     Everything drawn before <paramref name="anchor"/>, as the controls to render and where each goes on screen: from the
    ///         UI root down to the anchor, every sibling that comes before the next control on the way. The controls on the way
    ///         are not themselves drawn, so they must draw nothing of their own, as the popup layers don't. The anchor and what
    ///         it holds are left out, since they are what is being blurred behind.
    /// </summary>
    /// <param name="anchor">The control the backdrop is for.</param>
    /// <param name="anchorPosition">Where it is on screen, in pixels.</param>
    /// <param name="result">Filled, in drawing order.</param>
    public static void EarlierControls(Control anchor, Vector2i anchorPosition, List<(Control Control, Vector2i Position)> result)
    {
        result.Clear();

        // Up to the root, remembering each step, so that the walk down can be in drawing order.
        var chain = new List<(Control Control, Vector2i Position)>();
        var current = anchor;
        var position = anchorPosition;
        while (current.Parent is { } parent)
        {
            position -= current.PixelPosition;
            chain.Add((current, position));
            current = parent;
        }

        for (var i = chain.Count - 1; i >= 0; i--)
        {
            var (child, parentPosition) = chain[i];
            var parent = child.Parent!;
            var index = child.GetPositionInParent();
            for (var sibling = 0; sibling < index; sibling++)
            {
                var earlier = parent.GetChild(sibling);
                result.Add((earlier, parentPosition + earlier.PixelPosition));
            }
        }
    }
}
