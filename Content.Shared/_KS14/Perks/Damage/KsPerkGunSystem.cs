using Content.Shared._KS14.Weapons.Hitscan;
using Content.Shared.Mobs.Components;
using Content.Shared.Projectiles;
using Content.Shared.Mobs.Systems;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Containers;

namespace Content.Shared._KS14.Perks.Damage;

/// <summary>
///     What the gun abilities of the Damage perk ask about a gun and what it hit.
/// </summary>
public sealed partial class KsPerkGunSystem : EntitySystem
{
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private SharedGunSystem _gunSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private EntityQuery<MobStateComponent> _mobStateQuery = default!;

    /// <summary>
    ///     Where a gun's rounds come from: the magazine in its magazine slot, or the gun itself when it holds them
    ///         directly (a tube, a cylinder, a battery). False for a gun that takes magazines and has none in, which
    ///         may still fire the round in its chamber.
    /// </summary>
    public bool TryGetAmmoSource(EntityUid gunUid, out EntityUid sourceUid)
    {
        if (!_containerSystem.TryGetContainer(gunUid, SharedGunSystem.MagazineSlot, out var container))
        {
            sourceUid = gunUid;
            return true;
        }

        if (container is ContainerSlot { ContainedEntity: { } magazineUid })
        {
            sourceUid = magazineUid;
            return true;
        }

        sourceUid = default;
        return false;
    }

    /// <summary>
    ///     The magazine in a gun's magazine slot, if it takes them and has one in.
    /// </summary>
    public bool TryGetMagazine(EntityUid gunUid, out EntityUid magazineUid)
    {
        magazineUid = default;
        if (!_containerSystem.TryGetContainer(gunUid, SharedGunSystem.MagazineSlot, out var container) ||
            container is not ContainerSlot { ContainedEntity: { } containedUid })
            return false;

        magazineUid = containedUid;
        return true;
    }

    /// <inheritdoc cref="SharedGunSystem.GetAmmoCount"/>
    public int GetAmmoCount(EntityUid uid) => _gunSystem.GetAmmoCount(uid);

    /// <inheritdoc cref="SharedGunSystem.GetAmmoCapacity"/>
    public int GetAmmoCapacity(EntityUid uid) => _gunSystem.GetAmmoCapacity(uid);

    /// <summary>
    ///     Remembers where a projectile was fired from, so its hit raises <see cref="KsModifyPerkShotRangeDamageEvent"/>
    ///         on the shooter with the distance it flew. Stamping it twice changes nothing.
    /// </summary>
    public void StampOrigin(EntityUid projectileUid, EntityUid shooterUid)
    {
        if (HasComp<KsPerkShotOriginComponent>(projectileUid))
            return;

        var originComponent = AddComp<KsPerkShotOriginComponent>(projectileUid);
        var origin = _transformSystem.GetMapCoordinates(projectileUid);
        originComponent.Shooter = shooterUid;
        originComponent.OriginMap = origin.MapId;
        originComponent.OriginPosition = origin.Position;
    }

    /// <summary>
    ///     How far from its shooter a hitscan shot landed; null if that can't be told.
    /// </summary>
    public float? GetHitscanDistance(in KsHitscanDamageModifyEvent args)
    {
        if (args.Data.Shooter is not { } shooterUid || args.Data.HitCoordinates is not { } hitCoordinates)
            return null;

        var shooterCoordinates = _transformSystem.GetMapCoordinates(shooterUid);
        if (shooterCoordinates.MapId != hitCoordinates.MapId)
            return null;

        return (hitCoordinates.Position - shooterCoordinates.Position).Length();
    }

    [SubscribeLocalEvent]
    private void OnOriginProjectileHit(Entity<KsPerkShotOriginComponent> entity, ref ProjectileHitEvent args)
    {
        var hitCoordinates = _transformSystem.GetMapCoordinates(entity.Owner);
        if (entity.Comp.OriginMap != hitCoordinates.MapId || TerminatingOrDeleted(entity.Comp.Shooter))
            return;

        var ev = new KsModifyPerkShotRangeDamageEvent((hitCoordinates.Position - entity.Comp.OriginPosition).Length(), 1f);
        RaiseLocalEvent(entity.Comp.Shooter, ref ev);
        if (ev.Multiplier != 1f)
            args.Damage *= ev.Multiplier;
    }

    /// <summary>
    ///     Whether a hit landed on a mob that is still alive (critical counts).
    /// </summary>
    public bool IsLivingMob(EntityUid targetUid)
    {
        return _mobStateQuery.TryComp(targetUid, out var mobStateComponent) &&
               !_mobStateSystem.IsDead(targetUid, mobStateComponent);
    }
}
