using System.Numerics;
using Content.Client.Light;
using Content.Shared._KS14.DodgingEffect;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Client._KS14.DodgingEffect;

public sealed class DodgingEffectOverlay : Overlay
{
    public override OverlaySpace Space => OverlaySpace.WorldSpaceEntities;

    [Dependency] private readonly EntityManager _entityManager = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IOverlayManager _overlay = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] private readonly EntityLookupSystem _lookupSystem = default!;
    [Dependency] private readonly SpriteSystem _spriteSystem = default!;

    [Dependency] private readonly EntityQuery<SpriteComponent> _spriteQuery = default!;

    private static readonly Color EffectColor = new(1f, 1f, 1f, a: 1f);
    private static readonly Color EffectColorTransparent = new(0.5f, 0.5f, 0.5f, a: 0f);

    private readonly HashSet<Entity<DodgingEffectComponent>> _entities = [];
    private List<Entity<MapGridComponent>> _grids = [];


    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (args.Viewport.Eye == null)
            return false;

        return true;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var lightOverlay = _overlay.GetOverlay<BeforeLightTargetOverlay>();
        var bounds = lightOverlay.EnlargedBounds;

        _grids.Clear();
        // doesnt work offgrids o algo so plz fix somephono
        _mapManager.FindGridsIntersecting(args.MapId, bounds, ref _grids, approx: true);
        if (_grids.Count == 0)
            return;

        var eyeRotation = args.Viewport.Eye?.Rotation ?? new();
        var worldHandle = args.WorldHandle;

        var curTime = _gameTiming.CurTime;
        var transformQuery = _entityManager.TransformQuery;

        foreach (var grid in _grids)
        {
            var gridInvMatrix = _transformSystem.GetInvWorldMatrix(grid);
            var localBounds = gridInvMatrix.TransformBox(bounds);

            _entities.Clear();
            _lookupSystem.GetLocalEntitiesIntersecting(grid.Owner, localBounds, _entities);

            if (_entities.Count == 0)
                continue;

            var invGridMatrix = _transformSystem.GetWorldMatrix(grid.Owner);

            foreach (var entity in _entities)
            {
                var spriteComponent = _spriteQuery.GetComponent(entity);
                if (!spriteComponent.Visible)
                    continue;

                var transformComponent = transformQuery.GetComponent(entity.Owner);
                var worldRotation = Vector2.TransformNormal(transformComponent.LocalRotation.ToVec(), invGridMatrix).ToAngle();

                var spriteEntity = new Entity<SpriteComponent>(entity.Owner, spriteComponent);

                var oldColor = spriteEntity.Comp.Color;
                var timeFrac = (float)((curTime.TotalSeconds - entity.Comp.StartTime.TotalSeconds) / (entity.Comp.TimeUntilFinished.TotalSeconds - entity.Comp.StartTime.TotalSeconds));
                if (timeFrac < 0f)
                    timeFrac = 0f;
                else if (timeFrac > 1f)
                    timeFrac = 1f;

                _spriteSystem.SetColor(spriteEntity!, Color.InterpolateBetween(EffectColor, EffectColorTransparent, timeFrac));
                foreach (var otherCoordinates in entity.Comp.Data)
                {
                    var localCoordinates = _transformSystem.WithEntityId(otherCoordinates, transformComponent.ParentUid);
                    _spriteSystem.RenderSprite(spriteEntity, args.WorldHandle, eyeRotation, worldRotation, Vector2.Transform(localCoordinates.Position, invGridMatrix));
                }

                _spriteSystem.SetColor(spriteEntity!, oldColor);
            }
        }

        worldHandle.SetTransform(Matrix3x2.Identity);
    }
}
