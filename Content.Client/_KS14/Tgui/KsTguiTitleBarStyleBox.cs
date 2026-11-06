using Content.Client._KS14.UserInterface.Styles;
using Robust.Client.Graphics;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     A tgui title bar: flat, with a thin dark line along the bottom and a soft shadow falling onto the window below.
/// </summary>
public sealed class KsTguiTitleBarStyleBox : StyleBox
{
    private const float ShadowHeight = 6f;

    private static readonly Color BorderColor = KsTguiPalettes.Translucent(Color.Black, 0.33f, KsTguiPalettes.TitleBar);

    /// <summary>
    ///     Black at zero alpha, not <see cref="Color.Transparent"/>, which is white and would pale the shadow as it fades.
    /// </summary>
    private readonly KsStyleBoxGradient _shadow = new(
        Color.Black.WithAlpha(0.2f),
        Color.Black.WithAlpha(0f),
        KsGradientDirection.TopToBottom);

    public Color BackgroundColor;

    public KsTguiTitleBarStyleBox(Color backgroundColor)
    {
        BackgroundColor = backgroundColor;
    }

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        handle.DrawRect(box, BackgroundColor);
        handle.DrawRect(new UIBox2(box.Left, box.Bottom - uiScale, box.Right, box.Bottom), BorderColor);

        _shadow.Draw(handle, new UIBox2(box.Left, box.Bottom, box.Right, box.Bottom + ShadowHeight * uiScale), uiScale);
    }
}
