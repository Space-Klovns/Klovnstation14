using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Popups;
using Robust.Shared.Player;

namespace Content.Shared._KS14.KsPopup;

public sealed partial class KsPopupSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popupSystem = default!;

    public void PopupTargetAndUser(EntityUid uid, EntityUid userUid, string othersText, string userText, PopupType type = PopupType.Small, bool predicted = true)
    {
        if (predicted)
            _popupSystem.PopupClient(userText, uid, userUid, type: type);
        else
            _popupSystem.PopupEntity(userText, uid, uid, type: type);

        _popupSystem.PopupEntity(othersText, uid, Filter.PvsExcept(userUid), true, type: type);
    }

    /// <summary>
    ///     One message for the one acting, one for the one acted on and one for everyone else who can see it.
    ///     Safe to call from predicted code: the user's client shows theirs at once.
    /// </summary>
    public void PopupUserTargetOthers(EntityUid uid, EntityUid userUid, EntityUid targetUid,
        KsPopupMessage userMessage, KsPopupMessage targetMessage, KsPopupMessage othersMessage,
        PopupType type = PopupType.Small)
    {
        _popupSystem.PopupEntity(userMessage, uid, userUid, type);
        if (targetUid != userUid)
            _popupSystem.PopupEntity(targetMessage, uid, targetUid, type);

        var others = Filter.Pvs(uid, entityManager: EntityManager)
            .RemoveWhereAttachedEntity(entity => entity == userUid || entity == targetUid);
        _popupSystem.PopupEntity(othersMessage, uid, others, true, type);
    }
}
