using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server.Popups;

public sealed partial class PopupSystem
{
    protected override void PopupLocalized(KsPopupMessage message, KsPopupKind kind, EntityUid? entity,
        EntityCoordinates coordinates, Filter filter, bool recordReplay, PopupType type, int predictionKey = 0)
    {
        RaiseNetworkEvent(new KsLocalizedPopupEvent(message.ToPayload(EntityManager), kind, type, Timing.CurTick,
            entity is { } uid ? GetNetEntity(uid) : NetEntity.Invalid,
            kind == KsPopupKind.Coordinates ? GetNetCoordinates(coordinates) : default,
            predictionKey), filter, recordReplay);
    }
}
