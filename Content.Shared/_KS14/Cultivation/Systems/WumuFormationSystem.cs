using Content.Shared._KS14.Cultivation.Components;
using Content.Shared.Damage.Events;
using Content.Shared.Movement.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.Cultivation.Systems;

/// <summary>
///     "Shake a mountain, easy. Shake the Yue Family Army, impossible." Soldiers rallied by the Wumu Yishu move faster and
///         shrug off half the stamina damage they take, until the formation breaks up.
/// </summary>
public sealed partial class WumuFormationSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private MovementSpeedModifierSystem _movementSpeedModifierSystem = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _gameTiming.CurTime;
        var query = EntityQueryEnumerator<WumuFormationComponent>();
        while (query.MoveNext(out var uid, out var formationComponent))
        {
            if (now >= formationComponent.Until)
                RemCompDeferred<WumuFormationComponent>(uid);
        }
    }

    public void Rally(EntityUid uid, TimeSpan duration)
    {
        var formationComponent = EnsureComp<WumuFormationComponent>(uid);
        formationComponent.Until = _gameTiming.CurTime + duration;
        Dirty(uid, formationComponent);
        _movementSpeedModifierSystem.RefreshMovementSpeedModifiers(uid);
    }

    [SubscribeLocalEvent]
    private void OnFormationRemoved(Entity<WumuFormationComponent> entity, ref ComponentRemove args)
    {
        if (!TerminatingOrDeleted(entity))
            _movementSpeedModifierSystem.RefreshMovementSpeedModifiers(entity);
    }

    [SubscribeLocalEvent]
    private void OnFormationSpeed(Entity<WumuFormationComponent> entity, ref RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(entity.Comp.SpeedMultiplier, entity.Comp.SpeedMultiplier);
    }

    [SubscribeLocalEvent]
    private void OnFormationStamina(Entity<WumuFormationComponent> entity, ref BeforeStaminaDamageEvent args)
    {
        if (args.Value > 0f)
            args.Value *= entity.Comp.StaminaMultiplier;
    }
}
