using System.Numerics;
using Content.Shared._KS14.Particles;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Map;

namespace Content.Shared._KS14.Weapons.GunSmoke;

/// <summary>
///     TGMC's firing smoke (<c>gun_system.dm</c>, <c>/particles/firing_smoke</c>): a puff out of the muzzle, along the shot.
/// </summary>
public sealed partial class KsGunFiringSmokeSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private KsSharedParticlesSystem _particlesSystem = default!;

    [SubscribeLocalEvent]
    private void OnGunShot(Entity<KsGunFiringSmokeComponent> entity, ref GunShotEvent args)
    {
        var fromMapCoordinates = _transformSystem.ToMapCoordinates(args.FromCoordinates);
        var toMapCoordinates = _transformSystem.ToMapCoordinates(args.ToCoordinates);
        var worldDirection = toMapCoordinates.MapId == fromMapCoordinates.MapId
            ? toMapCoordinates.Position - fromMapCoordinates.Position
            : Vector2.Zero;

        // On the ground where the shot left, so the puff stays behind a moving shooter as TGMC's does (get_turf).
        var coordinates = _transformSystem.WithEntityId(args.FromCoordinates, GetGround(args.FromCoordinates));
        // The gun is the source: one puff per gun per tick, however often the shot is re-predicted.
        if (_particlesSystem.SpawnPredictedEffect(entity.Comp.Prototype, coordinates, entity.Owner) is not { } emitterUid ||
            worldDirection.LengthSquared() <= float.Epsilon)
            return;

        var parentUid = Transform(emitterUid).ParentUid;
        var parentRotation = parentUid.IsValid() ? _transformSystem.GetWorldRotation(parentUid) : Angle.Zero;
        _particlesSystem.SetRotation(emitterUid, Angle.FromWorldVec(Vector2.Normalize(worldDirection)) - parentRotation);
    }

    /// <summary>
    ///     The grid or map under <paramref name="coordinates"/>, not whoever holds the gun.
    /// </summary>
    private EntityUid GetGround(EntityCoordinates coordinates)
    {
        return _transformSystem.GetGrid(coordinates) ?? _transformSystem.GetMap(coordinates) ?? coordinates.EntityId;
    }
}
