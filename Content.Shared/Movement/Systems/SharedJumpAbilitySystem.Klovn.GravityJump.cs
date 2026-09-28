using System.Numerics;
using Content.Shared.Actions;
using Content.Shared.Damage.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Shared.Movement.Systems;

public sealed partial class SharedJumpAbilitySystem : EntitySystem
{
    [Dependency] private SharedStaminaSystem _staminaSystem = default!;
    [Dependency] private PullingSystem _pullingSystem = default!;
    [Dependency] private SharedMoverController _moverController = default!;
    [Dependency] private IGameTiming _gameTiming = default!;

    private void InitialiseKlovn()
    {
        SubscribeLocalEvent<JumpAbilityComponent, KsGravityJumpWorldEvent>(OnKsGravityJumpWorldEvent);
    }

    private void OnKsGravityJumpWorldEvent(Entity<JumpAbilityComponent> entity, ref KsGravityJumpWorldEvent args)
    {
        KsHandleEvent(entity, ref args);
    }

    private void KsHandleEvent<TEvent>(Entity<JumpAbilityComponent> entity, ref TEvent args) where TEvent : BaseActionEvent, IKsGravityJumpEvent
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
        EntityCoordinates direction;
        if (args is WorldTargetActionEvent worldTargetArgs)
            direction = worldTargetArgs.Target;
        else
        {
            var xform = Transform(args.Performer);

            // for direction, we will try to use the direction that the player is trying to move. If we can't get that or they aren't trying to move, just use the direction they're facing.
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
        }
        // KS14 change end

        _throwing.TryThrow(args.Performer, direction, entity.Comp.JumpThrowSpeed);
        _audio.PlayPredicted(entity.Comp.JumpSound, args.Performer, args.Performer);

        // KS14: changed logic
        EnsureComp<ActiveLeaperComponent>(entity, out var leaperComp);
        if (entity.Comp.CanCollide)
        {
            leaperComp.KnockdownDuration = entity.Comp.CollideKnockdown;
            leaperComp.StaminaDamage = entity.Comp.HitStaminaDamage;
            leaperComp.HitKnockdownDuration = entity.Comp.HitKnockdownDuration;
        }

        leaperComp.PunishStunDuration = entity.Comp.PunishKnockdown; // KS14 addition
        Dirty(entity.Owner, leaperComp);

        args.Handled = true;
    }
}
