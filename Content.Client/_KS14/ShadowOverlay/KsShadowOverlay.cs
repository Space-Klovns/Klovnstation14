using System.Linq;
using System.Numerics;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client._KS14.ShadowOverlay;

public sealed class KsShadowOverlay : Overlay
{
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly EntityManager _entityManager = default!;
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] private readonly SpriteSystem _spriteSystem = default!;
    [Dependency] private readonly EntityLookupSystem _entityLookupSystem = default!;

    [Dependency] private readonly EntityQuery<SpriteComponent> _spriteQuery = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceEntities;
    private const int ConstZIndex = (int)Shared.DrawDepth.DrawDepth.Mobs;
    private const LookupFlags EntityLookupFlags = LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.Uncontained | LookupFlags.Approximate;

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
        _mapManager.FindGridsIntersecting(args.MapId, bounds, ref _grids, approx: true);
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
                var quad = new Box2Rotated(box: Box2.CenteredAround(position + entity.Comp.Offset, texture.Size / (float)EyeManager.PixelsPerMeter * spriteComponent.Scale), -localEyeRotation, position);

                worldHandle.DrawTextureRectRegion(
                    texture,
                    quad,
                    modulate: entity.Comp.Modulate
                );
            }
        }

        worldHandle.SetTransform(Matrix3x2.Identity);
    }
}
