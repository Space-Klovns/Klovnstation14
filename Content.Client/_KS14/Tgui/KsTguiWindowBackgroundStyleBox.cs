using Content.Client._KS14.UserInterface.Styles;
using Robust.Client.Graphics;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     A tgui window's body: tgui's top to bottom gradient, with the Nanotrasen logo centred below the title bar.
/// </summary>
public sealed class KsTguiWindowBackgroundStyleBox : StyleBox
{
    /// <summary>
    ///     The logo's own colour and opacity, from <c>bg-nanotrasen.svg</c>.
    /// </summary>
    private static readonly Color LogoColor = KsTguiPalettes.Translucent(Color.Black, 0.33f, KsTguiPalettes.Base);

    private readonly KsStyleBoxGradient _gradient = new(
        Color.FromHex("#2B2B2B"),
        Color.FromHex("#212121"),
        KsGradientDirection.TopToBottom);

    private readonly Texture _logo;

    public KsTguiWindowBackgroundStyleBox(Texture logo)
    {
        _logo = logo;
    }

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        _gradient.Draw(handle, box, uiScale);

        var area = KsTguiGeometry.Deflate(box, 0f, KsTguiGeometry.TitleBarHeight * uiScale, 0f, 0f);
        if (area.Width > 0f && area.Height > 0f)
            handle.DrawTextureRect(_logo, KsTguiGeometry.FitLogo(area), LogoColor);
    }
}
