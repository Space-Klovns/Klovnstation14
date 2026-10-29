using System.Numerics;
using Content.Client.Graphics;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;

namespace Content.Client.Light;

/// <summary>
///     The AO of items resting on placeable surfaces (tables), drawn onto the surface itself.
/// </summary>
/// <remarks>
///     The ordinary AO is drawn below every entity, so a table's sprite hides the AO of what is on it. This builds two
///     textures for <see cref="_KS14.Light.KsSurfaceAmbientOcclusionOverlay"/>, which draws above surfaces and below
///     items: the blurred AO of the items on surfaces, and the surfaces' silhouettes to mask it with. Masking is a
///     shader rather than the stencil, which already holds the grid and FOV masks by then.
/// </remarks>
public sealed partial class AmbientOcclusionOverlay
{
    // Items are small, so their AO is tighter than a wall's.
    private const float SurfaceBlurMultiplier = 6f;

    private readonly List<SilhouetteDatum> _itemsOnSurfaces = new();
    private readonly List<Box2> _surfaceBounds = new();

    private readonly OverlayResourceCache<SurfaceResources> _surfaceResources = new();

    /// <summary>
    ///     Takes the textures built for <paramref name="viewport"/> this frame, if there are any. They are handed out
    ///     once, so a viewport this overlay skipped draws nothing stale.
    /// </summary>
    public bool TryTakeSurfaceTextures(IClydeViewport viewport, out Texture aoTexture, out Texture maskTexture)
    {
        aoTexture = default!;
        maskTexture = default!;

        var res = _surfaceResources.GetForViewport(viewport, static _ => new SurfaceResources());
        if (!res.Ready || res.AOTarget == null || res.MaskTarget == null)
            return false;

        res.Ready = false;
        aoTexture = res.AOTarget.Texture;
        maskTexture = res.MaskTarget.Texture;
        return true;
    }

    private void DrawSurfaceAmbientOcclusion(
        DrawingHandleWorld worldHandle,
        IClydeViewport viewport,
        IRenderTexture blurBuffer,
        Vector2 scale,
        EntityLookupSystem lookupSystem)
    {
        var res = _surfaceResources.GetForViewport(viewport, static _ => new SurfaceResources());
        res.Ready = false;

        if (_surfaceSilhouettes.Count == 0 || _itemSilhouettes.Count == 0)
            return;

        _surfaceBounds.Clear();
        foreach (var surface in _surfaceSilhouettes)
            _surfaceBounds.Add(lookupSystem.GetWorldAABB(surface.Uid));

        _itemsOnSurfaces.Clear();
        foreach (var item in _itemSilhouettes)
        {
            foreach (var bounds in _surfaceBounds)
            {
                if (!bounds.Contains(item.WorldPosition))
                    continue;

                _itemsOnSurfaces.Add(item);
                break;
            }
        }

        if (_itemsOnSurfaces.Count == 0)
            return;

        var targetSize = viewport.RenderTarget.Size;
        if (res.AOTarget?.Texture.Size != targetSize)
        {
            res.AOTarget?.Dispose();
            res.AOTarget = _clyde.CreateRenderTarget(targetSize, new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb), name: "surface-ambient-occlusion-target");
        }

        if (res.MaskTarget?.Texture.Size != targetSize)
        {
            res.MaskTarget?.Dispose();
            res.MaskTarget = _clyde.CreateRenderTarget(targetSize, new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb), name: "surface-ambient-occlusion-mask-target");
        }

        var eye = viewport.Eye!;
        var eyeRotation = eye.Rotation;
        var worldToTargetMatrix = res.AOTarget.GetWorldToLocalMatrix(eye, scale);
        var spriteSystem = _entManager.System<SpriteSystem>();
        var silhouetteShader = _proto.Index(SilhouetteShader).Instance();

        worldHandle.RenderInRenderTarget(res.AOTarget,
            () =>
            {
                worldHandle.UseShader(silhouetteShader);
                foreach (var item in _itemsOnSurfaces)
                    DrawSilhouette(worldHandle, spriteSystem, item.SpriteComponent, item.WorldPosition, item.WorldRotation, eyeRotation, worldToTargetMatrix);
            }, Color.Transparent);

        _clyde.BlurRenderTarget(viewport, res.AOTarget, blurBuffer, eye, SurfaceBlurMultiplier);

        worldHandle.RenderInRenderTarget(res.MaskTarget,
            () =>
            {
                worldHandle.UseShader(silhouetteShader);
                foreach (var surface in _surfaceSilhouettes)
                    DrawSilhouette(worldHandle, spriteSystem, surface.SpriteComponent, surface.WorldPosition, surface.WorldRotation, eyeRotation, worldToTargetMatrix);
            }, Color.Transparent);

        worldHandle.SetTransform(Matrix3x2.Identity);
        worldHandle.UseShader(null);
        res.Ready = true;
    }

    private sealed class SurfaceResources : IDisposable
    {
        public IRenderTexture? AOTarget;
        public IRenderTexture? MaskTarget;

        /// <summary>
        ///     Whether the textures were built this frame and not yet drawn.
        /// </summary>
        public bool Ready;

        public void Dispose()
        {
            AOTarget?.Dispose();
            MaskTarget?.Dispose();
        }
    }
}
