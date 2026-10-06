using Content.Shared._KS14.PopupLocalization; // KS14: deferred popup localization
using Content.Shared.Popups;
using Content.Shared.Construction.Components;

namespace Content.Shared.Gravity;

public abstract partial class SharedGravityGeneratorSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popupSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GravityGeneratorComponent, UnanchorAttemptEvent>(OnUnanchorAttempt);
    }

    /// <summary>
    /// Prevent unanchoring when gravity is active
    /// </summary>
    private void OnUnanchorAttempt(Entity<GravityGeneratorComponent> ent, ref UnanchorAttemptEvent args)
    {
        if (!ent.Comp.GravityActive)
            return;

        _popupSystem.PopupClient(KsPopupMessage.Create /* KS14: localize popups on the recipient */("gravity-generator-unanchoring-failed"), ent.Owner, args.User, PopupType.Medium);

        args.Cancel();
    }
}
