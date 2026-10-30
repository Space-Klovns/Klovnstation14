using System.Numerics;
using Content.Client._KS14.Light;
using Content.Shared.Placeable;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Utility;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client.Light;

public sealed partial class AmbientOcclusionOverlay
{
    // Draws the sprite's alpha as white, which is what the AO target expects.
    private static readonly ProtoId<ShaderPrototype> SilhouetteShader = "KsWhite";

    // The AO is blurred into a soft gradient, so it loses nothing visible at half resolution, while drawing, blurring
    //      and sampling it costs a quarter. The blur's radius is in texture coordinates, so it is unchanged.
    private const int TargetResolutionDivisor = 2;

    // A pile of items blurs into one blob: past the first few on a tile, another silhouette changes nothing visible.
    private const int MaxItemsPerTile = 3;

    // Barely visible sprites (faded wallmounts, cloaked things) cast nothing visible either.
    private const float MinCasterAlpha = 0.1f;

    // Items drawn so far this frame per world tile, for MaxItemsPerTile.
    private readonly Dictionary<Vector2i, int> _itemsPerTile = new();

    private readonly List<Entity<KsAmbientOcclusionCasterComponent, TransformComponent>> _cachedCasters = new();

    // Gathered by DrawEntityAmbientOcclusion for the surface pass; see AmbientOcclusionOverlay.Klovn.Surfaces.cs.
    private readonly List<SilhouetteDatum> _surfaceSilhouettes = new();
    private readonly List<SilhouetteDatum> _itemSilhouettes = new();

    private EntityQuery<SpriteComponent> _spriteQuery;
    private EntityQuery<PlaceableSurfaceComponent> _placeableSurfaceQuery;
    private bool _queriesResolved;

    /// <summary>
    ///     Draws the sprite silhouette of every caster in view into the AO target.
    /// </summary>
    /// <remarks>
    ///     <see cref="KsAmbientOcclusionCasterSystem"/> has already left out everything that can never cast, so only what
    ///         can change at runtime is checked here: visibility, draw depth, and unshaded layers.
    /// </remarks>
    private void DrawEntityAmbientOcclusion(
        DrawingHandleWorld worldHandle,
        MapId mapId,
        Box2 worldAabb,
        Matrix3x2 worldToTargetMatrix,
        Angle eyeRotation,
        SharedTransformSystem transformSystem)
    {
        _surfaceSilhouettes.Clear();
        _itemSilhouettes.Clear();

        if (!_drawEntities)
            return;

        if (!_queriesResolved)
        {
            _spriteQuery = _entManager.GetEntityQuery<SpriteComponent>();
            _placeableSurfaceQuery = _entManager.GetEntityQuery<PlaceableSurfaceComponent>();
            _queriesResolved = true;
        }

        _cachedCasters.Clear();
        _itemsPerTile.Clear();
        _entManager.System<KsAmbientOcclusionCasterSystem>().QueryAabb(_cachedCasters, mapId, worldAabb);

        if (_cachedCasters.Count == 0)
            return;

        var spriteSystem = _entManager.System<SpriteSystem>();
        worldHandle.UseShader(_proto.Index(SilhouetteShader).Instance());

        foreach (var (uid, casterComponent, transformComponent) in _cachedCasters)
        {
            if (!_spriteQuery.TryComp(uid, out var spriteComponent)
                || !spriteComponent.Visible
                || spriteComponent.DrawDepth >= KsAmbientOcclusionCasterSystem.MinEffectDrawDepth
                || spriteComponent.Color.A < MinCasterAlpha)
                continue;

            var spriteEntity = new Entity<SpriteComponent>(uid, spriteComponent);
            var (worldPosition, worldRotation) = transformSystem.GetWorldPositionRotation(transformComponent);

            if (casterComponent.IsItem)
            {
                var tile = new Vector2i((int)MathF.Floor(worldPosition.X), (int)MathF.Floor(worldPosition.Y));
                _itemsPerTile.TryGetValue(tile, out var itemCount);
                if (itemCount >= MaxItemsPerTile)
                    continue;

                _itemsPerTile[tile] = itemCount + 1;
            }

            // Nothing drawn means the sprite is all light (holograms, glowing signs), with no body to occlude with.
            if (!DrawSilhouette(worldHandle, spriteSystem, spriteEntity, worldPosition, worldRotation, eyeRotation, worldToTargetMatrix))
                continue;

            if (casterComponent.IsItem)
                _itemSilhouettes.Add(new SilhouetteDatum(spriteEntity, worldPosition, worldRotation));
            else if (casterComponent.IsSurface
                && _placeableSurfaceQuery.TryComp(uid, out var placeableSurfaceComponent)
                && placeableSurfaceComponent.IsPlaceable)
                _surfaceSilhouettes.Add(new SilhouetteDatum(spriteEntity, worldPosition, worldRotation));
        }

        worldHandle.SetTransform(Matrix3x2.Identity);
        worldHandle.UseShader(_proto.Index(UnshadedShader).Instance());
    }

