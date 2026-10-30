using System.Linq;
using System.Numerics;
using Content.Client._KS14.ZLevel;
using Content.Shared._KS14.ZLevel.Physics;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client._KS14.ShadowOverlay;

public sealed partial class KsShadowOverlay : Overlay
{
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private EntityManager _entityManager = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;

    [Dependency] private EntityQuery<SpriteComponent> _spriteQuery = default!;
    [Dependency] private EntityQuery<KsZLevelTransitSpriteComponent> _transitSpriteQuery = default!;
    [Dependency] private EntityQuery<KsZLevelTransitComponent> _transitQuery = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceEntities;
    private const int ConstZIndex = (int)Shared.DrawDepth.DrawDepth.Mobs;
    private const LookupFlags EntityLookupFlags = LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.Uncontained | LookupFlags.Approximate;

    /// <summary>
    ///     What a shadow shrinks to, and fades to, when its owner is a full z-level up mid-transit.
    /// </summary>
    private const float MinTransitShadowScale = 0.5f;
    private const float MinTransitShadowAlpha = 0.35f;

    private readonly HashSet<Entity<KsShadowComponent>> _entities = [];
    private List<Entity<MapGridComponent>> _grids = [];

    public KsShadowOverlay()
    {
        ZIndex = ConstZIndex;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (args.Viewport.Eye == null ||
            !_entityManager.EntityQuery<KsShadowComponent>().Any())
            return false;

        return true;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var bounds = args.WorldBounds;

        _grids.Clear();
        // doesnt work off grids, intentional
        _mapSystem.FindGridsIntersecting(args.MapId, bounds, ref _grids, approx: true);
        if (_grids.Count == 0)
            return;

        var worldHandle = args.WorldHandle;
        var transformQuery = _entityManager.TransformQuery;
        var eyeRotation = args.Viewport.Eye?.Rotation ?? Angle.Zero;

        foreach (var grid in _grids)
        {
            var gridInvMatrix = _transformSystem.GetInvWorldMatrix(grid);
            var localBounds = gridInvMatrix.TransformBox(bounds);

            _entities.Clear();
            _entityLookupSystem.GetLocalEntitiesIntersecting(grid.Owner, localBounds, _entities, flags: EntityLookupFlags);

            if (_entities.Count == 0)
                continue;

            var localEyeRotation = eyeRotation - gridInvMatrix.Rotation();
            var gridMatrix = _transformSystem.GetWorldMatrix(grid.Owner);
            worldHandle.SetTransform(gridMatrix);

            foreach (var entity in _entities)
            {
                if (entity.Comp.Sprite is not { } sprite ||
                    !_spriteQuery.TryGetComponent(entity, out var spriteComponent))
                    continue;

                var transformComponent = transformQuery.GetComponent(entity.Owner);
                var texture = _spriteSystem.Frame0(sprite);

                var position = transformComponent.LocalPosition;
                var shadowScale = spriteComponent.Scale;
                var shadowModulate = entity.Comp.Modulate;

                // A transiting entity's sprite is lifted and rescaled to sell the fall, so its shadow must not
                //      inherit any of that: it stays on the floor below, and only shrinks and fades with how far
                //      up its owner still is.
                if (_transitSpriteQuery.TryGetComponent(entity.Owner, out var transitSpriteComponent) &&
                    _transitQuery.TryGetComponent(entity.Owner, out var transitComponent))
                {
                    var grounded = 1f - Math.Clamp(transitComponent.Height, 0f, 1f);

                    shadowScale = transitSpriteComponent.PreLiftScale * (MinTransitShadowScale + (1f - MinTransitShadowScale) * grounded);
                    shadowModulate = shadowModulate.WithAlpha(shadowModulate.A * (MinTransitShadowAlpha + (1f - MinTransitShadowAlpha) * grounded));
                }

                var quad = new Box2Rotated(box: Box2.CenteredAround(position + entity.Comp.Offset * shadowScale, texture.Size / (float)EyeManager.PixelsPerMeter * shadowScale), -localEyeRotation, position);

                worldHandle.DrawTextureRectRegion(
                    texture,
                    quad,
                    modulate: shadowModulate
                );
            }
        }

        worldHandle.SetTransform(Matrix3x2.Identity);
    }
}
