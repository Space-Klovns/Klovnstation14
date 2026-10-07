using Content.Shared._KS14.PopupLocalization; // KS14: deferred popup localization
using Content.Server.Ghost;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction.Events;
using Content.Shared.Morgue;
using Content.Shared.Morgue.Components;
using Content.Shared.Popups;
using Robust.Shared.Player;

namespace Content.Server.Morgue;

public sealed partial class CrematoriumSystem : SharedCrematoriumSystem
{
    [Dependency] private GhostSystem _ghostSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CrematoriumComponent, SuicideByEnvironmentEvent>(OnSuicideByEnvironment);
    }

    private void OnSuicideByEnvironment(Entity<CrematoriumComponent> ent, ref SuicideByEnvironmentEvent args)
    {
        if (args.Handled)
            return;

        var victim = args.Victim;
        if (HasComp<ActorComponent>(victim) && Mind.TryGetMind(victim, out var mindId, out var mind))
        {
            _ghostSystem.OnGhostAttempt(mindId, false, mind: mind);

            if (mind.OwnedEntity is { Valid: true } entity)
            {
                Popup.PopupEntity(KsPopupMessage.Create /* KS14: localize popups on the recipient */("crematorium-entity-storage-component-suicide-message"), entity);
            }
        }

        Popup.PopupEntity(KsPopupMessage.Create /* KS14: localize popups on the recipient */("crematorium-entity-storage-component-suicide-message-others",
            ("victim", Identity.Entity(victim, EntityManager))),
            victim,
            Filter.PvsExcept(victim),
            true,
            PopupType.LargeCaution);

        if (EntityStorage.CanInsert(victim, ent.Owner))
        {
            EntityStorage.CloseStorage(ent.Owner);
            Standing.Down(victim, false);
            EntityStorage.Insert(victim, ent.Owner);
        }
        else
        {
            EntityStorage.CloseStorage(ent.Owner);
            Del(victim);
        }
        Cremate(ent.AsNullable());
        args.Handled = true;
    }
}
