// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 ScarKy0
// SPDX-FileCopyrightText: 2025 github_actions[bot]
// SPDX-FileCopyrightText: 2025 slarticodefast
// SPDX-FileCopyrightText: 2025 Голубь
//
// SPDX-License-Identifier: MPL-2.0

using System.Numerics; // KS14
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Cloning.Events;
using Content.Shared.Damage.Systems; // KS14
using Content.Shared.Gravity;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems; // KS14
using Content.Shared.Popups;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map; // KS14
using Robust.Shared.Physics.Events;
using Robust.Shared.Timing;

namespace Content.Shared.Movement.Systems;

public sealed partial class SharedJumpAbilitySystem : EntitySystem
{
    [Dependency] private readonly ThrowingSystem _throwing = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedGravitySystem _gravity = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedStaminaSystem _staminaSystem = default!; // KS14
    [Dependency] private readonly PullingSystem _pullingSystem = default!; // KS14
    [Dependency] private readonly SharedMoverController _moverController = default!; // KS14
    [Dependency] private readonly IGameTiming _gameTiming = default!; // KS14

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
        if (entity.Comp.KnockdownDuration is { } collisionKnockdownDuration) // KS14 change: made optional
            _stun.TryKnockdown(entity.Owner, collisionKnockdownDuration, force: true);

        if (entity.Comp.StaminaDamage != 0f && _gameTiming.IsFirstTimePredicted /* which genius thought to predict this event */) // KS14 addition
            _staminaSystem.TakeStaminaDamage(args.OtherEntity, entity.Comp.StaminaDamage);

        RemCompDeferred<ActiveLeaperComponent>(entity);
    }

    private void OnLeaperLand(Entity<ActiveLeaperComponent> entity, ref LandEvent args)
    {
        if (entity.Comp.GuaranteedKnockdownDuration is { } guaranteedKnockdownDuration) // KS14 addition
            _stun.TryKnockdown(entity.Owner, guaranteedKnockdownDuration, force: true, refresh: false, drop: false);

        RemCompDeferred<ActiveLeaperComponent>(entity);
    }

    private void OnLeaperStopThrow(Entity<ActiveLeaperComponent> entity, ref StopThrowEvent args)
    {
        RemCompDeferred<ActiveLeaperComponent>(entity);
    }

    private void OnGravityJump(Entity<JumpAbilityComponent> entity, ref GravityJumpEvent args)
    {
        if (_gravity.IsWeightless(args.Performer) || _standing.IsDown(args.Performer))
        {
            if (entity.Comp.JumpFailedPopup != null)
                _popup.PopupClient(Loc.GetString(entity.Comp.JumpFailedPopup.Value), args.Performer, args.Performer);
            return;
        }

        // KS14 change: Stamina-cost
        if (args.StaminaCost != 0f)
            _staminaSystem.TakeStaminaDamage(entity, args.StaminaCost, visual: false);

        // KS14 change: Stop pulling
        if (TryComp<PullerComponent>(entity, out var pullerComponent) &&
            pullerComponent.Pulling is { } pulledUid &&
            TryComp<PullableComponent>(pulledUid, out var pullableComponent))
        {
            _pullingSystem.TryStopPull(pulledUid, pullableComponent, entity);
        }

        // KS14 change start: direction is now the direction you're moving, if possible
        var xform = Transform(args.Performer);

        // for direction, we will try to use the direction that the player is trying to move. If we can't get that or they aren't trying to move, just use the direction they're facing.
        EntityCoordinates direction;
        if (TryComp<InputMoverComponent>(entity, out var entityMoverComponent)
            && !entityMoverComponent.WishDir.EqualsApprox(Vector2.Zero))
        {
            // logic reversed from https://github.com/space-wizards/space-station-14/blob/d4909aa88ea621c071119129d7cf6bf29ff6e86b/Content.Shared/Movement/Systems/SharedMoverController.cs#L615
            var negativeParentRotation = -_moverController.GetParentGridAngle(entityMoverComponent);
            var localWishDirUnit = negativeParentRotation.RotateVec(entityMoverComponent.WishDir).Normalized();

            direction = xform.Coordinates.Offset(localWishDirUnit * entity.Comp.JumpDistance);
        }
        else
            direction = xform.Coordinates.Offset(xform.LocalRotation.ToWorldVec() * entity.Comp.JumpDistance); // to make the character jump in the direction he's looking
        // KS14 change end

        _throwing.TryThrow(args.Performer, direction, entity.Comp.JumpThrowSpeed);
        _audio.PlayPredicted(entity.Comp.JumpSound, args.Performer, args.Performer);

        // KS14: changed logic
        EnsureComp<ActiveLeaperComponent>(entity, out var leaperComp);
        if (entity.Comp.CanCollide)
        {
            leaperComp.KnockdownDuration = entity.Comp.CollideKnockdown;
            leaperComp.StaminaDamage = entity.Comp.HitStaminaDamage;
        }

        leaperComp.GuaranteedKnockdownDuration = entity.Comp.FinishKnockdown; // KS14 addition
        Dirty(entity.Owner, leaperComp);

        args.Handled = true;
    }

    private void OnClone(Entity<JumpAbilityComponent> ent, ref CloningEvent args)
    {
        if (!args.Settings.EventComponents.Contains(Factory.GetRegistration(ent.Comp.GetType()).Name))
            return;

        var targetComp = Factory.GetComponent<JumpAbilityComponent>();
        targetComp.Action = ent.Comp.Action;
        targetComp.CanCollide = ent.Comp.CanCollide;
        targetComp.JumpSound = ent.Comp.JumpSound;
        targetComp.CollideKnockdown = ent.Comp.CollideKnockdown;
        targetComp.FinishKnockdown = ent.Comp.FinishKnockdown; // KS14 change
        targetComp.JumpDistance = ent.Comp.JumpDistance;
        targetComp.JumpThrowSpeed = ent.Comp.JumpThrowSpeed;
        AddComp(args.CloneUid, targetComp, true);
    }
}
