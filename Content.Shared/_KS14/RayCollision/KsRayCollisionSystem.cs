using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._KS14.RayCollision;

public sealed class KsRayCollisionSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] private readonly RayCastSystem _rayCastSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesBefore.Add(typeof(SharedPhysicsSystem));
    }

    public void StartChecking(Entity<TransformComponent?> entity)
    {
        var component = EnsureComp<KsRayCollisionComponent>(entity);
        component.LastMapCoordinates = _transformSystem.GetMapCoordinates(entity.Comp ?? Transform(entity));
    }

    public void StopChecking(EntityUid uid)
    {
        RemComp<KsRayCollisionComponent>(uid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var queryFilter = new QueryFilter { Flags = QueryFlags.Dynamic | QueryFlags.Static };
        var eqe = EntityQueryEnumerator<KsRayCollisionComponent, FixturesComponent, TransformComponent>();

        while (eqe.MoveNext(out var uid, out var rayCollisionComponent, out var fixturesComponent, out var transformComponent))
        {
            var lastMapCoordinates = rayCollisionComponent.LastMapCoordinates;
            var newMapCoordinates = _transformSystem.GetMapCoordinates(transformComponent);

            // fallback if cross-map
            if (lastMapCoordinates.MapId != transformComponent.MapID)
            {
                rayCollisionComponent.LastMapCoordinates = newMapCoordinates;
                continue;
            }

            foreach (var (_, fixture) in fixturesComponent.Fixtures)
            {
                if (!fixture.Hard)
                    continue;

                queryFilter.LayerBits = fixture.CollisionLayer;
                queryFilter.MaskBits = fixture.CollisionMask;
                queryFilter.IsIgnored = (otherUid) => otherUid == uid; // Dont hit ourselves

                var translation = newMapCoordinates.Position - lastMapCoordinates.Position;
                var rayResult = _rayCastSystem.CastRayClosest(
                    transformComponent.MapID,
                    lastMapCoordinates.Position,
                    translation,
                    queryFilter
                );

                if (!rayResult.Hit)
                    continue;

                var rayHit = rayResult.Results[0];
                var hitUid = rayHit.Entity;
                var hitTransformComponent = Transform(hitUid);

                var normal = translation;
                Vector2Helpers.Normalize(ref normal);
                var fixRad = fixture.Shape.Radius;
                var point = rayHit.Point - fixRad * -normal;

                var entityCoordinates = new EntityCoordinates(hitTransformComponent.MapUid!.Value, point);
                _transformSystem.SetCoordinates((uid, transformComponent, MetaData(uid)), entityCoordinates);

                DoCollision((uid, transformComponent), rayHit.Entity, new(hitTransformComponent.ParentUid, point));
                RemComp(uid, rayCollisionComponent);
                break;
            }

            rayCollisionComponent.LastMapCoordinates = newMapCoordinates;
        }
    }

    private void DoCollision(Entity<TransformComponent> ourEntity, Entity<TransformComponent?> otherEntity, EntityCoordinates point)
    {
        if (!EntityManager.TransformQuery.Resolve(otherEntity, ref otherEntity.Comp))
            return;

        var ev = new KsRayCollisionEvent(ourEntity, otherEntity!, point);
        RaiseLocalEvent(ourEntity, ref ev);
        RaiseLocalEvent(otherEntity, ref ev);
    }
}
