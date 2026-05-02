using Content.Shared.Popups;
using Robust.Shared.Player;

namespace Content.Shared._KS14.KsPopup;

public sealed class KsPopupSystem : EntitySystem
{
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;

    public void PopupTargetAndUser(EntityUid uid, EntityUid userUid, string othersText, string userText, PopupType type = PopupType.Small, bool predicted = true)
    {
        if (predicted)
            _popupSystem.PopupClient(userText, uid, userUid, type: type);
        else
            _popupSystem.PopupEntity(userText, uid, uid, type: type);

        _popupSystem.PopupEntity(othersText, uid, Filter.PvsExcept(userUid), true, type: type);
    }
}
