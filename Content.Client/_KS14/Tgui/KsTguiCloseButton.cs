using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     tgui's window close button: full height, and red the moment it's hovered, fading back once it isn't.
/// </summary>
public sealed class KsTguiCloseButton : BaseButton
{
    private const float ButtonWidth = 45f;
    private const float IconSize = 16f;
    private const float IdleOpacity = 0.5f;
    private const float FadeOutSeconds = 0.2f;

    private static readonly Color HoverColor = Color.FromHex("#D92626");

    private readonly Texture _icon;

    /// <summary>
    ///     How hovered this looks, from 0 to 1.
    /// </summary>
    private float _hover;

    public KsTguiCloseButton(Texture icon)
    {
        _icon = icon;
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
            handle.DrawRect(PixelSizeBox, HoverColor.WithAlpha(_hover));

        var center = PixelSizeBox.Center;
        var halfIcon = new Vector2(IconSize * UIScale / 2f);
        handle.DrawTextureRect(
            _icon,
            new UIBox2(center - halfIcon, center + halfIcon),
            Color.White.WithAlpha(MathHelper.Lerp(IdleOpacity, 1f, _hover)));
    }
}
