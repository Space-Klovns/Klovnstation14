namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     What the shader <c>KsTguiTabsMask</c> is given for one frame: tgui's scrollable Tabs <c>mask-image</c>, in pixels.
/// </summary>
/// <param name="StripWidth">The width of the masked strip.</param>
/// <param name="LeftFade"><c>--mask-left</c>, 0 when nothing fades at the left.</param>
/// <param name="RightFade"><c>--mask-right</c>, 0 when nothing fades at the right.</param>
public readonly record struct KsTguiTabsMaskParameters(float StripWidth, float LeftFade, float RightFade);

/// <summary>
///     The alpha of tgui's <c>.Tabs--scrollable-content</c> mask, which the shader <c>tgui_tabs_mask.swsl</c> repeats:
///     <c>linear-gradient(90deg, transparent calc(var(--mask-left) * 0.66), black var(--mask-left),
///         black calc(100% - var(--mask-right)), transparent calc(100% - var(--mask-right) * 0.66))</c>.
/// </summary>
public static class KsTguiTabsMask
{
    /// <summary>
    ///     The mask's alpha at <paramref name="x"/> pixels from the left of a strip <paramref name="width"/> pixels wide. A side
    ///         with a fade of 0 is not masked.
    /// </summary>
    public static float Alpha(float x, float width, float leftFade, float rightFade)
    {
        var alpha = 1f;

        if (leftFade > 0f)
        {
            var start = leftFade * KsTguiTabs.MaskOpaqueFraction;
            alpha *= Math.Clamp((x - start) / (leftFade - start), 0f, 1f);
        }

        if (rightFade > 0f)
        {
            var end = width - rightFade * KsTguiTabs.MaskOpaqueFraction;
            var start = width - rightFade;
            alpha *= 1f - Math.Clamp((x - start) / (end - start), 0f, 1f);
        }

        return alpha;
    }

    public static float Alpha(float x, KsTguiTabsMaskParameters parameters)
    {
        return Alpha(x, parameters.StripWidth, parameters.LeftFade, parameters.RightFade);
    }

    /// <summary>
    ///     The parameters for a strip <paramref name="pixelWidth"/> wide at <paramref name="scale"/>, whose fades are given in
    ///         the layout units of <see cref="KsTguiTabs.LeftFade"/> and <see cref="KsTguiTabs.RightFade"/>.
    /// </summary>
    public static KsTguiTabsMaskParameters Parameters(float leftFade, float rightFade, float pixelWidth, float scale)
    {
        return new KsTguiTabsMaskParameters(pixelWidth, leftFade * scale, rightFade * scale);
    }
}
