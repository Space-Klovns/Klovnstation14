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
using Robust.Shared.Prototypes;
using Robust.Shared.Reflection;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Emissive;

public sealed class EmissiveOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> Shader = "Emissive";

    public override OverlaySpace Space => OverlaySpace.BeforeLighting;

    [Dependency] private readonly EntityManager _entityManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IReflectionManager _reflectionManager = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IOverlayManager _overlay = default!;

    private readonly SharedTransformSystem _transformSystem = default!;
    private readonly EntityLookupSystem _lookupSystem = default!;
    private readonly SpriteSystem _spriteSystem = default!;

    private readonly EntityQuery<SpriteComponent> _spriteQuery = default!;

    private readonly List<EntityUid> _entitiesToRemoveFromShaders = [];
    private readonly HashSet<Entity<EmissiveLayersComponent>> _entities = [];
    private readonly HashSet<EntityUid> _allShaderEntities = [];
    /// <summary>
    ///     Because a new shader has to be instantiated if you want to use a custom parameter yaaaaaaaaay
    /// </summary>
    private readonly Dictionary<EntityUid, ShaderInstance> _shaders = [];
    private List<Entity<MapGridComponent>> _grids = [];

    public const int ContentZIndex = BeforeLightTargetOverlay.ContentZIndex + 2;

    public EmissiveOverlay()
    {
        IoCManager.InjectDependencies(this);

        _transformSystem = _entityManager.System<SharedTransformSystem>();
        _lookupSystem = _entityManager.System<EntityLookupSystem>();
        _spriteSystem = _entityManager.System<SpriteSystem>();

        _spriteQuery = _entityManager.GetEntityQuery<SpriteComponent>();

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
        _mapManager.FindGridsIntersecting(mapId, bounds, ref _grids, approx: true);

        if (_grids.Count == 0)
            return;

        var lightScale = viewport.LightRenderTarget.Size / (Vector2)viewport.Size;
        var scale = viewport.RenderScale / (Vector2.One / lightScale);

        var eyeRotation = args.Viewport.Eye?.Rotation ?? new();

        var transformQuery = _entityManager.TransformQuery;
        var curTime = _gameTiming.CurTime;

        args.WorldHandle.RenderInRenderTarget(target,
        () =>
        {
            var invMatrix = target.GetWorldToLocalMatrix(viewport.Eye!, scale);

            foreach (var grid in _grids)
            {
                var gridInvMatrix = _transformSystem.GetInvWorldMatrix(grid);
                var localBounds = gridInvMatrix.TransformBox(bounds);

                _entities.Clear();
                _lookupSystem.GetLocalEntitiesIntersecting(grid.Owner, localBounds, _entities);

                if (_entities.Count == 0)
                    continue;

                var gridMatrix = Matrix3x2.Multiply(_transformSystem.GetWorldMatrix(grid.Owner), invMatrix);
                worldHandle.SetTransform(gridMatrix);

                var localEyeRotation = eyeRotation - gridInvMatrix.Rotation();

                foreach (var ent in _entities)
                {
                    var spriteComponent = _spriteQuery.GetComponent(ent);
                    if (!spriteComponent.Visible)
                        continue;

                    // GetValueRefOrAddDefault isnt sandboxed so GEG
                    if (!_shaders.TryGetValue(ent.Owner, out var shader))
                    {
                        shader = _prototypeManager.Index(Shader).InstanceUnique();
                        _shaders[ent.Owner] = shader;
                    }
                    _allShaderEntities.Add(ent);
                    shader.SetParameter("bloom_intensity", ent.Comp.Intensity);
                    worldHandle.UseShader(shader);

                    var transformComponent = transformQuery.GetComponent(ent);

                    var renderPosition = transformComponent.Coordinates.Position;
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
                                textureRotation = localEyeRotation;
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

                        var texture = GetLayerTexture(spriteComponent, layer, textureRotation);
                        var box = Box2.CenteredAround(origin + ent.Comp.Offset /* this is the centroid of the box */, texture.Size / (float)EyeManager.PixelsPerMeter).Enlarged(ent.Comp.GlowRadius);

                        var textureBox = new Box2Rotated(
                            box,
                            ent.Comp.OnlyRotateTexture ? Angle.Zero : drawRotation,
                            origin // /* this is the pivot-point of the box */ The pivot-point should be at the origin of the box, not origin of the world (0,0)
                        );

                        worldHandle.DrawTextureRect(
                            texture,
                            textureBox,
                            modulate: spriteColor * layer.Color
                        );
                    }

                    worldHandle.UseShader(null);
                }
            }
        }, null);

        // Now, start purging old shaders that are no longer in use

        foreach (var (oldUid, oldShader) in _shaders)
        {
            if (_allShaderEntities.Contains(oldUid!))
                continue;

            oldShader.Dispose();
            _entitiesToRemoveFromShaders.Add(oldUid);
        }

        foreach (var uid in _entitiesToRemoveFromShaders)
            _shaders.Remove(uid);

        _entitiesToRemoveFromShaders.Clear();
        _allShaderEntities.Clear();
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
