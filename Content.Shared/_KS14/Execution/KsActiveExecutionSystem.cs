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

        // Only asking, not attacking, so there is nothing to tell them about.
        if (args.Pure)
            return;

        DoPopup(entity, "suicide-popup-melee-cant", args.Uid);
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
