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
///
///     It owns no render targets. It runs after the ordinary AO has been drawn for the frame, so it borrows that AO's
///     target and blur buffer, which are idle until the next frame.
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
        if (res.AOTexture == null || res.MaskTexture == null)
            return false;

        aoTexture = res.AOTexture;
        maskTexture = res.MaskTexture;
        res.AOTexture = null;
        res.MaskTexture = null;
        return true;
    }

    /// <param name="aoTarget">Borrowed for the item AO; free once the ordinary AO has been drawn.</param>
    /// <param name="blurBuffer">Borrowed as the blur's scratch, then for the mask.</param>
    private void DrawSurfaceAmbientOcclusion(
        DrawingHandleWorld worldHandle,
        IClydeViewport viewport,
        IRenderTexture aoTarget,
        IRenderTexture blurBuffer,
        Vector2 scale,
        EntityLookupSystem lookupSystem)
    {
        var res = _surfaceResources.GetForViewport(viewport, static _ => new SurfaceResources());
        res.AOTexture = null;
        res.MaskTexture = null;

        if (_surfaceSilhouettes.Count == 0 || _itemSilhouettes.Count == 0)
            return;

        _surfaceBounds.Clear();
        foreach (var surface in _surfaceSilhouettes)
            _surfaceBounds.Add(lookupSystem.GetWorldAABB(surface.SpriteEntity.Owner));

        _itemsOnSurfaces.Clear();
        foreach (var item in _itemSilhouettes)
        {
            // Drawn before the surface AO, so its AO would be drawn over it (a recharger at SmallObjects, say).
            if (item.SpriteEntity.Comp.DrawDepth < Content.Client._KS14.Light.KsSurfaceAmbientOcclusionOverlay.OverlayZIndex)
                continue;

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

        var eye = viewport.Eye!;
        var eyeRotation = eye.Rotation;
        var worldToTargetMatrix = aoTarget.GetWorldToLocalMatrix(eye, scale);
        var spriteSystem = _entManager.System<SpriteSystem>();
        var silhouetteShader = _proto.Index(SilhouetteShader).Instance();

        worldHandle.RenderInRenderTarget(aoTarget,
            () =>
            {
                worldHandle.UseShader(silhouetteShader);
                foreach (var item in _itemsOnSurfaces)
                    DrawSilhouette(worldHandle, spriteSystem, item.SpriteEntity, item.WorldPosition, item.WorldRotation, eyeRotation, worldToTargetMatrix);
            }, Color.Transparent);

        _clyde.BlurRenderTarget(viewport, aoTarget, blurBuffer, eye, SurfaceBlurMultiplier);

        worldHandle.RenderInRenderTarget(blurBuffer,
            () =>
            {
                worldHandle.UseShader(silhouetteShader);
                foreach (var surface in _surfaceSilhouettes)
                    DrawSilhouette(worldHandle, spriteSystem, surface.SpriteEntity, surface.WorldPosition, surface.WorldRotation, eyeRotation, worldToTargetMatrix);
            }, Color.Transparent);

        worldHandle.SetTransform(Matrix3x2.Identity);
        worldHandle.UseShader(null);
        res.AOTexture = aoTarget.Texture;
        res.MaskTexture = blurBuffer.Texture;
    }

    /// <summary>
    ///     What this frame built for a viewport, until it is drawn. Owns nothing: both are the ordinary AO's targets.
    /// </summary>
    private sealed class SurfaceResources : IDisposable
    {
        public Texture? AOTexture;
        public Texture? MaskTexture;

        public void Dispose()
        {
        }
    }
}
