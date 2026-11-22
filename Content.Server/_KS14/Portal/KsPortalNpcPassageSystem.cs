using Content.Server._KS14.NPC.Passages;
using Content.Shared._KS14.Portal;
using Content.Shared.Teleportation.Components;
using Content.Shared.Teleportation.Systems;

namespace Content.Server._KS14.Portal;

/// <summary>
///     Portals as passages for NPCs (<see cref="KsNpcPassageSystem"/>): one leads to the portal it is linked to, and is
///         taken by walking into it, which needs nothing from here. On the link and not on the portal, as a view
///         portal need not be a stock one.
/// </summary>
public sealed partial class KsPortalNpcPassageSystem : EntitySystem
{
    [Dependency] private KsPortalTravelSystem _portalTravelSystem = default!;
    [Dependency] private LinkedEntitySystem _linkedEntitySystem = default!;

    [SubscribeLocalEvent]
    private void OnGetDestination(Entity<LinkedEntityComponent> entity, ref KsGetNpcPassageDestinationEvent args)
    {
        // One link only: with more, where it comes out is a roll.
        if (!_linkedEntitySystem.GetLink(entity, out var linkedUid, entity.Comp) || TerminatingOrDeleted(linkedUid.Value))
            return;

        var linkedTransform = Transform(linkedUid.Value);

        // A view portal carries what crosses its doorway itself, wherever to. A stock one takes what touches it, and
        //      one of those that fizzles when asked to cross maps leads nowhere on another map.
        if (!_portalTravelSystem.CarriesTravellers(entity) &&
            (!TryComp<PortalComponent>(entity, out var portalComponent) ||
             linkedTransform.MapUid != Transform(entity).MapUid && !portalComponent.CanTeleportToOtherMaps))
            return;

        args.Destination = linkedTransform.Coordinates;
    }
}
