using System.Numerics;
using Content.Shared._KS14.FieldCommander;
using Robust.Client.Graphics;
using Robust.Shared.Utility;

namespace Content.Client.Overlays;

public sealed partial class StencilOverlay
{
    private void DrawFieldCommanderFog(
        in OverlayDrawArgs args,
        CachedResources res,
        KsFieldCommanderFogComponent fogComponent,
        Matrix3x2 invMatrix)
    {
        var worldHandle = args.WorldHandle;
        var renderScale = args.Viewport.RenderScale.X;
        var zoom = args.Viewport.Eye?.Zoom ?? Vector2.One;
        var length = zoom.X;
        var bufferRange = MathF.Min(10f, fogComponent.Range);

        var pixelCenter = Vector2.Transform(fogComponent.Origin, invMatrix);
        var vertical = args.Viewport.Size.Y;

        var pixelMaxRange = fogComponent.Range * renderScale / length * EyeManager.PixelsPerMeter;
        var pixelBufferRange = bufferRange * renderScale / length * EyeManager.PixelsPerMeter;
        var pixelMinRange = pixelMaxRange - pixelBufferRange;

        _shader.SetParameter("position", new Vector2(pixelCenter.X, vertical - pixelCenter.Y));
        _shader.SetParameter("maxRange", pixelMaxRange);
        _shader.SetParameter("minRange", pixelMinRange);
        _shader.SetParameter("bufferRange", pixelBufferRange);
        _shader.SetParameter("gradient", 0.80f);

        var worldAABB = args.WorldAABB;
        var worldBounds = args.WorldBounds;
        var position = args.Viewport.Eye?.Position.Position ?? Vector2.Zero;
        var localAABB = invMatrix.TransformBox(worldAABB);

        worldHandle.RenderInRenderTarget(res.Blep!, () =>
        {
            worldHandle.UseShader(_shader);
            worldHandle.DrawRect(localAABB, Color.White);
        }, Color.Transparent);

        worldHandle.SetTransform(Matrix3x2.Identity);
        worldHandle.UseShader(_protoManager.Index(StencilMask).Instance());
        worldHandle.DrawTextureRect(res.Blep!.Texture, worldBounds);
        var curTime = _timing.RealTime;
        var sprite = _sprite.GetFrame(new SpriteSpecifier.Texture(new ResPath("/Textures/Parallaxes/noise.png")), curTime);

        worldHandle.UseShader(_protoManager.Index(StencilDraw).Instance());
        _parallax.DrawParallax(worldHandle, worldAABB, sprite, curTime, position, new Vector2(0.5f, 0f));
    }
}
