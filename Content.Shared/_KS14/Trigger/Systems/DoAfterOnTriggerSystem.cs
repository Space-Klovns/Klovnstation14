using Content.Shared._KS14.Trigger.Components;
using Content.Shared.DoAfter;
using Content.Shared.Popups;
using Content.Shared.Trigger;
using Content.Shared.Trigger.Systems;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.Trigger.Systems;

public sealed partial class DoAfterOnTriggerSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private TriggerSystem _triggerSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DoAfterOnTriggerComponent, TriggerEvent>(OnTrigger);
        SubscribeLocalEvent<DoAfterOnTriggerComponent, DoAfterOnTriggerDoAfterEvent>(OnDoAfter);
    }

    private void OnTrigger(Entity<DoAfterOnTriggerComponent> entity, ref TriggerEvent args)
    {
        if (args.Key is { } key &&
            !entity.Comp.KeysIn.Contains(key))
            return;

        if (entity.Comp.CurrentUserUid is { } currentUserUid &&
            entity.Comp.BlockDuplicate &&
            currentUserUid != args.User)
        {
            if (args.User is { } userUid &&
                entity.Comp.DuplicatePopupLoc is { } duplicatePopupLoc)
                _popupSystem.PopupClient(Loc.GetString(duplicatePopupLoc), userUid, userUid);

            return;
        }

        var curTime = _gameTiming.CurTime;
        if (entity.Comp.NextAllowedTime > curTime)
        {
            if (args.User is { } userUid &&
                entity.Comp.CooldownPopupLoc is { } cooldownPopupLoc)
            {
                var timeLeftSeconds = (float)(entity.Comp.NextAllowedTime - curTime).TotalSeconds;
                _popupSystem.PopupClient(Loc.GetString(cooldownPopupLoc, ("time", $"{timeLeftSeconds:0.#}")), userUid, userUid);
            }

            return;
        }

        if (entity.Comp.TargetUser &&
            args.User is not { })
            return;

        var doerUid = args.User ?? entity.Owner;
        var ev = new DoAfterOnTriggerDoAfterEvent();
        if (!_doAfterSystem.TryStartDoAfter(
            new DoAfterArgs(EntityManager, doerUid, entity.Comp.Duration, ev, entity, target: entity)
            { BlockDuplicate = entity.Comp.BlockDuplicate, DuplicateCondition = DuplicateConditions.SameTarget | DuplicateConditions.SameEvent, BreakOnDamage = true }))
            return;

        entity.Comp.CurrentUserUid = args.User;
        DirtyField(entity!, nameof(entity.Comp.CurrentUserUid));

        entity.Comp.NextAllowedTime = curTime + entity.Comp.Cooldown;
        DirtyField(entity!, nameof(entity.Comp.NextAllowedTime));

        if (entity.Comp.StartKeyOut is { })
            _triggerSystem.Trigger(entity, user: args.User, key: entity.Comp.StartKeyOut, predicted: true);

        args.Handled = true;
        args.Predicted = true;
    }

    private void OnDoAfter(Entity<DoAfterOnTriggerComponent> entity, ref DoAfterOnTriggerDoAfterEvent args)
    {
        entity.Comp.CurrentUserUid = null;
        DirtyField(entity!, nameof(entity.Comp.CurrentUserUid));

        if (args.Cancelled)
        {
            _triggerSystem.Trigger(entity, user: args.User, key: entity.Comp.CancelledKeyOut, predicted: true);
            return;
        }

        _triggerSystem.Trigger(entity, user: args.User, key: entity.Comp.KeyOut, predicted: true);
    }
}

[Serializable, NetSerializable]
public sealed partial class DoAfterOnTriggerDoAfterEvent : DoAfterEvent
{
    public override DoAfterEvent Clone() => this;
}
