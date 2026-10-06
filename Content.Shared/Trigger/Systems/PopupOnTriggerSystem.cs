using Content.Shared._KS14.PopupLocalization; // KS14: deferred popup localization
using Content.Shared.IdentityManagement;
using Content.Shared.Popups;
using Content.Shared.Trigger.Components.Effects;

namespace Content.Shared.Trigger.Systems;

/// <summary>
/// This handles <see cref="PopupOnTriggerComponent"/>
/// </summary>
public sealed partial class PopupOnTriggerSystem : XOnTriggerSystem<PopupOnTriggerComponent>
{
    [Dependency] private SharedPopupSystem _popup = default!;

    protected override void OnTrigger(Entity<PopupOnTriggerComponent> ent, EntityUid target, ref TriggerEvent args)
    {
        object user = args.User != null ? Identity.Entity(args.User.Value, EntityManager) /* KS14: defer identity name */ : KsPopupMessage.Create /* KS14: defer popup argument translation */("generic-unknown");

        // Popups only play for one entity
        if (ent.Comp.Quiet)
        {
            if (ent.Comp.Predicted)
            {
                _popup.PopupClient(KsPopupMessage.Create /* KS14: localize popups on the recipient */(ent.Comp.Text, ("entity", ent), ("user", user)),
                    target,
                    ent.Comp.UserIsRecipient ? args.User : ent.Owner,
                    ent.Comp.PopupType);
            }

            else if (args.User != null)
            {
                _popup.PopupEntity(KsPopupMessage.Create /* KS14: localize popups on the recipient */(ent.Comp.OtherText ?? ent.Comp.Text, ("entity", ent), ("user", user)),
                    target,
                    args.User.Value,
                    ent.Comp.PopupType);
            }

            return;
        }

        // Popups play for all entities
        if (ent.Comp.Predicted)
        {
            _popup.PopupPredicted(KsPopupMessage.Create /* KS14: localize popups on the recipient */(ent.Comp.Text, ("entity", ent), ("user", user)),
                KsPopupMessage.Create(ent.Comp.OtherText ?? ent.Comp.Text, ("entity", ent), ("user", user)),
                target,
                ent.Comp.UserIsRecipient ? args.User : ent.Owner,
                ent.Comp.PopupType);
        }

        else
        {
            _popup.PopupEntity(KsPopupMessage.Create /* KS14: localize popups on the recipient */(ent.Comp.OtherText ?? ent.Comp.Text, ("entity", ent), ("user", user)),
                target,
                ent.Comp.PopupType);
        }
    }
}
