using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     A button in tgui's title bar, as its close button is: full height, 45px wide, a glyph at half opacity, and lit the moment
///         it's hovered, fading back once it isn't.
/// </summary>
public sealed class KsTguiTitleBarButton : BaseButton
{
    private const float ButtonWidth = 45f;
    private const float IconSize = 16f;
    /// <summary>
    ///     tgui's <c>opacity: .5</c> on the glyph, as the alpha that blends to what CSS gets over the title bar. A plain .5 blends
    ///         in linear light and comes out 191 where tgui's is 156.
    /// </summary>
    internal static readonly float IdleOpacity = KsTguiPalettes.Translucent(Color.White, 0.5f, KsTguiPalettes.TitleBar).A;

    private const float FadeOutSeconds = 0.2f;

    /// <summary>
    ///     tgui's close button.
    /// </summary>
    public static readonly Color CloseHoverColor = Color.FromHex("#D92626");

    /// <summary>
    ///     Anything else in the bar, whose hover shouldn't read as closing the window.
    /// </summary>
    public static readonly Color NeutralHoverColor = KsTguiPalettes.Translucent(Color.White, 0.1f, KsTguiPalettes.Base);

    private readonly Texture _icon;
    private readonly Color _hoverColor;

    /// <summary>
    ///     How hovered this looks, from 0 to 1.
    /// </summary>
    private float _hover;

    public KsTguiTitleBarButton(Texture icon, Color hoverColor)
    {
        _icon = icon;
        _hoverColor = hoverColor;
        MinSize = new Vector2(ButtonWidth, KsTguiGeometry.TitleBarHeight);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        _hover = IsHovered ? 1f : MathF.Max(_hover - args.DeltaSeconds / FadeOutSeconds, 0f);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (_hover > 0f)
            handle.DrawRect(PixelSizeBox, _hoverColor.WithAlpha(_hoverColor.A * _hover));

        var center = PixelSizeBox.Center;
        var halfIcon = new Vector2(IconSize * UIScale / 2f);
        handle.DrawTextureRect(
            _icon,
            new UIBox2(center - halfIcon, center + halfIcon),
            Color.White.WithAlpha(MathHelper.Lerp(IdleOpacity, 1f, _hover)));
    }
}
