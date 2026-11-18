using Content.Shared._KS14.Weapons.Hitscan;
using Content.Shared._KS14.Weapons.Ranged;
using Content.Shared.Explosion.EntitySystems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;

namespace Content.Shared._KS14.Perks.Damage;

/// <summary>
///     Makes the last rounds of a freshly loaded full magazine explosive
///         (<see cref="KsExplosiveLastRoundsAbilityComponent"/>). Which rounds is decided as they are fired, on both
///         sides; the explosion is the server's.
/// </summary>
public sealed partial class KsExplosiveLastRoundsSystem : EntitySystem
{
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private SharedExplosionSystem _explosionSystem = default!;
    [Dependency] private KsPerkGunSystem _perkGunSystem = default!;

    /// <summary>
    ///     How many of a magazine's last rounds explode.
    /// </summary>
    public static int GetExplosiveRounds(KsExplosiveLastRoundsAbilityComponent component, int capacity)
    {
        return Math.Max(component.MinRounds, (int)MathF.Round(component.Fraction * capacity));
    }

    // The rounds for this shot are already taken, so the gun had that many more than it has now. A class event, so
    //      no Entity<T> handler (RA0054).
    [SubscribeLocalEvent]
    private void OnBeforeShot(EntityUid uid, KsExplosiveLastRoundsAbilityComponent component, SelfBeforeGunShotEvent args)
    {
        var gunUid = args.Gun.Owner;
        var countAfter = _perkGunSystem.GetAmmoCount(gunUid);
        var countBefore = countAfter + args.Ammo.Count;

        // No magazine in: the round in the chamber is still the old magazine's.
        if (_perkGunSystem.TryGetAmmoSource(gunUid, out var sourceUid) &&
            (component.Gun != gunUid || component.AmmoSource != sourceUid))
        {
            component.Gun = gunUid;
            component.AmmoSource = sourceUid;
            component.Capacity = _perkGunSystem.GetAmmoCapacity(sourceUid);
            component.Eligible = component.Capacity > 0 && countBefore >= component.Capacity;
        }
        // Topped up without a magazine swap (shells into a tube): full again or not, as a new magazine would be.
        else if (component.Gun == gunUid && countBefore > component.LastCount)
        {
            component.Eligible = component.Capacity > 0 && countBefore >= component.Capacity;
        }
        else if (component.Gun != gunUid)
        {
            component.Gun = gunUid;
            component.Eligible = false;
        }

        component.LastCount = countAfter;
        component.ArmedShot = component.Eligible && countAfter < GetExplosiveRounds(component, component.Capacity);
        Dirty(uid, component);
    }

    [SubscribeLocalEvent]
    private void OnFired(Entity<KsExplosiveLastRoundsAbilityComponent> entity, ref KsShooterFiredGunEvent args)
    {
        if (!entity.Comp.ArmedShot)
            return;

        entity.Comp.ArmedShot = false;
        Dirty(entity);

        foreach (var projectileUid in args.ProjectileUids)
        {
            if (!HasComp<ProjectileComponent>(projectileUid) || HasComp<KsExplosiveShotComponent>(projectileUid))
                continue;

            // Filled in before it is added, so the client's startup already sees its colour.
            var shotComponent = new KsExplosiveShotComponent
            {
                TracerColor = entity.Comp.TracerColor,
                Shooter = entity.Owner,
                ExplosionType = entity.Comp.ExplosionType,
                TotalIntensity = entity.Comp.TotalIntensity,
                IntensitySlope = entity.Comp.IntensitySlope,
                MaxIntensity = entity.Comp.MaxIntensity,
            };
            AddComp(projectileUid, shotComponent);
        }

        _audioSystem.PlayPredicted(entity.Comp.FireSound, args.Gun.Owner, entity.Owner);
    }

    // Raised while the shot is being fired, before OnFired, so ArmedShot still says whether this one explodes.
    [SubscribeLocalEvent]
    private void OnHitscanHit(Entity<KsExplosiveLastRoundsAbilityComponent> entity, ref KsShooterHitscanHitEvent args)
    {
        if (!entity.Comp.ArmedShot || args.Data.HitCoordinates is not { } hitCoordinates)
            return;

        if (_netManager.IsClient)
            return;

        // The explosion is queued at an entity: a marker where the ray landed.
        var markerUid = Spawn(null, hitCoordinates);
        Explode(markerUid, entity.Comp.ExplosionType, entity.Comp.TotalIntensity, entity.Comp.IntensitySlope,
            entity.Comp.MaxIntensity, entity.Owner);
        QueueDel(markerUid);
    }

    [SubscribeLocalEvent]
    private void OnProjectileHit(Entity<KsExplosiveShotComponent> entity, ref ProjectileHitEvent args)
    {
        // Once, even for a round that goes on through.
        if (entity.Comp.Exploded)
            return;

        entity.Comp.Exploded = true;
        Explode(entity.Owner, entity.Comp.ExplosionType, entity.Comp.TotalIntensity,
            entity.Comp.IntensitySlope, entity.Comp.MaxIntensity, entity.Comp.Shooter);
    }

    // Does nothing on the client: the shared method is the server's alone.
    private void Explode(EntityUid atUid, string explosionType, float totalIntensity, float slope, float maxIntensity, EntityUid? userUid)
    {
        _explosionSystem.QueueExplosion(atUid, explosionType, totalIntensity, slope, maxIntensity,
            tileBreakScale: 0f, maxTileBreak: 0, canCreateVacuum: false, user: userUid);
    }
}
