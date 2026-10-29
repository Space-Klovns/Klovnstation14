using System.Numerics;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Light;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;
using Content.Shared.Item;
using Content.Shared.Mobs.Components;
using Content.Shared.Placeable;
using Content.Shared.Projectiles;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Utility;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;

namespace Content.Client.Light;

public sealed partial class AmbientOcclusionOverlay
{
    // Draws the sprite's alpha as white, which is what the AO target expects.
    private static readonly ProtoId<ShaderPrototype> SilhouetteShader = "KsWhite";

    // Anything drawn this low lies flat on the floor (catwalks, water, puddles, pipes), so casts nothing.
    private const int MaxFlatDrawDepth = (int)DrawDepth.HighFloorObjects;

    // Gases, effects and above aren't solid.
    private const int MinEffectDrawDepth = (int)DrawDepth.Gasses;

    private readonly HashSet<EntityUid> _cachedEntities = new();

    // Gathered by DrawEntityAmbientOcclusion for the surface pass; see AmbientOcclusionOverlay.Klovn.Surfaces.cs.
    private readonly List<SilhouetteDatum> _surfaceSilhouettes = new();
    private readonly List<SilhouetteDatum> _itemSilhouettes = new();

    private EntityQuery<OccluderComponent> _occluderQuery;
    private EntityQuery<MobStateComponent> _mobStateQuery;
    private EntityQuery<ProjectileComponent> _projectileQuery;
    private EntityQuery<ItemComponent> _itemQuery;
    private EntityQuery<FixturesComponent> _fixturesQuery;
    private EntityQuery<SpriteComponent> _spriteQuery;
    private EntityQuery<TransformComponent> _transformQuery;
    private EntityQuery<KsNoAmbientOcclusionComponent> _noAmbientOcclusionQuery;
    private EntityQuery<KsForceAmbientOcclusionComponent> _forceAmbientOcclusionQuery;
    private EntityQuery<PlaceableSurfaceComponent> _placeableSurfaceQuery;
    private bool _queriesResolved;

    /// <summary>
    ///     Draws the sprite silhouette of every uncontained item and structure in view into the AO target.
    /// </summary>
    private void DrawEntityAmbientOcclusion(
        DrawingHandleWorld worldHandle,
        MapId mapId,
        Box2 worldAabb,
        Matrix3x2 worldToTargetMatrix,
        Angle eyeRotation,
        EntityLookupSystem lookupSystem,
        SharedTransformSystem transformSystem)
    {
        _surfaceSilhouettes.Clear();
        _itemSilhouettes.Clear();

        if (!_cfgManager.GetCVar(KsCCVars.AmbientOcclusionEntities))
            return;

        if (!_queriesResolved)
        {
            _occluderQuery = _entManager.GetEntityQuery<OccluderComponent>();
            _mobStateQuery = _entManager.GetEntityQuery<MobStateComponent>();
            _projectileQuery = _entManager.GetEntityQuery<ProjectileComponent>();
            _itemQuery = _entManager.GetEntityQuery<ItemComponent>();
            _fixturesQuery = _entManager.GetEntityQuery<FixturesComponent>();
            _spriteQuery = _entManager.GetEntityQuery<SpriteComponent>();
            _transformQuery = _entManager.GetEntityQuery<TransformComponent>();
            _noAmbientOcclusionQuery = _entManager.GetEntityQuery<KsNoAmbientOcclusionComponent>();
            _placeableSurfaceQuery = _entManager.GetEntityQuery<PlaceableSurfaceComponent>();
            _forceAmbientOcclusionQuery = _entManager.GetEntityQuery<KsForceAmbientOcclusionComponent>();
            _queriesResolved = true;
        }

        // Uncontained only: held and stored entities are never drawn, so never looked at either.
        //      Sundries matter: a body that can't collide (as a dropped item is, once the server's state lands) is filed there, not in the dynamic tree.
        _cachedEntities.Clear();
        lookupSystem.GetEntitiesIntersecting(mapId,
            worldAabb,
            _cachedEntities,
            LookupFlags.Uncontained | LookupFlags.Approximate);

        if (_cachedEntities.Count == 0)
            return;

        var spriteSystem = _entManager.System<SpriteSystem>();
        worldHandle.UseShader(_proto.Index(SilhouetteShader).Instance());

        foreach (var uid in _cachedEntities)
        {
            // Sprite and draw depth first: most of what the lookup returns (decals, cables, pipes, puddles) fails here,
            //      before any of the component lookups below.
            if (!_spriteQuery.TryComp(uid, out var spriteComponent)
                || !spriteComponent.Visible
                || spriteComponent.DrawDepth >= MinEffectDrawDepth)
                continue;

            var forced = _forceAmbientOcclusionQuery.HasComp(uid);
            if (!forced && spriteComponent.DrawDepth <= MaxFlatDrawDepth)
                continue;

            if (_occluderQuery.HasComp(uid)
                || _mobStateQuery.HasComp(uid)
                || _projectileQuery.HasComp(uid)
                || _noAmbientOcclusionQuery.HasComp(uid)
                || !_transformQuery.TryComp(uid, out var transformComponent))
                continue;

            // Structures block movement, anchored or not (closets, crates); that leaves out decals, cables and pipes.
            var isItem = _itemQuery.HasComp(uid);
            if (!forced && !isItem && !HasHardFixture(uid))
                continue;

            var spriteEntity = new Entity<SpriteComponent>(uid, spriteComponent);
            var (worldPosition, worldRotation) = transformSystem.GetWorldPositionRotation(transformComponent);

            // Nothing drawn means the sprite is all light (holograms, glowing signs), with no body to occlude with.
            if (!DrawSilhouette(worldHandle, spriteSystem, spriteEntity, worldPosition, worldRotation, eyeRotation, worldToTargetMatrix))
                continue;

            if (isItem)
                _itemSilhouettes.Add(new SilhouetteDatum(spriteEntity, worldPosition, worldRotation));
            else if (_placeableSurfaceQuery.TryComp(uid, out var placeableSurfaceComponent) && placeableSurfaceComponent.IsPlaceable)
                _surfaceSilhouettes.Add(new SilhouetteDatum(spriteEntity, worldPosition, worldRotation));
        }

        worldHandle.SetTransform(Matrix3x2.Identity);
        worldHandle.UseShader(_proto.Index(UnshadedShader).Instance());
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

        foreach (var spriteLayer in spriteComponent.AllLayers)
        {
            if (spriteLayer is not SpriteComponent.Layer layer
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

    private bool HasHardFixture(EntityUid uid)
    {
        if (!_fixturesQuery.TryComp(uid, out var fixturesComponent))
            return false;

        foreach (var fixture in fixturesComponent.Fixtures.Values)
        {
            if (fixture.Hard)
                return true;
        }

        return false;
    }
}
