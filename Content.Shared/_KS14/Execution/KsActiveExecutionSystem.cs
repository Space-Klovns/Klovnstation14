using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.Execution;

// intentionally prevents you from shooting, rather than just cancelling the doafter

public sealed partial class KsActiveExecutionSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;

    private static readonly TimeSpan PopupDelay = TimeSpan.FromSeconds(0.8d);

    [SubscribeLocalEvent]
    private void OnAttemptMelee(Entity<KsActiveExecutionComponent> entity, ref AttackAttemptEvent args)
    {
        if (args.Cancelled ||
            entity.Comp.VictimUid != args.Uid)
            return;

        // you cant hit something while trying to kill yourself
        args.Cancel();

        // The client's MeleeWeaponSystem.Update calls CanAttack with no target every tick, purely to query
        //      whether attacking is possible. The server never makes that call, so it never advances
        //      NextPopupTime, and every incoming game state resets the client's prediction of it back to the
        //      server's value - popping up again on the next tick. Real targetless swings re-raise this with
        //      each target they hit, so only popping up for targeted attempts loses nothing that matters.
        if (args.Target == null)
            return;

        DoPopup(entity, "suicide-popup-melee-cant", args.Uid);
    }

    [SubscribeLocalEvent]
    private void OnShotAttempted(Entity<KsActiveExecutionComponent> entity, ref ShotAttemptedEvent args)
    {
        if (args.Cancelled ||
            entity.Comp.VictimUid != args.User)
            return;

        // you cant shoot while trying to kill yourself
        args.Cancel();
        DoPopup(entity, "suicide-popup-gun-cant", args.User);
    }

    private void DoPopup(Entity<KsActiveExecutionComponent> entity, LocId popupId, EntityUid userUid)
    {
        if (_gameTiming.CurTime < entity.Comp.NextPopupTime)
            return;

        _popupSystem.PopupEntity(Loc.GetString(popupId), userUid, userUid, type: PopupType.LargeCaution);
        entity.Comp.NextPopupTime = _gameTiming.CurTime + PopupDelay;
        Dirty(entity);
    }
}
