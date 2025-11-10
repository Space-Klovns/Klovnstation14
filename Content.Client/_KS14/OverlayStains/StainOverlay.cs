using System.Numerics;
using Content.Client.Graphics;
using Content.Shared._KS14.StainOverlays;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client.Light;

/// <summary>
/// Applies ambient-occlusion to the viewport.
/// </summary>
public sealed class StainOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> UnshadedShader = "unshaded";
    private static readonly ProtoId<ShaderPrototype> StencilMaskShader = "StencilMask";
    private static readonly ProtoId<ShaderPrototype> StencilEqualDrawShader = "StencilEqualDraw";

    [Dependency] private readonly IClyde _clyde = default!;
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;

    private readonly OccluderSystem _occluderSystem = default!;
    private readonly TransformSystem _transformSystem = default!;
    private readonly SpriteSystem _spriteSystem = default!;

    private EntityQuery<TransformComponent> _transformQuery;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public List<(Vector2, Color)> RenderedStains = new();
    public SpriteSpecifier? StainSpriteSpecifier;

    private readonly OverlayResourceCache<CachedResources> _resources = new();

    private static readonly Vector2 Vector2Half = Vector2.One / 2;
    private static readonly Matrix3x2 ScaleMatrix = Matrix3Helpers.CreateScale(new Vector2(1, 1));

    public StainOverlay()
    {
        IoCManager.InjectDependencies(this);

        _occluderSystem = _entManager.System<OccluderSystem>();
        _transformSystem = _entManager.System<TransformSystem>();
        _spriteSystem = _entManager.System<SpriteSystem>();

        _transformQuery = _entManager.GetEntityQuery<TransformComponent>();

        ZIndex = AfterLightTargetOverlay.ContentZIndex + 1;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (StainSpriteSpecifier == null ||
            RenderedStains.Count == 0)
            return;

        var viewport = args.Viewport;
        var mapId = args.MapId;
        var worldBounds = args.WorldBounds;
        var worldHandle = args.WorldHandle;
        //var color = Color.Red;
        var target = viewport.RenderTarget;
        var lightScale = target.Size / (Vector2)viewport.Size;
        var scale = viewport.RenderScale / (Vector2.One / lightScale);
        var invMatrix = args.Viewport.GetWorldToLocalMatrix();

        var res = _resources.GetForViewport(args.Viewport, static _ => new CachedResources());

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
                worldHandle.UseShader(_prototypeManager.Index(UnshadedShader).Instance());
                var invMatrix = res.StainTarget.GetWorldToLocalMatrix(viewport.Eye!, scale);

                foreach (var entry in _occluderSystem.QueryAabb(mapId, worldBounds))
                {
                    DebugTools.Assert(entry.Component.Enabled);
                    var matrix = _transformSystem.GetWorldMatrix(entry.Transform);
                    var localMatrix = Matrix3x2.Multiply(matrix, invMatrix);

                    worldHandle.SetTransform(localMatrix);
                    worldHandle.DrawRect(Box2.UnitCentered, Color.White);
                }
            }, Color.Transparent);

        worldHandle.SetTransform(Matrix3x2.Identity);

        // draw the stencil texture we made to the depth buffer
        worldHandle.UseShader(_prototypeManager.Index(StencilMaskShader).Instance());
        worldHandle.DrawTextureRect(res.StainTarget.Texture, worldBounds);

        var texture = _spriteSystem.GetFrame(StainSpriteSpecifier, _gameTiming.RealTime);

        worldHandle.UseShader(_prototypeManager.Index(StencilEqualDrawShader).Instance());

        var rotationMatrix = Matrix3Helpers.CreateRotation(-args.Viewport.Eye?.Rotation ?? default);

        var stainedEnumerator = _entManager.EntityQueryEnumerator<StainedOverlayComponent, TransformComponent>();
        while (stainedEnumerator.MoveNext(out var uid, out var stainedComponent, out var transformComponent))
        {
            var worldPosition = _transformSystem.GetWorldPosition(transformComponent, _transformQuery);

            var worldMatrix = Matrix3Helpers.CreateTranslation(worldPosition);
            var scaledWorld = Matrix3x2.Multiply(ScaleMatrix, worldMatrix);
            var matty = Matrix3x2.Multiply(rotationMatrix, scaledWorld);
            worldHandle.SetTransform(matty);

            foreach (var (stainOffset, color) in stainedComponent.Stains)
                worldHandle.DrawTexture(texture, stainOffset + Vector2Half);
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
        // Couldn't figure out a way to avoid this so if you can then please do.
        public IRenderTexture? StainTarget;

        public void Dispose()
        {
            StainTarget?.Dispose();
        }
    }
}
