using System.Linq;
using System.Numerics;
using Content.Shared._KS14.Atmos.ChemicalFire;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client._KS14.Atmos.ChemicalFire;

/// <summary>
///     Draws the <c>over</c> half of every chemfire, above the effects layer, so flames render on top of the
///         things standing in them while the entity's own <c>under</c> layer stays below.
///     Rendering is unshaded, matching the entity sprite, and the animation frame is read straight off the
///         <c>under</c> layer so the two halves can never drift apart.
/// </summary>
public sealed partial class ChemicalFireOverlay : Overlay
{
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private EntityManager _entityManager = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;

    [Dependency] private EntityQuery<SpriteComponent> _spriteQuery = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceEntities;

    /// <summary>Above the chemfire entities themselves, and above whatever is standing in them.</summary>
    public const int ContentZIndex = (int)Shared.DrawDepth.DrawDepth.Effects;

    private const LookupFlags EntityLookupFlags = LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.Uncontained | LookupFlags.Approximate;

    private readonly ShaderInstance _unshadedShader;

    private readonly HashSet<Entity<ChemicalFireComponent>> _entities = [];
    private List<Entity<MapGridComponent>> _grids = [];

    /// <summary>
    ///     Resolved <c>over</c> frames, so the RSI is only walked once per state rather than per fire per frame.
    /// </summary>
    private readonly Dictionary<(RSI Rsi, string State), Texture[]> _frames = [];

    public ChemicalFireOverlay(ShaderInstance unshadedShader)
    {
        _unshadedShader = unshadedShader;
        ZIndex = ContentZIndex;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return args.Viewport.Eye != null &&
            _entityManager.EntityQuery<ChemicalFireComponent>(true).Any();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var bounds = args.WorldBounds;

        _grids.Clear();
        _mapManager.FindGridsIntersecting(args.MapId, bounds, ref _grids, approx: true);
        if (_grids.Count == 0)
            return;

        var worldHandle = args.WorldHandle;
        var transformQuery = _entityManager.TransformQuery;
        var eyeRotation = args.Viewport.Eye?.Rotation ?? Angle.Zero;

        worldHandle.UseShader(_unshadedShader);

        foreach (var grid in _grids)
        {
            var gridInvMatrix = _transformSystem.GetInvWorldMatrix(grid);
            var localBounds = gridInvMatrix.TransformBox(bounds);

            _entities.Clear();
            _entityLookupSystem.GetLocalEntitiesIntersecting(grid.Owner, localBounds, _entities, flags: EntityLookupFlags);

            if (_entities.Count == 0)
                continue;

            var localEyeRotation = eyeRotation - gridInvMatrix.Rotation();
            worldHandle.SetTransform(_transformSystem.GetWorldMatrix(grid.Owner));

            foreach (var entity in _entities)
            {
                if (!_spriteQuery.TryGetComponent(entity.Owner, out var spriteComponent) ||
                    !spriteComponent.Visible ||
                    !_spriteSystem.TryGetLayer((entity.Owner, spriteComponent), ChemicalFireVisualLayers.Under, out var underLayer, false) ||
                    !underLayer.Visible ||
                    !TryGetOverFrames(entity.Comp, underLayer, out var frames))
                    continue;

                var texture = frames[Math.Clamp(underLayer.AnimationFrame, 0, frames.Length - 1)];

                var transformComponent = transformQuery.GetComponent(entity.Owner);
                var position = transformComponent.LocalPosition;

                // Chemfire sprites snap to cardinals, so the over half has to snap the exact same way or it
                //     would slide off the under half whenever the grid (or eye) is rotated.
                var spriteRotation = transformComponent.LocalRotation + spriteComponent.Rotation;
                var cardinal = (spriteRotation + localEyeRotation)
                    .Reduced()
                    .FlipPositive()
                    .RoundToCardinalAngle();
                var drawRotation = spriteRotation - cardinal;

                var origin = position + drawRotation.RotateVec(spriteComponent.Offset * spriteComponent.Scale);
                var quad = new Box2Rotated(
                    Box2.CenteredAround(origin, texture.Size / (float)EyeManager.PixelsPerMeter * spriteComponent.Scale),
                    drawRotation,
                    origin
                );

                worldHandle.DrawTextureRectRegion(texture, quad, modulate: spriteComponent.Color * entity.Comp.Color);
            }
        }

        worldHandle.UseShader(null);
        worldHandle.SetTransform(Matrix3x2.Identity);
    }

    private bool TryGetOverFrames(ChemicalFireComponent fireComponent, SpriteComponent.Layer underLayer, out Texture[] frames)
    {
        frames = [];

        if (underLayer.ActualRsi is not { } rsi ||
            string.IsNullOrEmpty(fireComponent.OverState))
            return false;

        var key = (rsi, fireComponent.OverState);

        // GetValueRefOrAddDefault isnt sandboxed, so this is two lookups.
        if (!_frames.TryGetValue(key, out var cachedFrames))
        {
            if (!rsi.TryGetState(fireComponent.OverState, out var state))
                return false;

            cachedFrames = state.GetFrames(RsiDirection.South);
            _frames[key] = cachedFrames;
        }

        if (cachedFrames.Length == 0)
            return false;

        frames = cachedFrames;
        return true;
    }
}
