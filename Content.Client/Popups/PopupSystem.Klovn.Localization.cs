using System.Linq;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Client.Popups;

public sealed partial class PopupSystem
{
    protected override Filter RecipientFilter(EntityUid? recipient)
        => recipient == _playerManager.LocalEntity && _playerManager.LocalSession is { } session
            ? Filter.SinglePlayer(session) : Filter.Empty();

    protected override void PopupLocalized(KsPopupMessage message, KsPopupKind kind, EntityUid? entity,
        EntityCoordinates coordinates, Filter filter, bool recordReplay, PopupType type, int predictionKey = 0)
    {
        if (!Timing.IsFirstTimePredicted || !filter.Recipients.Contains(_playerManager.LocalSession))
            return;
        var text = message.Format(EntityManager);
        switch (kind)
        {
            case KsPopupKind.Entity:
                if (entity is { } uid)
                    PopupEntity(text, uid, type);
                break;
            case KsPopupKind.Coordinates:
                PopupCoordinates(text, coordinates, type, predictionKey);
                break;
            case KsPopupKind.Cursor:
                PopupCursor(text, type);
                break;
        }
    }

    [SubscribeNetworkEvent]
    private void OnLocalizedPopup(KsLocalizedPopupEvent ev)
    {
        var text = ev.Message.Format(EntityManager);
        // Formatting before matching makes the authoritative message identical to
        // the recipient's predicted one, regardless of the server's culture.
        switch (ev.Kind)
        {
            case KsPopupKind.Entity:
                OnPopupEntityEvent(new PopupEntityEvent(text, ev.Type, ev.Tick, ev.Entity));
                break;
            case KsPopupKind.Coordinates:
                OnPopupCoordinatesEvent(new PopupCoordinatesEvent(text, ev.Type, ev.Tick, ev.Coordinates, ev.PredictionKey));
                break;
            case KsPopupKind.Cursor:
                OnPopupCursorEvent(new PopupCursorEvent(text, ev.Type, ev.Tick));
                break;
        }
    }
}
