using Content.Shared.Chat;
using Content.Shared.IdentityManagement.Components;
using Content.Shared.Implants.Components;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs;
<<<<<<< HEAD
=======
using Content.Shared.Store;
using Content.Shared.VoiceMask;
>>>>>>> upstream/master

namespace Content.Shared.Implants;

public abstract partial class SharedSubdermalImplantSystem
{
    public void InitializeRelay()
    {
        SubscribeLocalEvent<ImplantedComponent, MobStateChangedEvent>(RelayToImplantEvent);
        SubscribeLocalEvent<ImplantedComponent, AfterInteractUsingEvent>(RelayToImplantEvent);
        SubscribeLocalEvent<ImplantedComponent, SuicideEvent>(RelayToImplantEvent);
        SubscribeLocalEvent<ImplantedComponent, TransformSpeakerNameEvent>(RelayToImplantEvent);
        SubscribeLocalEvent<ImplantedComponent, TransformSpeechEvent>(RelayToImplantEvent);
        SubscribeLocalEvent<ImplantedComponent, SeeIdentityAttemptEvent>(RelayToImplantEvent);
<<<<<<< HEAD
=======
        SubscribeLocalEvent<ImplantedComponent, VoiceMaskToggledEvent>(RelayToImplantEvent);

        // Ref relays, for when you need to write to the event!
        SubscribeLocalEvent<ImplantedComponent, CurrencyInsertAttemptEvent>(RefRelayToImplantEvent);
        SubscribeLocalEvent<ImplantedComponent, GetStoreEvent>(RefRelayToImplantEvent);
>>>>>>> upstream/master
    }

    /// <summary>
    /// Relays events from the implanted to the implant.
    /// </summary>
    private void RelayToImplantEvent<T>(EntityUid uid, ImplantedComponent component, T args) where T : notnull
    {
        if (!_container.TryGetContainer(uid, ImplanterComponent.ImplantSlotId, out var implantContainer))
            return;

        var relayEv = new ImplantRelayEvent<T>(args, uid);
        foreach (var implant in implantContainer.ContainedEntities)
        {
            if (args is HandledEntityEventArgs { Handled: true })
                return;

            RaiseLocalEvent(implant, relayEv);
        }
    }
<<<<<<< HEAD
=======

    /// <summary>
    /// Relays events from the implanted to the implant.
    /// </summary>
    private void RefRelayToImplantEvent<T>(Entity<ImplantedComponent> entity, ref T args) where T : notnull
    {
        if (!_container.TryGetContainer(entity, ImplanterComponent.ImplantSlotId, out var implantContainer))
            return;

        var relayEv = new ImplantRelayEvent<T>(args, entity);
        foreach (var implant in implantContainer.ContainedEntities)
        {
            if (args is HandledEntityEventArgs { Handled: true })
                return;

            RaiseLocalEvent(implant, relayEv);
        }

        args = relayEv.Args;
    }
>>>>>>> upstream/master
}

/// <summary>
/// Wrapper for relaying events from an implanted entity to their implants.
/// </summary>
public sealed class ImplantRelayEvent<T> where T : notnull
{
<<<<<<< HEAD
    public readonly T Event;
=======
    public T Args;
>>>>>>> upstream/master

    public readonly EntityUid ImplantedEntity;

    public ImplantRelayEvent(T ev, EntityUid implantedEntity)
    {
        Args = ev;
        ImplantedEntity = implantedEntity;
    }
}
