using System.Linq;
using System.Numerics;
using Content.Client.Graphics;
using Content.Shared._KS14.OverlayStains;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._KS14.OverlayStains;

public sealed class StainOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> UnshadedShader = "unshaded";
    private static readonly ProtoId<ShaderPrototype> StencilMaskShader = "StencilMask";
    private static readonly ProtoId<ShaderPrototype> StencilDrawShader = "StencilDraw";

    [Dependency] private readonly IClyde _clyde = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;

    [Dependency] private readonly TransformSystem _transformSystem = default!;
    [Dependency] private readonly SpriteSystem _spriteSystem = default!;
    [Dependency] private readonly EntityLookupSystem _entityLookupSystem = default!;

    /// <summary>
    ///     Based on <see cref="Shared._KS14.CCVar.KsCCVars.ComplexStainDrawing"/>
    /// </summary>
    public bool ComplexDrawing = false;

    [Dependency] private readonly EntityQuery<TransformComponent> _transformQuery = default!;
    [Dependency] private readonly EntityQuery<SpriteComponent> _spriteQuery = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public SpriteSpecifier? StainSpriteSpecifier;

    private List<Entity<MapGridComponent>> _grids = new();
    private HashSet<EntityUid> _intersectingEntities = new();

    private readonly OverlayResourceCache<CachedResources> _resources = new();

    // see: DoAfterOverlay.cs
    private const float Scale = 1f;
    private const float DblPixelsPerMeter = 2f * EyeManager.PixelsPerMeter;

    private static readonly Matrix3x2 ScaleMatrix = Matrix3Helpers.CreateScale(new Vector2(Scale, Scale));

    public StainOverlay()
    {
        ZIndex = (int)Shared.DrawDepth.DrawDepth.WallTops;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (!base.BeforeDraw(args))
            return false;

        if (StainSpriteSpecifier == null)
            return false;

        var stainedQuery = _entityManager.EntityQuery<StainedComponent>();
        if (!stainedQuery.Any())
            return false;

        return false;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var viewport = args.Viewport;
        var mapId = args.MapId;
        var worldBounds = args.WorldBounds;
        var worldHandle = args.WorldHandle;
        //var color = Color.Red;
        var target = viewport.RenderTarget;
        var lightScale = target.Size / (Vector2)viewport.Size;
        var scale = viewport.RenderScale / (Vector2.One / lightScale);
        var invMatrix = viewport.GetWorldToLocalMatrix();
        var realTime = _gameTiming.RealTime;
        var eyeRotation = viewport.Eye?.Rotation ?? Angle.Zero;

        var res = _resources.GetForViewport(viewport, static _ => new CachedResources());

        if (res.StainTarget?.Texture.Size != target.Size)
        {
            res.StainTarget?.Dispose();
            res.StainTarget = _clyde.CreateRenderTarget(target.Size, new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb), name: "stain-stencil-target");
        }

        // Need to do stencilling after blur as it will nuke it.
        // Draw stencil for the grid so we don't draw in space.
        args.WorldHandle.RenderInRenderTarget(res.StainTarget,
            () =>
            {
                _grids.Clear();
                _mapManager.FindGridsIntersecting(mapId, worldBounds, ref _grids);
                var worldBoundBox = worldBounds.CalcBoundingBox();

                worldHandle.UseShader(_prototypeManager.Index(UnshadedShader).Instance());
                foreach (var grid in _grids)
                {
                    _intersectingEntities.Clear();
                    _entityLookupSystem.GetEntitiesIntersecting(mapId, worldBoundBox, _intersectingEntities, LookupFlags.Static);
                    if (_intersectingEntities.Count == 0)
                        continue;

                    var localMatrix = Matrix3x2.Multiply(_transformSystem.GetWorldMatrix(grid, _transformQuery), invMatrix);
                    worldHandle.SetTransform(localMatrix);

                    // TODO: Draw actual sprite texture to stencil?
                    foreach (var uid in _intersectingEntities)
                    {
                        if (!_transformQuery.TryGetComponent(uid, out var transformComponent) ||
                            !_spriteQuery.TryGetComponent(uid, out var spriteComponent))
                            continue;

                        if (ComplexDrawing)
                        {
                            foreach (var layer in spriteComponent.AllLayers)
                            {
                                if (layer.Texture is not { } layerTexture)
                                    continue;

                                worldHandle.DrawTexture(layerTexture, transformComponent.Coordinates.Position, transformComponent.LocalRotation, modulate: Color.Black);
                            }
                        }
                        else
                        {
                            var bounds = _spriteSystem.CalculateBounds((uid, spriteComponent), transformComponent.Coordinates.Position, transformComponent.LocalRotation, eyeRotation);
                            worldHandle.DrawRect(bounds, Color.Black);
                        }
                    }
                }

            }, Color.White);

        worldHandle.SetTransform(Matrix3x2.Identity);

        // draw the stencil texture we made to the depth buffer
        worldHandle.UseShader(_prototypeManager.Index(StencilMaskShader).Instance());
        worldHandle.DrawTextureRect(res.StainTarget.Texture, worldBounds);

        var texture = _spriteSystem.GetFrame(StainSpriteSpecifier!, realTime);
        var convertedTextureWidth = texture.Width / DblPixelsPerMeter;
        var convertedTextureHeight = texture.Height / DblPixelsPerMeter;

        worldHandle.UseShader(_prototypeManager.Index(StencilDrawShader).Instance());

        var stainedEnumerator = _entityManager.EntityQueryEnumerator<StainedComponent, SpriteComponent, TransformComponent>();
        while (stainedEnumerator.MoveNext(out var uid, out var stainedComponent, out var spriteComponent, out var transformComponent))
        {
            var (worldPosition, worldRotation) = _transformSystem.GetWorldPositionRotation(transformComponent);
            var worldMatrix = Matrix3Helpers.CreateTransform(worldPosition, worldRotation - transformComponent.LocalRotation);

            var scaledWorld = Matrix3x2.Multiply(ScaleMatrix, worldMatrix);
            worldHandle.SetTransform(scaledWorld);

            foreach (var (stainData, color) in stainedComponent.Stains)
                worldHandle.DrawTexture(
                    texture,
                    new Vector2(stainData.X - convertedTextureWidth, stainData.Y - convertedTextureHeight),
                    angle: new Angle(stainData.Z * MathF.Tau), modulate: color
                );
        }

        worldHandle.SetTransform(Matrix3x2.Identity);
        worldHandle.UseShader(null);
    }

    protected override void DisposeBehavior()
    {
        _resources.Dispose();
        base.DisposeBehavior();
    }

    private sealed class CachedResources : IDisposable
    {
        public IRenderTexture? StainTarget;

        public void Dispose()
        {
            StainTarget?.Dispose();
        }
    }
}
