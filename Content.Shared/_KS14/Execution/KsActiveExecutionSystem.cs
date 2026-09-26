using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.Execution;

// intentionally prevents you from shooting, rather than just cancelling the doafter

public sealed partial class KsActiveExecutionSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;

    private static readonly TimeSpan PopupDelay = TimeSpan.FromSeconds(0.8d);

    private static readonly LocId MeleePopupLocId = "suicide-popup-melee-cant";
    private static readonly LocId GunPopupLocId = "suicide-popup-gun-cant";

    // Covers guns as well as melee: SharedGunSystem.AttemptShoot asks CanAttack before every shot.
    [SubscribeLocalEvent]
    private void OnAttemptAttack(Entity<KsActiveExecutionComponent> entity, ref AttackAttemptEvent args)
    {
        if (args.Cancelled ||
            entity.Comp.VictimUid != args.Uid)
            return;

        // you cant hit or shoot something while trying to kill yourself
        args.Cancel();

        // Only asking, not attacking, so there is nothing to tell them about.
        if (args.Pure)
            return;

        // A melee attempt names its weapon. The only real attempt that doesn't is a gun asking before it fires.
        DoPopup(entity, args.Weapon == null ? GunPopupLocId : MeleePopupLocId, args.Uid);
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
