using Content.Shared._KS14.PopupLocalization;
using Content.Shared._KS14.Weapons.Hitscan;
using Content.Shared._KS14.Weapons.Ranged;
using Content.Shared.Hands;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Network;

namespace Content.Shared._KS14.Perks.Damage;

/// <summary>
///     Ramps a shooter's damage up with every hit on a living mob (<see cref="KsDamageRampAbilityComponent"/>).
///         Bullets carry the bonus from when they were fired; a hitscan shot gets it as it lands. Hits are counted on
///         the server, which is the only side that knows they happened.
/// </summary>
public sealed partial class KsDamageRampSystem : EntitySystem
{
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private KsPerkGunSystem _perkGunSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private EntityQuery<KsDamageRampAbilityComponent> _rampQuery = default!;
    [Dependency] private EntityQuery<ProjectileComponent> _projectileQuery = default!;

    /// <summary>
    ///     The damage multiplier the next shot gets.
    /// </summary>
    public float GetMultiplier(KsDamageRampAbilityComponent component)
    {
        return 1f + MathF.Min(component.Hits * component.BonusPerHit, component.MaxBonus);
    }

    // Before the shot leaves: a reload or a different gun starts the ramp over. The rounds for this shot are already
    //      taken, so the gun had that many more than it has now. A class event, so no Entity<T> handler (RA0054).
    [SubscribeLocalEvent]
    private void OnBeforeShot(EntityUid uid, KsDamageRampAbilityComponent component, SelfBeforeGunShotEvent args)
    {
        var entity = new Entity<KsDamageRampAbilityComponent>(uid, component);
        var gunUid = args.Gun.Owner;
        var countAfter = _perkGunSystem.GetAmmoCount(gunUid);
        var countBefore = countAfter + args.Ammo.Count;
        var hasSource = _perkGunSystem.TryGetAmmoSource(gunUid, out var sourceUid);

        if (component.Gun != gunUid)
        {
            Reset(entity);
            component.Gun = gunUid;
            component.AmmoSource = hasSource ? sourceUid : null;
        }
        // No magazine in: the round in the chamber is still the old magazine's.
        else if (hasSource && component.AmmoSource != sourceUid ||
                 countBefore > component.LastCount)
        {
            Reset(entity);
            component.AmmoSource = hasSource ? sourceUid : component.AmmoSource;
        }

        component.LastCount = countAfter;
        Dirty(entity);
    }

    [SubscribeLocalEvent]
    private void OnFired(Entity<KsDamageRampAbilityComponent> entity, ref KsShooterFiredGunEvent args)
    {
        var multiplier = GetMultiplier(entity.Comp);
        foreach (var projectileUid in args.ProjectileUids)
        {
            if (!_projectileQuery.TryComp(projectileUid, out var projectileComponent))
                continue;

            projectileComponent.Damage *= multiplier;
            var shotComponent = EnsureComp<KsDamageRampShotComponent>(projectileUid);
            shotComponent.Shooter = entity.Owner;
            shotComponent.Generation = entity.Comp.Generation;
        }

        // Emptied: the ramp goes with the last round, after it has had its bonus.
        if (_perkGunSystem.GetAmmoCount(args.Gun) <= 0)
            Reset(entity);
    }

    [SubscribeLocalEvent]
    private void OnHitscanModify(Entity<KsDamageRampAbilityComponent> entity, ref KsHitscanDamageModifyEvent args)
    {
        if (entity.Comp.Gun == args.Data.Gun)
            args.Damage *= GetMultiplier(entity.Comp);
    }

    [SubscribeLocalEvent]
    private void OnHitscanHit(Entity<KsDamageRampAbilityComponent> entity, ref KsShooterHitscanHitEvent args)
    {
        if (args.Data.HitEntity is { } targetUid && entity.Comp.Gun == args.Data.Gun)
            TryCountHit(entity, targetUid, entity.Comp.Generation);
    }

    [SubscribeLocalEvent]
    private void OnProjectileHit(Entity<KsDamageRampShotComponent> entity, ref ProjectileHitEvent args)
    {
        if (_rampQuery.TryComp(entity.Comp.Shooter, out var rampComponent))
            TryCountHit((entity.Comp.Shooter, rampComponent), args.Target, entity.Comp.Generation);
    }

    [SubscribeLocalEvent]
    private void OnDropped(Entity<KsDamageRampAbilityComponent> entity, ref DidUnequipHandEvent args)
    {
        if (args.Unequipped != entity.Comp.Gun)
            return;

        Reset(entity);
        entity.Comp.Gun = null;
        entity.Comp.AmmoSource = null;
        Dirty(entity);
    }

    private void TryCountHit(Entity<KsDamageRampAbilityComponent> entity, EntityUid targetUid, int generation)
    {
        // A hit is the server's to count: the client's copy of a bullet can hit what the server's misses.
        if (_netManager.IsClient ||
            generation != entity.Comp.Generation ||
            entity.Comp.Hits * entity.Comp.BonusPerHit >= entity.Comp.MaxBonus ||
            !_perkGunSystem.IsLivingMob(targetUid))
            return;

        entity.Comp.Hits++;
        Dirty(entity);

        var bonus = MathF.Min(entity.Comp.Hits * entity.Comp.BonusPerHit, entity.Comp.MaxBonus);
        var maxed = bonus >= entity.Comp.MaxBonus;
        _popupSystem.PopupEntity(
            KsPopupMessage.Create("ks-perk-damage-ramp-up", ("percent", (int)MathF.Round(bonus * 100f)), ("maxed", maxed)),
            entity.Owner,
            entity.Owner,
            maxed ? PopupType.Medium : PopupType.Small);
    }

    private void Reset(Entity<KsDamageRampAbilityComponent> entity)
    {
        entity.Comp.Generation++;
        if (entity.Comp.Hits > 0)
        {
            entity.Comp.Hits = 0;
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-perk-damage-ramp-lost"), entity.Owner, entity.Owner);
        }

        Dirty(entity);
    }
}
