using Content.Shared._KS14.PopupLocalization; // KS14: deferred popup localization
using Content.Shared.CCVar;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Content.Shared.UserInterface;
using Robust.Shared.Configuration;

namespace Content.Shared.Shuttles.Systems;

public abstract partial class SharedEmergencyShuttleSystem : EntitySystem
{
    [Dependency] protected IConfigurationManager ConfigManager = default!;
    [Dependency] protected SharedPopupSystem Popup = default!;

    private bool _emergencyEarlyLaunchAllowed;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EmergencyShuttleConsoleComponent, ActivatableUIOpenAttemptEvent>(OnEmergencyOpenAttempt);

        Subs.CVar(ConfigManager, CCVars.EmergencyEarlyLaunchAllowed, value => _emergencyEarlyLaunchAllowed = value, true);
    }

    private void OnEmergencyOpenAttempt(Entity<EmergencyShuttleConsoleComponent> ent, ref ActivatableUIOpenAttemptEvent args)
    {
        // I'm hoping ActivatableUI checks it's open before allowing these messages.
        if (_emergencyEarlyLaunchAllowed)
            return;

        args.Cancel();

        if (!args.Silent)
            Popup.PopupClient(KsPopupMessage.Create /* KS14: localize popups on the recipient */("emergency-shuttle-console-no-early-launches"), ent, args.User);
    }
}
