using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Dynamics;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._KS14.RayCollision;

public sealed partial class KsRayCollisionSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private RayCastSystem _rayCastSystem = default!;
    [Dependency] private FixtureSystem _fixtureSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesBefore.Add(typeof(SharedPhysicsSystem));

        SubscribeLocalEvent<KsRayCollisionComponent, PhysicsSleepEvent>(OnPhysicsSleep);
    }

    private void OnPhysicsSleep(Entity<KsRayCollisionComponent> entity, ref PhysicsSleepEvent args)
    {
        StopChecking(entity);
    }

    /// <param name="fixtures">If not null, then the component will check only these fixtures for collision instead of</param>
    public void StartChecking(Entity<TransformComponent?> entity, string[]? exclusiveFixtureIds = null)
    {
        var component = EnsureComp<KsRayCollisionComponent>(entity);
        component.LastMapCoordinates = _transformSystem.GetMapCoordinates(entity.Comp ?? Transform(entity));
        component.ExclusivelyCheckedFixtures = exclusiveFixtureIds;
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

            var translation = newMapCoordinates.Position - lastMapCoordinates.Position;
            if (MathHelper.CloseTo(translation.LengthSquared(), 0f, tolerance: 0.1f))
                continue;

            var metaDataComponent = MetaData(uid);
            var exclusivity = rayCollisionComponent.ExclusivelyCheckedFixtures is { };

            queryFilter.IsIgnored = (otherUid) => otherUid == uid; // Dont hit ourselves

            if (rayCollisionComponent.ExclusivelyCheckedFixtures is { } exclusiveFixtures)
            {
                foreach (var fixtureId in exclusiveFixtures)
                {
                    if (_fixtureSystem.GetFixtureOrNull(uid, fixtureId, manager: fixturesComponent) is not { } fixture ||
                        !TryProcessFixture((uid, transformComponent, metaDataComponent), fixtureId, fixture, lastMapCoordinates, translation, ref queryFilter))
                        continue;

                    RemComp(uid, rayCollisionComponent);
                    break;
                }
            }
            else
            {
                foreach (var (fixtureId, fixture) in fixturesComponent.Fixtures)
                {
                    if (!TryProcessFixture((uid, transformComponent, metaDataComponent), fixtureId, fixture, lastMapCoordinates, translation, ref queryFilter))
                        continue;

                    RemComp(uid, rayCollisionComponent);
                    break;
                }
            }

            rayCollisionComponent.LastMapCoordinates = newMapCoordinates;
        }
    }

    /// <returns>True if there was a collision.</returns>
    private bool TryProcessFixture(Entity<TransformComponent, MetaDataComponent> ourEntity, string fixtureId, Fixture fixture, MapCoordinates lastMapCoordinates, Vector2 translation, ref QueryFilter queryFilter)
    {
        if (!fixture.Hard)
            return false;

        queryFilter.LayerBits = fixture.CollisionLayer;
        queryFilter.MaskBits = fixture.CollisionMask;

        var rayResult = _rayCastSystem.CastRay(
            ourEntity.Comp1.MapID,
            lastMapCoordinates.Position,
            translation,
            queryFilter
        );

        // No PreventCollideEvent here!

        if (!rayResult.Hit)
            return false;

        var rayHit = rayResult.Results[0];
        var hitUid = rayHit.Entity;
        var hitTransformComponent = Transform(hitUid);

        var normal = translation;
        Vector2Helpers.Normalize(ref normal);
        var fixRad = fixture.Shape.Radius;
        var point = rayHit.Point - fixRad * -normal;

        var entityCoordinates = new EntityCoordinates(hitTransformComponent.MapUid!.Value, point);
        _transformSystem.SetCoordinates(ourEntity, entityCoordinates);

        DoCollision(ourEntity, rayHit.Entity, new(hitTransformComponent.ParentUid, point), fixtureId, fixture);
        return true;
    }

    private void DoCollision(Entity<TransformComponent> ourEntity, Entity<TransformComponent?> otherEntity, EntityCoordinates point, string fixtureId, Fixture fixture)
    {
        if (!EntityManager.TransformQuery.Resolve(otherEntity, ref otherEntity.Comp))
            return;

        var ev = new KsRayCollisionEvent(ourEntity, otherEntity!, point, fixtureId, fixture);
        RaiseLocalEvent(ourEntity, ref ev);
        RaiseLocalEvent(otherEntity, ref ev);
    }
}
