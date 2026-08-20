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

namespace Content.Client._KS14.OverlayStains;

public sealed partial class StainOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> BlackShaderId = "KsBlack";
    private static readonly ProtoId<ShaderPrototype> StencilMaskShaderId = "StencilMask";
    private static readonly ProtoId<ShaderPrototype> StencilEqualDrawShaderId = "StencilEqualDraw";

    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IMapManager _mapManager = default!;

    [Dependency] private TransformSystem _transformSystem = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;

    /// <summary>
    ///     Based on <see cref="Shared._KS14.CCVar.KsCCVars.ComplexStainDrawing"/>
    /// </summary>
    public bool ComplexDrawing = false;

    [Dependency] private EntityQuery<TransformComponent> _transformQuery = default!;
    [Dependency] private EntityQuery<SpriteComponent> _spriteQuery = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    private List<Entity<MapGridComponent>> _grids = new();
    private HashSet<Entity<StainableComponent>> _intersectingEntities = new();

    private readonly OverlayResourceCache<CachedResources> _resources = new();

    private ShaderInstance _blackShader = default!;
    private ShaderInstance _stencilMaskShader = default!;
    private ShaderInstance _stencilEqualDrawShader = default!;

    // see: DoAfterOverlay.cs
    private const float Scale = 1f;
    private const float DblPixelsPerMeter = 2f * EyeManager.PixelsPerMeter;

    private static readonly Matrix3x2 ScaleMatrix = Matrix3Helpers.CreateScale(new Vector2(Scale, Scale));

    public StainOverlay()
    {
        ZIndex = (int)Shared.DrawDepth.DrawDepth.WallTops;
    }

    public void Initialise()
    {
        _blackShader = _prototypeManager.Index(BlackShaderId).Instance();
        _stencilMaskShader = _prototypeManager.Index(StencilMaskShaderId).Instance();
        _stencilEqualDrawShader = _prototypeManager.Index(StencilEqualDrawShaderId).Instance();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (!base.BeforeDraw(args))
            return false;

        return _entityManager.EntityQuery<StainedComponent>(includePaused: false).Any();
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

        var worldBoundBox = worldBounds.CalcBoundingBox();

        // Need to do stencilling after blur as it will nuke it.
        // Draw stencil for the grid so we don't draw in space.
        worldHandle.UseShader(_blackShader);
        args.WorldHandle.RenderInRenderTarget(res.StainTarget,
            () =>
            {
                _grids.Clear();
                _mapManager.FindGridsIntersecting(mapId, worldBounds, ref _grids);

                foreach (var grid in _grids)
                {
                    var gridInvMatrix = _transformSystem.GetInvWorldMatrix(grid, _transformQuery);
                    var localBounds = gridInvMatrix.TransformBox(worldBoundBox);

                    _intersectingEntities.Clear();
                    _entityLookupSystem.GetLocalEntitiesIntersecting(grid.Owner, localBounds, _intersectingEntities, flags: LookupFlags.Static | LookupFlags.Uncontained);
                    if (_intersectingEntities.Count == 0)
                        continue;

                    var worldMatrix = _transformSystem.GetWorldMatrix(grid, _transformQuery);
                    var localMatrix = Matrix3x2.Multiply(worldMatrix, invMatrix);

                    if (ComplexDrawing)
                        worldHandle.SetTransform(Matrix3x2.Identity);
                    else
                        worldHandle.SetTransform(localMatrix);

                    // TODO: Draw actual sprite texture to stencil?
                    foreach (var uid in _intersectingEntities)
                    {
                        if (!_transformQuery.TryGetComponent(uid, out var transformComponent) ||
                            !_spriteQuery.TryGetComponent(uid, out var spriteComponent))
                            continue;

                        // TODO LCDC: make this work
                        if (ComplexDrawing)
                        {
                            //_spriteSystem.RenderSprite((uid, spriteComponent), worldHandle, eyeRotation, worldMatrix.Rotation() * transformComponent.LocalRotation, Vector2.Transform(transformComponent.LocalPosition, worldMatrix));
                            _spriteSystem.RenderSprite((uid, spriteComponent), worldHandle, eyeRotation, _transformSystem.GetWorldRotation(transformComponent), _transformSystem.GetWorldPosition(transformComponent));
                        }
                        else
                        {
                            var bounds = _spriteSystem.CalculateBounds((uid, spriteComponent), transformComponent.Coordinates.Position, transformComponent.LocalRotation, eyeRotation);
                            worldHandle.DrawRect(bounds, Color.Black);
                        }
                    }
                }

            }, Color.Transparent);

        worldHandle.SetTransform(Matrix3x2.Identity);

        // draw the stencil texture we made to the depth buffer
        worldHandle.UseShader(_stencilMaskShader);
        worldHandle.DrawTextureRect(res.StainTarget.Texture, worldBounds);

        // Only draw stains where the stencil was set above, i.e. on top of walls.
        worldHandle.UseShader(_stencilEqualDrawShader);

        var stainedEnumerator = _entityManager.EntityQueryEnumerator<StainedComponent, TransformComponent>();
        while (stainedEnumerator.MoveNext(out var uid, out var stainedComponent, out var transformComponent))
        {
            if (stainedComponent.Stains.Count == 0)
                continue;

            var (worldPosition, worldRotation) = _transformSystem.GetWorldPositionRotation(transformComponent);
            var worldMatrix = Matrix3Helpers.CreateTransform(worldPosition, worldRotation - transformComponent.LocalRotation);

            var scaledWorld = Matrix3x2.Multiply(ScaleMatrix, worldMatrix);
            worldHandle.SetTransform(scaledWorld);

            foreach (var stain in stainedComponent.Stains)
            {
                var texture = _spriteSystem.GetFrame(stain.Texture, realTime);
                var halfTextureWidth = texture.Width / DblPixelsPerMeter;
                var halfTextureHeight = texture.Height / DblPixelsPerMeter;

                worldHandle.DrawTexture(
                    texture,
                    new Vector2(stain.Offset.X - halfTextureWidth, stain.Offset.Y - halfTextureHeight),
                    angle: new Angle(stain.Rotation * MathF.Tau), modulate: stain.Color
                );
            }
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
