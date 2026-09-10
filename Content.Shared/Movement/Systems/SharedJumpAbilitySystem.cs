using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Cloning.Events;
using Content.Shared.Gravity;
using Content.Shared.Movement.Components;
using Content.Shared.Popups;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics.Events;

namespace Content.Shared.Movement.Systems;

public sealed partial class SharedJumpAbilitySystem : EntitySystem
{
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedGravitySystem _gravity = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private StandingStateSystem _standing = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<JumpAbilityComponent, MapInitEvent>(OnInit);
        SubscribeLocalEvent<JumpAbilityComponent, ComponentShutdown>(OnShutdown);

        SubscribeLocalEvent<JumpAbilityComponent, GravityJumpEvent>(OnGravityJump);

        SubscribeLocalEvent<ActiveLeaperComponent, StartCollideEvent>(OnLeaperCollide);
        SubscribeLocalEvent<ActiveLeaperComponent, LandEvent>(OnLeaperLand);
        SubscribeLocalEvent<ActiveLeaperComponent, StopThrowEvent>(OnLeaperStopThrow);

        SubscribeLocalEvent<JumpAbilityComponent, CloningEvent>(OnClone);

        InitialiseKlovn(); // KS14
    }

    private void OnInit(Entity<JumpAbilityComponent> entity, ref MapInitEvent args)
    {
        if (!TryComp(entity, out ActionsComponent? comp))
            return;

        _actions.AddAction(entity, ref entity.Comp.ActionEntity, entity.Comp.Action, component: comp);
    }

    private void OnShutdown(Entity<JumpAbilityComponent> entity, ref ComponentShutdown args)
    {
        _actions.RemoveAction(entity.Owner, entity.Comp.ActionEntity);
    }

    private void OnLeaperCollide(Entity<ActiveLeaperComponent> entity, ref StartCollideEvent args)
    {
        if (!_gameTiming.IsFirstTimePredicted) // KS14
            return;

        if (entity.Comp.KnockdownDuration is { } collisionKnockdownDuration) // KS14 change: made optional
            _stun.TryKnockdown(entity.Owner, collisionKnockdownDuration);

        if (entity.Comp.StaminaDamage != 0f) // KS14 addition
            _staminaSystem.TakeStaminaDamage(args.OtherEntity, entity.Comp.StaminaDamage);

        if (entity.Comp.HitKnockdownDuration is { } hitKnockdownDuration && _gameTiming.IsFirstTimePredicted) // KS14 addition
            entity.Comp.Punish = !_stun.TryKnockdown(args.OtherEntity, hitKnockdownDuration, refresh: false, force: true);
        else
            entity.Comp.Punish = true;

        // KS14 Start
        var ksEv = new KsHitByJumpEvent(entity.Owner);
        RaiseLocalEvent(args.OtherEntity, ref ksEv);
        // KS14 End

        RemCompDeferred<ActiveLeaperComponent>(entity);
    }

    private void OnLeaperLand(Entity<ActiveLeaperComponent> entity, ref LandEvent args)
    {
        if (!entity.Comp.Punish) // KS14 addition
        {
            return;
        }
        else
        {
            // Stun them if they didnt hit anything to break their fall
            if (entity.Comp.PunishStunDuration is { } guaranteedKnockdownDuration) // KS14 addition
            {
                _stun.TryAddStunDuration(entity.Owner, guaranteedKnockdownDuration);
                _stun.TryKnockdown(entity.Owner, guaranteedKnockdownDuration, force: true, refresh: false, drop: true);
            }
        }

        RemCompDeferred<ActiveLeaperComponent>(entity);
    }

    private void OnLeaperStopThrow(Entity<ActiveLeaperComponent> entity, ref StopThrowEvent args)
    {
        RemCompDeferred<ActiveLeaperComponent>(entity);
    }

    private void OnGravityJump(Entity<JumpAbilityComponent> entity, ref GravityJumpEvent args)
    {
        // KS14: delegate logic to KS
        KsHandleEvent(entity, ref args);
    }

    private void OnClone(Entity<JumpAbilityComponent> ent, ref CloningEvent args)
    {
        if (!args.Settings.EventComponents.Contains(Factory.GetRegistration(ent.Comp.GetType()).Name))
            return;

        // Make sure to set the datafields before adding the component so that the correct action gets spawned on map init.
        var targetComp = Factory.GetComponent<JumpAbilityComponent>();
        targetComp.Action = ent.Comp.Action;
        targetComp.JumpDistance = ent.Comp.JumpDistance;
        targetComp.JumpThrowSpeed = ent.Comp.JumpThrowSpeed;
        targetComp.CanCollide = ent.Comp.CanCollide;
        targetComp.CollideKnockdown = ent.Comp.CollideKnockdown;
        targetComp.PunishKnockdown = ent.Comp.PunishKnockdown; // KS14 change
        targetComp.JumpSound = ent.Comp.JumpSound;
        targetComp.JumpFailedPopup = ent.Comp.JumpFailedPopup;
        AddComp(args.CloneUid, targetComp, true);
    }
}
