using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Client.Graphics;
using Content.Client.Light;
using Content.Shared._KS14;
using Content.Shared._KS14.Emissive;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Utility;
using Robust.Shared.Enums;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;
using Robust.Shared.Reflection;

namespace Content.Client._KS14.Emissive;

public sealed partial class EmissiveOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> Shader = "Emissive";

    public override OverlaySpace Space => OverlaySpace.BeforeLighting;

    [Dependency] private EntityManager _entityManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IReflectionManager _reflectionManager = default!;
    [Dependency] private IOverlayManager _overlay = default!;
    [Dependency] private IClyde _clyde = default!;

    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;
    [Dependency] private Content.Shared.Examine.ExamineSystemShared _examineSystem = default!;

    [Dependency] private EntityQuery<SpriteComponent> _spriteQuery = default!;
    [Dependency] private EntityQuery<EmissiveTreeComponent> _emissiveTreeQuery = default!;

    private List<ComponentTreeEntry<EmissiveLayersComponent>> _entities = []; // not readonly: the tree query takes it by ref

    /// <summary>
    ///     One shader per intensity, since the intensity is a parameter and a parameter needs its own instance. Shared by
    ///         every entity with that intensity, so their draws batch together instead of each breaking the batch.
    /// </summary>
    private readonly Dictionary<float, ShaderInstance> _shadersByIntensity = [];
    private List<Entity<MapGridComponent>> _grids = [];

    private readonly OverlayResourceCache<CachedResources> _resources = new();

    // Lower z-levels are rendered through the same viewport every frame, so they keep their own, smaller bloom targets
    //      rather than resizing the viewer's twice a frame.
    private readonly OverlayResourceCache<CachedResources> _lowerLevelResources = new();

    /// <summary>
    ///     How much smaller the bloom target is on a lower z-level, which is scaled down and blurred afterwards anyway.
    /// </summary>
    private const int LowerLevelBloomDivisor = 4;
    private readonly List<EmissiveDraw> _draws = [];

    public const int ContentZIndex = BeforeLightTargetOverlay.ContentZIndex + 2;

    public EmissiveOverlay()
    {
        ZIndex = ContentZIndex;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (args.Viewport.Eye == null)
            return false;

        return true;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var mapId = args.MapId;
        var worldHandle = args.WorldHandle;

        var lightOverlay = _overlay.GetOverlay<BeforeLightTargetOverlay>();
        var bounds = lightOverlay.EnlargedBounds;
        var lightTarget = lightOverlay.GetCachedForViewport(args.Viewport).EnlargedLightTarget;

        var viewport = args.Viewport;
        var resources = GetResources(viewport);
        resources.Drawn = false;

        _grids.Clear();
        _mapSystem.FindGridsIntersecting(mapId, bounds, ref _grids, approx: true);

        if (_grids.Count == 0)
            return;

        var lightScale = viewport.LightRenderTarget.Size / (Vector2)viewport.Size;
        var scale = viewport.RenderScale / (Vector2.One / lightScale);

        var eyeRotation = args.Viewport.Eye?.Rotation ?? new();

        // Only bloom is culled by sight, and only when it's drawn and the eye has FOV to match.
        var cullBloom = viewport.Eye!.DrawFov
            && OverlayManager.TryGetOverlay<KsEmissiveBloomOverlay>(out var bloomOverlayForCull)
            && bloomOverlayForCull.Active;
        var eyeCoordinates = viewport.Eye.Position;

        // The query and every layer's maths run once, into a list of draws, which is then replayed: into the light
        //      target enlarged by the glow radius, which lights the floor around it, and, for KsEmissiveBloomOverlay,
        //      into the bloom target at the sprite's own size. The enlarged quads can't be the bloom: added over the
        //      lit world, they'd wash it out.
        _draws.Clear();
        {
            foreach (var grid in _grids)
            {
                var gridInvMatrix = _transformSystem.GetInvWorldMatrix(grid);
                var localBounds = gridInvMatrix.TransformBox(bounds);

                // The grid's own tree of emissives, rather than every entity in view filtered for the component.
                if (!_emissiveTreeQuery.TryComp(grid.Owner, out var treeComponent))
                    continue;

                _entities.Clear();
                treeComponent.Tree.QueryAabb(ref _entities,
                    static (ref List<ComponentTreeEntry<EmissiveLayersComponent>> entities, in ComponentTreeEntry<EmissiveLayersComponent> entry) =>
                    {
                        entities.Add(entry);
                        return true;
                    },
                    localBounds,
                    approx: true);

                if (_entities.Count == 0)
                    continue;

                var gridWorldMatrix = _transformSystem.GetWorldMatrix(grid.Owner);

                var localEyeRotation = eyeRotation - gridInvMatrix.Rotation();

                foreach (var entry in _entities)
                {
                    var ent = new Entity<EmissiveLayersComponent>(entry.Uid, entry.Component);
                    var spriteComponent = _spriteQuery.GetComponent(ent);
                    if (!spriteComponent.Visible)
                        continue;

                    var shader = GetShader(ent.Comp.Intensity);

                    // The engine's own sprite and layer matrices, as SpriteSystem.RenderSprite builds them, so the glow sits
                    //      exactly on the layer: noRot, cardinal snapping, and the sprite's and layer's offset, rotation and
                    //      scale all included. In the grid's frame, so the eye's rotation is made relative to the grid.
                    var transformComponent = entry.Transform;
                    var localRotation = transformComponent.LocalRotation;
                    var angle = (localRotation + localEyeRotation).Reduced().FlipPositive();
                    var cardinal = spriteComponent is { NoRotation: false, SnapCardinals: true } ? angle.RoundToCardinalAngle() : Angle.Zero;
                    var entityMatrix = Matrix3Helpers.CreateTransform(transformComponent.LocalPosition,
                        spriteComponent.NoRotation ? -localEyeRotation : localRotation - cardinal);
                    var spriteToGridMatrix = spriteComponent.LocalMatrix * entityMatrix;
                    var spriteToWorldMatrix = spriteToGridMatrix * gridWorldMatrix;

                    // Moves only the light, along the entity's own facing whatever the sprite does, e.g. out from a wall.
                    var lightSpriteToWorldMatrix = ent.Comp.LightOffset == Vector2.Zero
                        ? spriteToWorldMatrix
                        : spriteToGridMatrix * Matrix3x2.CreateTranslation(localRotation.RotateVec(ent.Comp.LightOffset)) * gridWorldMatrix;

                    // Or the light ignores the sprite and layers entirely: at the entity, turned by its rotation alone.
                    var lightEntityToWorldMatrix = Matrix3Helpers.CreateTransform(transformComponent.LocalPosition, localRotation) * gridWorldMatrix;

                    // A halo is blurred in screen space, so it would spread past a wall onto its far side. Bloom only what
                    //      the eye can see, like FOV does; an emitter inside the occluder it's mounted on still counts.
                    var inView = !cullBloom || _examineSystem.InRangeUnOccluded(eyeCoordinates,
                        new MapCoordinates(Vector2.Transform(transformComponent.LocalPosition, gridWorldMatrix), mapId),
                        0f,
                        0,
                        static (_, _) => false);

                    Direction? overrideDirection = spriteComponent.EnableDirectionOverride ? spriteComponent.DirectionOverride : null;
                    var spriteColor = spriteComponent.Color;
                    foreach (var layerId in ent.Comp.Layers)
                    {
                        // insanity
                        SpriteComponent.Layer? layer = null;
                        var layerEnumKey = KsEnumHelpers.ParseKey(layerId, out var isKeyEnum, _reflectionManager);
                        if (isKeyEnum)
                        {
                            if (!_spriteSystem.TryGetLayer((ent.Owner, spriteComponent), layerEnumKey!, out layer, false))
                                continue;
                        }
                        else if (!_spriteSystem.TryGetLayer((ent.Owner, spriteComponent), layerId, out layer, false))
                            continue;

                        if (!layer.Visible)
                            continue;

                        var state = layer.ActualState;
                        var direction = state == null ? RsiDirection.South : SpriteComponent.Layer.GetDirection(state.RsiDirections, angle);
                        layer.GetLayerDrawMatrix(direction, out var layerMatrix);

                        if (overrideDirection != null && state != null)
                            direction = overrideDirection.Value.Convert(state.RsiDirections);
                        direction = direction.OffsetRsiDir(layer.DirOffset);

                        var texture = state?.GetFrame(direction, layer.AnimationFrame) ?? layer.Texture ?? _spriteSystem.GetFallbackTexture();

                        ent.Comp.LayerParams.TryGetValue(layerId, out var layerParams);

                        // In the layer's own frame, centred on it, as the engine draws it.
                        var bloomBox = Box2.CenteredAround(ent.Comp.Offset, texture.Size / (float)EyeManager.PixelsPerMeter);
                        var glowRadius = layerParams?.GlowRadius ?? ent.Comp.GlowRadius;
                        var textureSize = texture.Size / (float)EyeManager.PixelsPerMeter;
                        var lightBox = ent.Comp.LightFollowsEntity
                            ? Box2.CenteredAround(ent.Comp.LightOffset, textureSize).Enlarged(glowRadius)
                            : bloomBox.Enlarged(glowRadius);

                        // The colour drawn is the colour emitted, so scaling it dims this layer's glow alone
                        var color = spriteColor * layer.Color;
                        if (layerParams is { Brightness: var brightness } && brightness != 1f)
                            color = new Color(color.R * brightness, color.G * brightness, color.B * brightness, color.A);

                        _draws.Add(new EmissiveDraw(ent.Comp.LightFollowsEntity ? lightEntityToWorldMatrix : layerMatrix * lightSpriteToWorldMatrix,
                            layerMatrix * spriteToWorldMatrix,
                            texture,
                            lightBox,
                            bloomBox,
                            color,
                            shader,
                            inView));
                    }
                }
            }
        }

        if (_draws.Count == 0)
            return;

        var lightMatrix = lightTarget.GetWorldToLocalMatrix(viewport.Eye!, scale);
        worldHandle.RenderInRenderTarget(lightTarget,
            () =>
            {
                ShaderInstance? currentShader = null;
                foreach (var draw in _draws)
                {
                    if (draw.Shader != currentShader)
                    {
                        worldHandle.UseShader(draw.Shader);
                        currentShader = draw.Shader;
                    }

                    worldHandle.SetTransform(draw.LightToWorldMatrix * lightMatrix);
                    worldHandle.DrawTextureRect(draw.Texture, draw.LightBox, modulate: draw.Color);
                }

                worldHandle.UseShader(null);
            }, null);

        if (!OverlayManager.TryGetOverlay<KsEmissiveBloomOverlay>(out var bloomOverlay) || !bloomOverlay.Active)
            return;

        // The light target's frame, at its size or, on a lower z-level, a fraction of it. Made only once bloom is on.
        var divisor = Content.Client._KS14.ZLevel.KsZLevelRenderPass.LowerLevel ? LowerLevelBloomDivisor : 1;
        var targetSize = Vector2i.ComponentMax(lightTarget.Size / divisor, Vector2i.One);
        if (resources.Target?.Size != targetSize)
        {
            resources.Target?.Dispose();
            resources.BlurBuffer?.Dispose();
            var format = new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb);
            resources.Target = _clyde.CreateRenderTarget(targetSize, format, name: "emissive-target");
            resources.BlurBuffer = _clyde.CreateRenderTarget(targetSize, format, name: "emissive-blur-target");
        }

        var target = resources.Target;
        var bloomMatrix = target.GetWorldToLocalMatrix(viewport.Eye!, scale / divisor);

        // The part of the target the viewport covers: the light target's skirt is cropped off, as AfterLightTargetOverlay does.
        var viewportLightSize = viewport.LightRenderTarget.Size / divisor;
        var halfDiff = (targetSize - viewportLightSize) / 2;
        resources.SubRegion = new UIBox2i(halfDiff.X, halfDiff.Y, viewportLightSize.X + halfDiff.X, viewportLightSize.Y + halfDiff.Y);

        resources.Drawn = true;
        worldHandle.RenderInRenderTarget(target,
            () =>
            {
                // Unlit, so it's the layer's own colour. Blended over black, that's colour times alpha, so transparent pixels
                //      add nothing to the blur. The bloom overlay blurs it and adds it on.
                worldHandle.UseShader(_prototypeManager.Index(SpriteSystem.UnshadedId).Instance());
                foreach (var draw in _draws)
                {
                    if (!draw.InView)
                        continue;

                    worldHandle.SetTransform(draw.LayerToWorldMatrix * bloomMatrix);
                    worldHandle.DrawTextureRect(draw.Texture, draw.BloomBox, modulate: draw.Color);
                }

                worldHandle.UseShader(null);
            }, new Color(0f, 0f, 0f, 0f)); // not Color.Transparent, which is white at zero alpha
    }

    private readonly record struct EmissiveDraw(
        Matrix3x2 LightToWorldMatrix,
        Matrix3x2 LayerToWorldMatrix,
        Texture Texture,
        Box2 LightBox,
        Box2 BloomBox,
        Color Color,
        ShaderInstance Shader,
        bool InView);

    private CachedResources GetResources(IClydeViewport viewport)
    {
        var cache = Content.Client._KS14.ZLevel.KsZLevelRenderPass.LowerLevel ? _lowerLevelResources : _resources;
        return cache.GetForViewport(viewport, static _ => new CachedResources());
    }

    /// <summary>
    ///     This pass's emissive glow for a viewport, for <see cref="KsEmissiveBloomOverlay"/>, once. False if nothing
    ///         glowed, or lighting wasn't rendered this pass.
    /// </summary>
    public bool TryGetGlow(IClydeViewport viewport,
        [NotNullWhen(true)] out IRenderTexture? target,
        [NotNullWhen(true)] out IRenderTexture? blurBuffer,
        out UIBox2i subRegion)
    {
        var resources = GetResources(viewport);
        target = resources.Target;
        blurBuffer = resources.BlurBuffer;
        subRegion = resources.SubRegion;

        // Taken, not just read: this overlay only runs while lighting is rendered, so with lighting off nothing would
        //      reset it, and the last glow would stay on screen.
        var drawn = resources.Drawn;
        resources.Drawn = false;
        return drawn && target != null && blurBuffer != null;
    }

    public sealed class CachedResources : IDisposable
    {
        /// <summary>
        ///     The glow, in the enlarged light target's frame.
        /// </summary>
        public IRenderTexture? Target;

        /// <summary>
        ///     Scratch for blurring <see cref="Target"/>.
        /// </summary>
        public IRenderTexture? BlurBuffer;

        /// <summary>
        ///     The part of <see cref="Target"/>, in pixels, that the viewport covers.
        /// </summary>
        public UIBox2i SubRegion;

        public bool Drawn;

        public void Dispose()
        {
            Target?.Dispose();
            BlurBuffer?.Dispose();
        }
    }

    private ShaderInstance GetShader(float intensity)
    {
        // GetValueRefOrAddDefault isnt sandboxed so GEG
        if (!_shadersByIntensity.TryGetValue(intensity, out var shader))
        {
            shader = _prototypeManager.Index(Shader).InstanceUnique();
            shader.SetParameter("bloom_intensity", intensity);
            _shadersByIntensity[intensity] = shader;
        }

        return shader;
    }

    protected override void DisposeBehavior()
    {
        foreach (var shader in _shadersByIntensity.Values)
            shader.Dispose();

        _shadersByIntensity.Clear();
        _resources.Dispose();
        _lowerLevelResources.Dispose();
        base.DisposeBehavior();
    }
}
