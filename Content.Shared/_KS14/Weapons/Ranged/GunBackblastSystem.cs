using System.Linq;
using System.Numerics;
using Content.Shared.Damage.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._KS14.Weapons.Ranged;

public sealed partial class GunBackblastSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private SharedStunSystem _stunSystem = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;

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

        foreach (var otherUid in uidsInRange)
        {
            if (otherUid == args.User ||
                otherUid == entity.Owner ||
                HasComp<ProjectileComponent>(otherUid) ||
                args.Ammo.Any(x => x.Uid == otherUid) ||
                !IsInCone(fromWorldPosition, otherUid, sectorDirection, halfField, out var toDirectionUnitVector))
                continue;

            if (TryComp<PhysicsComponent>(otherUid, out var physicsComponent))
                _physicsSystem.ApplyLinearImpulse(otherUid, toDirectionUnitVector * entity.Comp.PushForce, body: physicsComponent);

            _damageableSystem.TryChangeDamage(otherUid, entity.Comp.Damage, origin: args.User);
            _stunSystem.TryKnockdown(otherUid, entity.Comp.KnockdownTime, refresh: false);
        }
    }

    private bool IsInCone(Vector2 fromWorldPosition, EntityUid otherUid, Vector2 sectorUnitDirection, Angle halfField, out Vector2 toDirectionUnitVector)
    {
        var toWorldPosition = _transformSystem.GetWorldPosition(otherUid);

        toDirectionUnitVector = toWorldPosition - fromWorldPosition;
        Vector2Helpers.Normalize(ref toDirectionUnitVector);

        return Vector2.Dot(sectorUnitDirection, toDirectionUnitVector) >=
               MathF.Cos((float)halfField.Theta);
    }
}
