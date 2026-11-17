using Content.Shared._KS14.PopupLocalization;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Shared.Popups;

public abstract partial class SharedPopupSystem
{
    protected abstract void PopupLocalized(KsPopupMessage message, KsPopupKind kind, EntityUid? entity,
        EntityCoordinates coordinates, Filter filter, bool recordReplay, PopupType type, int predictionKey = 0);

    protected virtual Filter RecipientFilter(EntityUid? recipient) => TryComp<ActorComponent>(recipient, out var actor)
        ? Filter.SinglePlayer(actor.PlayerSession) : Filter.Empty();

    public void PopupEntity(KsPopupMessage message, EntityUid uid, PopupType type = PopupType.Small)
        => PopupEntity(message, uid, Filter.Pvs(uid), true, type);

    public void PopupEntity(KsPopupMessage message, EntityUid uid, EntityUid? recipient, PopupType type = PopupType.Small)
        => PopupEntity(message, uid, RecipientFilter(recipient), false, type);

    public void PopupEntity(KsPopupMessage message, EntityUid uid, ICommonSession recipient, PopupType type = PopupType.Small)
        => PopupEntity(message, uid, Filter.SinglePlayer(recipient), false, type);

    public void PopupEntity(KsPopupMessage message, EntityUid uid, Filter filter, bool recordReplay, PopupType type = PopupType.Small)
        => PopupLocalized(message, KsPopupKind.Entity, uid, default, filter, recordReplay, type);

    public void PopupEntity(KsPopupMessage recipientMessage, KsPopupMessage othersMessage, EntityUid uid,
        EntityUid? recipient, PopupType type = PopupType.Small)
    {
        if (recipient is { } recipientUid)
        {
            PopupEntity(othersMessage, uid, Filter.PvsExcept(recipientUid), true, type);
            PopupEntity(recipientMessage, uid, recipientUid, type);
        }
        else
            PopupEntity(othersMessage, uid, type);
    }

    public void PopupEntity(KsPopupMessage recipientMessage, string? othersMessage, EntityUid uid,
        EntityUid? recipient, PopupType type = PopupType.Small)
    {
        if (recipient is { } recipientUid)
        {
            PopupEntity(othersMessage, uid, Filter.PvsExcept(recipientUid), true, type);
            PopupEntity(recipientMessage, uid, recipientUid, type);
        }
        else
            PopupEntity(othersMessage, uid, type);
    }

    public void PopupCursor(KsPopupMessage message, EntityUid? recipient, PopupType type = PopupType.Small)
        => PopupCursor(message, RecipientFilter(recipient), false, type);

    public void PopupCursor(KsPopupMessage message, ICommonSession recipient, PopupType type = PopupType.Small)
        => PopupCursor(message, Filter.SinglePlayer(recipient), false, type);

    public void PopupCursor(KsPopupMessage message, Filter filter, bool recordReplay, PopupType type = PopupType.Small)
        => PopupLocalized(message, KsPopupKind.Cursor, null, default, filter, recordReplay, type);

    public void PopupCoordinates(KsPopupMessage message, EntityCoordinates coordinates,
        PopupType type = PopupType.Small, int predictionKey = 0)
        => PopupCoordinates(message, coordinates, Filter.Pvs(coordinates), true, type, predictionKey);

    public void PopupCoordinates(KsPopupMessage message, EntityCoordinates coordinates, EntityUid? recipient,
        PopupType type = PopupType.Small, int predictionKey = 0)
        => PopupCoordinates(message, coordinates, RecipientFilter(recipient), false, type, predictionKey);

    public void PopupCoordinates(KsPopupMessage message, EntityCoordinates coordinates, ICommonSession recipient,
        PopupType type = PopupType.Small, int predictionKey = 0)
        => PopupCoordinates(message, coordinates, Filter.SinglePlayer(recipient), false, type, predictionKey);

    public void PopupCoordinates(KsPopupMessage message, EntityCoordinates coordinates, Filter filter,
        bool recordReplay, PopupType type = PopupType.Small, int predictionKey = 0)
        => PopupLocalized(message, KsPopupKind.Coordinates, null, coordinates, filter, recordReplay, type, predictionKey);

    public void PopupClient(KsPopupMessage message, EntityUid? recipient, PopupType type = PopupType.Small)
    {
        if (recipient is { } uid)
            PopupEntity(message, uid, recipient, type);
    }

    public void PopupClient(KsPopupMessage message, EntityUid uid, EntityUid? recipient, PopupType type = PopupType.Small)
        => PopupEntity(message, uid, recipient, type);

    public void PopupClient(KsPopupMessage message, EntityCoordinates coordinates, EntityUid? recipient, PopupType type = PopupType.Small)
        => PopupCoordinates(message, coordinates, recipient, type);

    public void PopupPredicted(KsPopupMessage message, EntityUid uid, EntityUid? recipient, PopupType type = PopupType.Small)
        => PopupEntity(message, uid, type);

    public void PopupPredicted(KsPopupMessage message, EntityUid uid, EntityUid? recipient, Filter filter,
        bool recordReplay, PopupType type = PopupType.Small)
        => PopupEntity(message, uid, filter, recordReplay, type);

    public void PopupPredicted(KsPopupMessage recipientMessage, KsPopupMessage othersMessage, EntityUid uid,
        EntityUid? recipient, PopupType type = PopupType.Small)
        => PopupEntity(recipientMessage, othersMessage, uid, recipient, type);

    public void PopupPredicted(KsPopupMessage recipientMessage, string? othersMessage, EntityUid uid,
        EntityUid? recipient, PopupType type = PopupType.Small)
        => PopupEntity(recipientMessage, othersMessage, uid, recipient, type);

    public void PopupPredictedCursor(KsPopupMessage message, EntityUid recipient, PopupType type = PopupType.Small)
        => PopupCursor(message, recipient, type);

    public void PopupPredictedCursor(KsPopupMessage message, ICommonSession recipient, PopupType type = PopupType.Small)
        => PopupCursor(message, recipient, type);

    public void PopupPredictedCoordinates(KsPopupMessage message, EntityCoordinates coordinates,
        EntityUid? recipient, PopupType type = PopupType.Small)
        => PopupCoordinates(message, coordinates, type);
}
