using Content.Shared._KS14.PopupLocalization; // KS14: deferred popup localization
using Content.Shared.Popups;
using Content.Shared.Whitelist;

namespace Content.Shared.Lock;

public sealed partial class LockingWhitelistSystem : EntitySystem
{
    [Dependency] private EntityWhitelistSystem _whitelistSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LockingWhitelistComponent, UserLockToggleAttemptEvent>(OnUserLockToggleAttempt);
    }

    private void OnUserLockToggleAttempt(Entity<LockingWhitelistComponent> ent, ref UserLockToggleAttemptEvent args)
    {
        if (_whitelistSystem.CheckBoth(args.Target, ent.Comp.Blacklist, ent.Comp.Whitelist))
            return;

        if (!args.Silent)
            _popupSystem.PopupClient(KsPopupMessage.Create /* KS14: localize popups on the recipient */("locking-whitelist-component-lock-toggle-deny"), ent.Owner);

        args.Cancelled = true;
    }
}
