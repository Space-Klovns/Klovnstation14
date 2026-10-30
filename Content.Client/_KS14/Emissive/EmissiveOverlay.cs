using System.Diagnostics.CodeAnalysis;
using System.Numerics;
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
        var target = lightOverlay.GetCachedForViewport(args.Viewport).EnlargedLightTarget;

        var viewport = args.Viewport;
        _grids.Clear();
        _mapSystem.FindGridsIntersecting(mapId, bounds, ref _grids, approx: true);

        if (_grids.Count == 0)
            return;

        var lightScale = viewport.LightRenderTarget.Size / (Vector2)viewport.Size;
        var scale = viewport.RenderScale / (Vector2.One / lightScale);

        var eyeRotation = args.Viewport.Eye?.Rotation ?? new();

        args.WorldHandle.RenderInRenderTarget(target,
        () =>
        {
            var invMatrix = target.GetWorldToLocalMatrix(viewport.Eye!, scale);

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

                var gridMatrix = Matrix3x2.Multiply(_transformSystem.GetWorldMatrix(grid.Owner), invMatrix);
                worldHandle.SetTransform(gridMatrix);

                var localEyeRotation = eyeRotation - gridInvMatrix.Rotation();

                ShaderInstance? currentShader = null;
                foreach (var entry in _entities)
                {
                    var ent = new Entity<EmissiveLayersComponent>(entry.Uid, entry.Component);
                    var spriteComponent = _spriteQuery.GetComponent(ent);
                    if (!spriteComponent.Visible)
                        continue;

                    var shader = GetShader(ent.Comp.Intensity);
                    if (shader != currentShader)
                    {
                        worldHandle.UseShader(shader);
                        currentShader = shader;
                    }

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
                        var box = Box2.CenteredAround(origin + ent.Comp.Offset /* this is the centroid of the box */, texture.Size / (float)EyeManager.PixelsPerMeter).Enlarged(layerParams?.GlowRadius ?? ent.Comp.GlowRadius);

                        var textureBox = new Box2Rotated(
                            box,
                            ent.Comp.OnlyRotateTexture ? Angle.Zero : drawRotation,
                            origin // /* this is the pivot-point of the box */ The pivot-point should be at the origin of the box, not origin of the world (0,0)
                        );

                        // The colour drawn is the colour emitted, so scaling it dims this layer's glow alone
                        var color = spriteColor * layer.Color;
                        if (layerParams is { Brightness: var brightness } && brightness != 1f)
                            color = new Color(color.R * brightness, color.G * brightness, color.B * brightness, color.A);

                        worldHandle.DrawTextureRect(
                            texture,
                            textureBox,
                            modulate: color
                        );
                    }
                }

                worldHandle.UseShader(null);
            }
        }, null);
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
