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
        var resources = _resources.GetForViewport(viewport, static _ => new CachedResources());
        resources.Drawn = false;
        resources.Bounds = bounds;

        _grids.Clear();
        _mapSystem.FindGridsIntersecting(mapId, bounds, ref _grids, approx: true);

        if (_grids.Count == 0)
            return;

        var lightScale = viewport.LightRenderTarget.Size / (Vector2)viewport.Size;
        var scale = viewport.RenderScale / (Vector2.One / lightScale);

        var eyeRotation = args.Viewport.Eye?.Rotation ?? new();

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

                    var transformComponent = entry.Transform;

                    var renderPosition = transformComponent.LocalPosition;
                    var spriteRotation = Angle.Zero;
                    if (ent.Comp.UseSpriteTransform)
                    {
                        renderPosition += spriteComponent.Offset;
                        spriteRotation += spriteComponent.Rotation;
                    }
                    else
                        spriteRotation += transformComponent.LocalRotation;

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

                        var textureRotation = spriteRotation;
                        var drawRotation = spriteRotation;

                        var origin = renderPosition;
                        if (ent.Comp.UseSpriteTransform)
                        {
                            textureRotation += layer.Rotation;
                            origin += textureRotation.RotateVec(layer.Offset); // This might need to be rotated by the texture rotation but idk

                            var noRot = spriteComponent.NoRotation;
                            var snapCardinals = spriteComponent.SnapCardinals;
                            if (spriteComponent.GranularLayersRendering)
                            {
                                noRot = layer.RenderingStrategy == LayerRenderingStrategy.NoRotation || layer.RenderingStrategy == LayerRenderingStrategy.UseSpriteStrategy && noRot;
                                snapCardinals = layer.RenderingStrategy == LayerRenderingStrategy.SnapToCardinals || layer.RenderingStrategy == LayerRenderingStrategy.UseSpriteStrategy && snapCardinals;
                            }

                            if (noRot) // If its no-rot
                            {
                                textureRotation = -localEyeRotation;
                                drawRotation = textureRotation;
                            }
                            else // With rotation
                            {
                                var cardinal = Angle.Zero;
                                if (snapCardinals)
                                {
                                    cardinal = (spriteRotation + localEyeRotation)
                                        .Reduced()
                                        .FlipPositive() // angle on-screen. Used to decide the direction of 4/8 directional RSIs
                                        .RoundToCardinalAngle();

                                    drawRotation = spriteRotation - cardinal;
                                }
                                else if (layer.ActualState == null ||
                                    SpriteComponent.Layer.GetDirection(layer.ActualState.RsiDirections, textureRotation) == RsiDirection.South) // if 1dir
                                    drawRotation = transformComponent.LocalRotation;
                                else
                                    drawRotation = spriteRotation;

                                textureRotation = spriteRotation - cardinal;
                            }
                        }

                        ent.Comp.LayerParams.TryGetValue(layerId, out var layerParams);

                        var texture = GetLayerTexture(spriteComponent, layer, textureRotation);
                        var spriteBox = Box2.CenteredAround(origin + ent.Comp.Offset /* this is the centroid of the box */, texture.Size / (float)EyeManager.PixelsPerMeter);
                        var boxRotation = ent.Comp.OnlyRotateTexture ? Angle.Zero : drawRotation;

                        // The pivot-point should be at the origin of the box, not origin of the world (0,0)
                        var lightBox = new Box2Rotated(spriteBox.Enlarged(layerParams?.GlowRadius ?? ent.Comp.GlowRadius), boxRotation, origin);
                        var bloomBox = new Box2Rotated(spriteBox, boxRotation, origin);

                        // The colour drawn is the colour emitted, so scaling it dims this layer's glow alone
                        var color = spriteColor * layer.Color;
                        if (layerParams is { Brightness: var brightness } && brightness != 1f)
                            color = new Color(color.R * brightness, color.G * brightness, color.B * brightness, color.A);

                        _draws.Add(new EmissiveDraw(gridWorldMatrix, texture, lightBox, bloomBox, color, shader));
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

                    worldHandle.SetTransform(draw.GridWorldMatrix * lightMatrix);
                    worldHandle.DrawTextureRect(draw.Texture, draw.LightBox, modulate: draw.Color);
                }

                worldHandle.UseShader(null);
            }, null);

        if (!OverlayManager.TryGetOverlay<KsEmissiveBloomOverlay>(out var bloomOverlay) || !bloomOverlay.Active)
            return;

        // Same size and frame as the light target, so the light target's matrix maps into it too. Made only once bloom is on.
        if (resources.Target?.Size != lightTarget.Size)
        {
            resources.Target?.Dispose();
            resources.BlurBuffer?.Dispose();
            var format = new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb);
            resources.Target = _clyde.CreateRenderTarget(lightTarget.Size, format, name: "emissive-target");
            resources.BlurBuffer = _clyde.CreateRenderTarget(lightTarget.Size, format, name: "emissive-blur-target");
        }

        var target = resources.Target;

        resources.Drawn = true;
        worldHandle.RenderInRenderTarget(target,
            () =>
            {
                // Plain, without the Emissive shader: the sprite's own pixels, which the bloom overlay blurs.
                foreach (var draw in _draws)
                {
                    worldHandle.SetTransform(draw.GridWorldMatrix * lightMatrix);
                    worldHandle.DrawTextureRect(draw.Texture, draw.BloomBox, modulate: draw.Color);
                }
            }, Color.Transparent);
    }

    private readonly record struct EmissiveDraw(
        Matrix3x2 GridWorldMatrix,
        Texture Texture,
        Box2Rotated LightBox,
        Box2Rotated BloomBox,
        Color Color,
        ShaderInstance Shader);

    /// <summary>
    ///     This frame's emissive glow for a viewport, for <see cref="KsEmissiveBloomOverlay"/>. False if nothing glowed.
    /// </summary>
    public bool TryGetGlow(IClydeViewport viewport,
        [NotNullWhen(true)] out IRenderTexture? target,
        [NotNullWhen(true)] out IRenderTexture? blurBuffer,
        out Box2Rotated bounds)
    {
        var resources = _resources.GetForViewport(viewport, static _ => new CachedResources());
        target = resources.Target;
        blurBuffer = resources.BlurBuffer;
        bounds = resources.Bounds;
        return resources.Drawn && target != null && blurBuffer != null;
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
        ///     The world bounds <see cref="Target"/> covers.
        /// </summary>
        public Box2Rotated Bounds;

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
        base.DisposeBehavior();
    }

    private Texture GetLayerTexture(SpriteComponent spriteComponent, SpriteComponent.Layer layer, Angle rotation)
    {
        var state = layer.ActualState;
        var dir = state == null ? RsiDirection.South : SpriteComponent.Layer.GetDirection(state.RsiDirections, rotation);

        Direction? overrideDirection = spriteComponent.EnableDirectionOverride ? spriteComponent.DirectionOverride : null;
        if (overrideDirection != null && state != null)
            dir = overrideDirection.Value.Convert(state.RsiDirections);

        dir = dir.OffsetRsiDir(layer.DirOffset);

        return state?.GetFrame(dir, layer.AnimationFrame) ?? layer.Texture ?? _spriteSystem.GetFallbackTexture();
    }
}