    private static Vector2i GetAmbientOcclusionTargetSize(Vector2i viewportTargetSize)
    {
        return new Vector2i(
            Math.Max(1, viewportTargetSize.X / TargetResolutionDivisor),
            Math.Max(1, viewportTargetSize.Y / TargetResolutionDivisor));
    }

    private readonly record struct SilhouetteDatum(Entity<SpriteComponent> SpriteEntity, Vector2 WorldPosition, Angle WorldRotation);

    /// <summary>
    ///     Draws every layer of a sprite with the current shader, in the AO target's frame.
    /// </summary>
    /// <returns>Whether any layer was drawn; a sprite whose every layer is unshaded draws none.</returns>
    /// <remarks>
    ///     Not <see cref="SpriteSystem.RenderSprite"/>: that sets the handle's transform per layer, discarding the
    ///     world-to-target matrix, so everything lands outside the target. This is its layer loop minus layer
    ///     shaders and colours, which the silhouette doesn't want anyway.
    /// </remarks>
    private static bool DrawSilhouette(
        DrawingHandleWorld worldHandle,
        SpriteSystem spriteSystem,
        Entity<SpriteComponent> spriteEntity,
        Vector2 worldPosition,
        Angle worldRotation,
        Angle eyeRotation,
        Matrix3x2 worldToTargetMatrix)
    {
        var spriteComponent = spriteEntity.Comp;
        var angle = (worldRotation + eyeRotation).Reduced().FlipPositive();
        var cardinal = spriteComponent is { NoRotation: false, SnapCardinals: true } ? angle.RoundToCardinalAngle() : Angle.Zero;
        var entityMatrix = Matrix3Helpers.CreateTransform(worldPosition, spriteComponent.NoRotation ? -eyeRotation : worldRotation - cardinal);
        var spriteToTargetMatrix = spriteComponent.LocalMatrix * entityMatrix * worldToTargetMatrix;

        Direction? overrideDirection = spriteComponent.EnableDirectionOverride ? spriteComponent.DirectionOverride : null;
        var drewAny = false;

        // AllLayers is the sprite's own List<Layer>; indexing it avoids boxing an enumerator for every entity, every frame.
        if (spriteComponent.AllLayers is not IReadOnlyList<ISpriteLayer> spriteLayers)
            return false;

        for (var i = 0; i < spriteLayers.Count; i++)
        {
            if (spriteLayers[i] is not SpriteComponent.Layer layer
                || !layer.Visible
                || layer.Blank
                || layer.CopyToShaderParameters != null
                || layer.ShaderPrototype == SpriteSystem.UnshadedId) // light, not a body: a screen or lamp casts nothing
                continue;

            var state = layer.ActualState;
            var direction = state == null ? RsiDirection.South : SpriteComponent.Layer.GetDirection(state.RsiDirections, angle);
            layer.GetLayerDrawMatrix(direction, out var layerMatrix);

            if (overrideDirection != null && state != null)
                direction = overrideDirection.Value.Convert(state.RsiDirections);
            direction = direction.OffsetRsiDir(layer.DirOffset);

            var texture = state?.GetFrame(direction, layer.AnimationFrame) ?? layer.Texture ?? spriteSystem.GetFallbackTexture();
            var textureSize = texture.Size / (float)EyeManager.PixelsPerMeter;

            worldHandle.SetTransform(layerMatrix * spriteToTargetMatrix);
            worldHandle.DrawTextureRect(texture, Box2.FromDimensions(textureSize / -2f, textureSize));
            drewAny = true;
        }

        return drewAny;
    }
}
