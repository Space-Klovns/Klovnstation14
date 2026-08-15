/*
    Some code in this file is taken from https://github.com/space-wizards/RobustToolbox/blob/08a3d120b7029d03e60b44b23fed2b2659ed3224/Robust.Client/UserInterface/Controls/TextureRect.cs
        which is licensed under the MIT license
*/

using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client._KS14.LobbyTransition;

[Access(typeof(LobbyTransitionSystem))]
public sealed partial class LobbyTransitionOverlay : Overlay
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IClyde _clyde = default!;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;
    public Texture? ArtTexture = null;
    public TimeSpan TransitionStartTime = TimeSpan.MinValue;
    public TimeSpan TransitionFinishTime = TimeSpan.MinValue;

    private TimeSpan _curTime;

    protected override bool BeforeDraw(in OverlayDrawArgs args)
        => ArtTexture is { } &&
        (_curTime = _gameTiming.CurTime) < TransitionFinishTime;

    protected override void Draw(in OverlayDrawArgs args)
    {
        // fade-out
        var elapsed = (_curTime - TransitionStartTime).TotalSeconds;
        var duration = (TransitionFinishTime - TransitionStartTime).TotalSeconds;
        var alpha = Math.Clamp(1f - (float)(elapsed / duration), 0f, 1f);
        var modulate = new Color(1f, 1f, 1f, a: alpha);

        // draw according to StretchMode.KeepAspectCovered
        var pixelSize = _clyde.ScreenSize;
        var pixelSizeBox = new UIBox2i(Vector2i.Zero, pixelSize);

        var dima = GetDrawDimensions(pixelSize);
        var subRegion = CalcClipSubRegion(ArtTexture!.Size, dima, pixelSizeBox);
        args.ScreenHandle.DrawTextureRectRegion(ArtTexture!, pixelSizeBox, subRegion, modulate: modulate);
    }

    private static UIBox2 CalcClipSubRegion(Vector2 texSize, UIBox2 drawDimensions, UIBox2 size)
    {
        var normTL = (size.TopLeft - drawDimensions.TopLeft) / drawDimensions.Size;
        var normBR = (size.BottomRight - drawDimensions.TopLeft) / drawDimensions.Size;

        return new UIBox2(normTL * texSize, normBR * texSize);
    }

    private UIBox2 GetDrawDimensions(Vector2i pixelSize)
    {
        // ArtTexture.Size is a Vector2i; dividing Vector2i by Vector2i truncates to integers,
        // so convert to Vector2 first to get a proper floating-point scale factor.
        var texSize = (Vector2)ArtTexture!.Size;
        var (scaleX, scaleY) = pixelSize / texSize;
        var scale = Math.Max(scaleX, scaleY);
        var texDrawSize = texSize * scale;
        var offset = (pixelSize - texDrawSize) / 2f;
        return UIBox2.FromDimensions(offset, texDrawSize);
    }
}
