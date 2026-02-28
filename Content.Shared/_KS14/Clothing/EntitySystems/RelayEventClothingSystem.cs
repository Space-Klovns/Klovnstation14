using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs;

namespace Content.Shared._KS14.Clothing.EntitySystems;

public sealed class RelayEventClothingSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<WornHealthMonitorComponent, MobStateChangedEvent>(RelayToImplantEvent);
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
}

/// <summary>
/// Wrapper for relaying events from the wearer to the clothing.
/// </summary>
public sealed class ClothingRelayEvent<T> where T : notnull
{
    public readonly T Event;

    public readonly EntityUid WearerEntity;

    public ClothingRelayEvent(T ev, EntityUid wearerEntity)
    {
        Event = ev;
        WearerEntity = wearerEntity;
    }
}
