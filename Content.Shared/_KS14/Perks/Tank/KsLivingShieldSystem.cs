using Content.Shared._KS14.Perks.Support;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Actions;
using Content.Shared.Blocking;
using Content.Shared.Damage.Systems;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.StatusEffectNew;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;

namespace Content.Shared._KS14.Perks.Tank;

/// <summary>
///     Raises and lowers the living shield (<see cref="KsLivingShieldAbilityComponent"/>). Raising and lowering are
///         predicted; what it soaks is the server's, as damage is.
/// </summary>
public sealed partial class KsLivingShieldSystem : EntitySystem
{
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private SharedActionsSystem _actionsSystem = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private MovementSpeedModifierSystem _movementSpeedModifierSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;

    [SubscribeLocalEvent]
    private void OnAction(Entity<KsLivingShieldAbilityComponent> entity, ref KsLivingShieldActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        entity.Comp.Action = args.Action.Owner;

        if (entity.Comp.Raised)
        {
            Lower(entity, broken: false);
            return;
        }

        entity.Comp.Raised = true;
        entity.Comp.Remaining = entity.Comp.Capacity;
        Dirty(entity);

        _actionsSystem.SetToggled(entity.Comp.Action, true);
        _movementSpeedModifierSystem.RefreshMovementSpeedModifiers(entity.Owner);
        _audioSystem.PlayPredicted(entity.Comp.RaiseSound, entity.Owner, entity.Owner);
        _popupSystem.PopupEntity(KsPopupMessage.Create("ks-perk-tank-shield-raised", ("user", entity.Owner)), entity.Owner);
    }

    /// <summary>
    ///     Lowers the shield, broken or by choice, and starts its cooldown.
    /// </summary>
    public void Lower(Entity<KsLivingShieldAbilityComponent> entity, bool broken)
    {
        if (!entity.Comp.Raised)
            return;

        entity.Comp.Raised = false;
        entity.Comp.Remaining = 0f;
        Dirty(entity);

        _actionsSystem.SetToggled(entity.Comp.Action, false);
        _actionsSystem.SetCooldown(entity.Comp.Action, entity.Comp.Cooldown);
        _movementSpeedModifierSystem.RefreshMovementSpeedModifiers(entity.Owner);

        if (broken)
        {
            _audioSystem.PlayPredicted(entity.Comp.BreakSound, entity.Owner, null);
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-perk-tank-shield-broken", ("user", entity.Owner)), entity.Owner, PopupType.MediumCaution);
        }
        else
        {
            _audioSystem.PlayPredicted(entity.Comp.LowerSound, entity.Owner, entity.Owner);
        }
    }

    // Soaks what it can of what would really be dealt: after worn armour (the inventory relay), damage-modifying status
    //     effects, a held blocking shield, a mark's vulnerability and the universal mob modifier have all had their say,
    //     so it wears down only by what got past them. Unordered it ran before the armour and spent itself on the whole hit.
    // Not pure, as a Modify handler should be, and it can't be split: DamageDealtEvent carries only the final amount and
    //     can't change it, so how much the shield took is known only here. ChangeDamage raises DamageModifyEvent only
    //     right before dealing the damage, and nothing raises it to ask.
    // Damage is applied on the server, so only the server counts it. A class event, so no Entity<T> handler (RA0054).
    [SubscribeLocalEvent(after: [typeof(InventorySystem), typeof(StatusEffectsSystem), typeof(BlockingSystem), typeof(KsMarkedTargetSystem), typeof(MobStateSystem)])]
    private void OnDamageModify(EntityUid uid, KsLivingShieldAbilityComponent component, DamageModifyEvent args)
    {
        if (_netManager.IsClient || !component.Raised)
            return;

        var total = (float)args.Damage.GetTotal();
        if (total <= 0f)
            return;

        var absorbed = MathF.Min(total, component.Remaining);
        component.Remaining -= absorbed;
        args.Damage *= (total - absorbed) / total;
        Dirty(uid, component);

        if (component.Remaining <= 0f)
            Lower((uid, component), broken: true);
    }

    [SubscribeLocalEvent]
    private void OnShotAttempted(Entity<KsLivingShieldAbilityComponent> entity, ref ShotAttemptedEvent args)
    {
        if (entity.Comp.Raised)
            args.Cancel();
    }

    // A class event, so no Entity<T> handler (RA0054).
    [SubscribeLocalEvent]
    private void OnRefreshSpeed(EntityUid uid, KsLivingShieldAbilityComponent component, RefreshMovementSpeedModifiersEvent args)
    {
        if (component.Raised)
            args.ModifySpeed(component.WalkModifier, component.SprintModifier);
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<KsLivingShieldAbilityComponent> entity, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(entity.Owner))
            return;

        entity.Comp.Raised = false;
        _movementSpeedModifierSystem.RefreshMovementSpeedModifiers(entity.Owner);
    }
}
