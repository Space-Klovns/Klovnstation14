using Content.Shared._KS14.Weapons.Hitscan;
using Content.Shared.Alert;
using Content.Shared._KS14.Weapons.Ranged;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Movement.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Perks.Support;

/// <summary>
///     What being rallied does (<see cref="KsRallyBuffComponent"/>): faster movement and fire on both sides, harder
///         hitting gun shots. Movement speed and the held guns' fire rate are refreshed as it starts and ends, and as a
///         gun comes into or leaves a hand meanwhile.
/// </summary>
public sealed partial class KsRallyBuffSystem : EntitySystem
{
    [Dependency] private MovementSpeedModifierSystem _movementSpeedModifierSystem = default!;
    [Dependency] private SharedGunSystem _gunSystem = default!;
    [Dependency] private SharedHandsSystem _handsSystem = default!;
    [Dependency] private EntityQuery<GunComponent> _gunQuery = default!;
    [Dependency] private EntityQuery<ProjectileComponent> _projectileQuery = default!;

    /// <summary>
    ///     Rallies <paramref name="uid"/> until <paramref name="endTime"/>, at these multiples. Rallied again, it takes the
    ///         later end and the better of each.
    /// </summary>
    public KsRallyBuffComponent Apply(EntityUid uid, TimeSpan endTime, float speedMultiplier, float fireRateMultiplier, float damageMultiplier)
    {
        var buffComponent = EnsureComp<KsRallyBuffComponent>(uid);
        buffComponent.EndTime = buffComponent.EndTime > endTime ? buffComponent.EndTime : endTime;
        buffComponent.SpeedMultiplier = MathF.Max(buffComponent.SpeedMultiplier, speedMultiplier);
        buffComponent.FireRateMultiplier = MathF.Max(buffComponent.FireRateMultiplier, fireRateMultiplier);
        buffComponent.DamageMultiplier = MathF.Max(buffComponent.DamageMultiplier, damageMultiplier);
        Dirty(uid, buffComponent);
        Refresh(uid);
        return buffComponent;
    }

    /// <summary>
    ///     What tells the rallied it has worn off: the alert to clear and the sound to play.
    /// </summary>
    public void SetTells(Entity<KsRallyBuffComponent> entity, ProtoId<AlertPrototype>? alert, SoundSpecifier? endSound)
    {
        entity.Comp.Alert = alert;
        entity.Comp.EndSound = endSound;
    }

    [SubscribeLocalEvent]
    private void OnStartup(Entity<KsRallyBuffComponent> entity, ref ComponentStartup args)
    {
        Refresh(entity.Owner);
    }

    [SubscribeLocalEvent]
    private void OnAfterHandleState(Entity<KsRallyBuffComponent> entity, ref AfterAutoHandleStateEvent args)
    {
        Refresh(entity.Owner);
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<KsRallyBuffComponent> entity, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(entity.Owner))
            return;

        // Refreshed once it is no longer counted.
        entity.Comp.SpeedMultiplier = 1f;
        entity.Comp.FireRateMultiplier = 1f;
        entity.Comp.DamageMultiplier = 1f;
        Refresh(entity.Owner);
    }

    private void Refresh(EntityUid uid)
    {
        _movementSpeedModifierSystem.RefreshMovementSpeedModifiers(uid);
        foreach (var heldUid in _handsSystem.EnumerateHeld(uid))
        {
            if (_gunQuery.HasComp(heldUid))
                _gunSystem.RefreshModifiers(heldUid);
        }
    }

    // A by-value event, so no Entity<T> handler (RA0054).
    [SubscribeLocalEvent]
    private void OnRefreshSpeed(EntityUid uid, KsRallyBuffComponent component, RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(component.SpeedMultiplier, component.SpeedMultiplier);
    }

    [SubscribeLocalEvent]
    private void OnRefreshGun(Entity<KsRallyBuffComponent> entity, ref GunRefreshModifiersEvent args)
    {
        args.FireRate *= entity.Comp.FireRateMultiplier;
    }

    // By-value events, so no Entity<T> handlers (RA0054).
    [SubscribeLocalEvent]
    private void OnEquipHand(EntityUid uid, KsRallyBuffComponent component, DidEquipHandEvent args)
    {
        if (_gunQuery.HasComp(args.Equipped))
            _gunSystem.RefreshModifiers(args.Equipped);
    }

    [SubscribeLocalEvent]
    private void OnUnequipHand(EntityUid uid, KsRallyBuffComponent component, DidUnequipHandEvent args)
    {
        if (_gunQuery.HasComp(args.Unequipped))
            _gunSystem.RefreshModifiers(args.Unequipped);
    }

    [SubscribeLocalEvent]
    private void OnFired(Entity<KsRallyBuffComponent> entity, ref KsShooterFiredGunEvent args)
    {
        if (entity.Comp.DamageMultiplier == 1f)
            return;

        foreach (var projectileUid in args.ProjectileUids)
        {
            if (_projectileQuery.TryComp(projectileUid, out var projectileComponent))
                projectileComponent.Damage *= entity.Comp.DamageMultiplier;
        }
    }

    [SubscribeLocalEvent]
    private void OnHitscanModify(Entity<KsRallyBuffComponent> entity, ref KsHitscanDamageModifyEvent args)
    {
        if (entity.Comp.DamageMultiplier != 1f)
            args.Damage *= entity.Comp.DamageMultiplier;
    }
}
