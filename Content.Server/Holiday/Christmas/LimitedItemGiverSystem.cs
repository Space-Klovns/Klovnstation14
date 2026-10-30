using Content.Shared._KS14.PopupLocalization; // KS14: deferred popup localization
using Content.Server.Hands.Systems;
using Content.Server.Popups;
using Content.Shared.Interaction;
using Content.Shared.Storage;
using Robust.Shared.Player;

namespace Content.Server.Holiday.Christmas;

/// <summary>
/// This handles handing out items from item givers.
/// </summary>
public sealed partial class LimitedItemGiverSystem : EntitySystem
{
    [Dependency] private HandsSystem _hands = default!;
    [Dependency] private HolidaySystem _holiday = default!;
    [Dependency] private PopupSystem _popup = default!;
    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<LimitedItemGiverComponent, InteractHandEvent>(OnInteractHand);
    }

    private void OnInteractHand(EntityUid uid, LimitedItemGiverComponent component, InteractHandEvent args)
    {
        if (!TryComp<ActorComponent>(args.User, out var actor))
            return;

        if (component.GrantedPlayers.Contains(actor.PlayerSession.UserId) || (component.RequiredHoliday is not null && !_holiday.IsCurrentlyHoliday(component.RequiredHoliday)))
        {
            _popup.PopupEntity(KsPopupMessage.Create /* KS14: localize popups on the recipient */(component.DeniedPopup), uid, args.User);
            return;
        }

        var toGive = EntitySpawnCollection.GetSpawns(component.SpawnEntries);
        var coords = Transform(args.User).Coordinates;

        foreach (var item in toGive)
        {
            if (item is null)
                continue;

            var spawned = Spawn(item, coords);
            _hands.PickupOrDrop(args.User, spawned);
        }

        component.GrantedPlayers.Add(actor.PlayerSession.UserId);
        _popup.PopupEntity(KsPopupMessage.Create /* KS14: localize popups on the recipient */(component.ReceivedPopup), uid, args.User);
    }
}
