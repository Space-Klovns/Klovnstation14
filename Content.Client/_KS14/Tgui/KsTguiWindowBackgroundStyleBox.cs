using Content.Client._KS14.UserInterface.Styles;
using Robust.Client.Graphics;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     A tgui window's body: tgui's top to bottom gradient, with the theme's logo centred below the title bar.
/// </summary>
public sealed class KsTguiWindowBackgroundStyleBox : StyleBox
{
    private readonly KsStyleBoxGradient _gradient;
    private readonly Texture _logo;
    private readonly Color _logoColor;
    private readonly float _logoAspect;

    /// <param name="logo">The theme's <c>bg-*.svg</c>, white on clear.</param>
    /// <param name="start">The top of the gradient, <c>--color-base-start</c>.</param>
    /// <param name="end">The bottom, <c>--color-base-end</c>.</param>
    /// <param name="logoColor">The logo's own colour and opacity: black at <c>opacity .33</c>, which every theme's svg sets.</param>
    public KsTguiWindowBackgroundStyleBox(Texture logo, Color start, Color end, Color logoColor)
    {
        _logo = logo;
        _logoColor = logoColor;
        _logoAspect = logo.Height == 0 ? 1f : (float)logo.Width / (float)logo.Height;
        _gradient = new KsStyleBoxGradient(start, end, KsGradientDirection.TopToBottom);
    }

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        _gradient.Draw(handle, box, uiScale);

        var area = KsTguiGeometry.Deflate(box, 0f, KsTguiGeometry.TitleBarHeight * uiScale, 0f, 0f);
        if (area.Width > 0f && area.Height > 0f)
            handle.DrawTextureRect(_logo, KsTguiGeometry.FitLogo(area, _logoAspect), _logoColor);
    }
}
