using System.Linq;
using System.Numerics;
using Content.Shared.Damage.Systems;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Projectiles;
using Content.Shared.Stunnable;
using Content.Shared.SubFloor;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._KS14.Weapons.Ranged;

public sealed partial class GunBackblastSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private SharedStunSystem _stunSystem = default!;
    [Dependency] private SharedInteractionSystem _interactionSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private TileSystem _tileSystem = default!;

    [Dependency] private EntityQuery<SubFloorHideComponent> _subFloorHideQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GunBackblastComponent, GunShotEvent>(OnGunShot);
    }

    private void OnGunShot(Entity<GunBackblastComponent> entity, ref GunShotEvent args)
    {
        var gunTransform = Transform(entity);

        var fromWorldPosition = _transformSystem.ToWorldPosition(args.FromCoordinates);
        var uidsInRange = _entityLookupSystem.GetEntitiesInRange(gunTransform.MapID, fromWorldPosition, entity.Comp.Radius, LookupFlags.Uncontained | LookupFlags.Dynamic);
        if (uidsInRange.Count == 0)
            return;

        var sectorDirection = ((_transformSystem.ToWorldPosition(args.ToCoordinates) - fromWorldPosition).ToWorldAngle() + entity.Comp.DirectionOffset).ToWorldVec();
        var halfField = entity.Comp.EffectField / 2d;

        var gridUid = _transformSystem.GetGrid(args.FromCoordinates);
        TryComp<MapGridComponent>(gridUid, out var mapGridComponent);

        foreach (var otherUid in uidsInRange)
        {
            if (otherUid == args.User ||
                otherUid == entity.Owner ||
                HasComp<ProjectileComponent>(otherUid) ||
                args.Ammo.Any(x => x.Uid == otherUid) ||
                !IsInCone(fromWorldPosition, otherUid, sectorDirection, halfField, out var toWorldPosition, out var toDirectionUnitVector))
                continue;

            if (!_interactionSystem.InRangeUnobstructed(new MapCoordinates(fromWorldPosition, gunTransform.MapID), new MapCoordinates(toWorldPosition, gunTransform.MapID), range: -1))
                continue;

            if (mapGridComponent is { } &&
                _subFloorHideQuery.TryGetComponent(otherUid, out var subFloorHideComponent) &&
                _mapSystem.TryGetTileRef(gridUid!.Value, mapGridComponent!, toWorldPosition, out var tileRef))
            {
                _tileSystem.PryTile(tileRef);
            }

            if (TryComp<PhysicsComponent>(otherUid, out var physicsComponent))
                _physicsSystem.ApplyLinearImpulse(otherUid, toDirectionUnitVector * entity.Comp.PushForce, body: physicsComponent);

            _damageableSystem.TryChangeDamage(otherUid, entity.Comp.Damage, origin: args.User);
            _stunSystem.TryKnockdown(otherUid, entity.Comp.KnockdownTime, refresh: false);
        }
    }

    private bool IsInCone(Vector2 fromWorldPosition, EntityUid otherUid, Vector2 sectorUnitDirection, Angle halfField, out Vector2 toWorldPosition, out Vector2 toDirectionUnitVector)
    {
        toWorldPosition = _transformSystem.GetWorldPosition(otherUid);
        toDirectionUnitVector = toWorldPosition - fromWorldPosition;
        Vector2Helpers.Normalize(ref toDirectionUnitVector);

        return Vector2.Dot(sectorUnitDirection, toDirectionUnitVector) >=
               MathF.Cos((float)halfField.Theta);
    }
}
