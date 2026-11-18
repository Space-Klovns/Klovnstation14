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
    ///     Shows each of <paramref name="popups"/> to its own audience, at <paramref name="uid"/>: whoever acts, whoever
    ///         it is done to, and everyone else who can see it. Every message gets the same <paramref name="arguments"/>.
    ///     Safe to call from predicted code: the user's client shows theirs at once.
    /// </summary>
    public void PopupUserTargetOthers(EntityUid uid, EntityUid userUid, EntityUid targetUid, KsAudiencePopups popups,
        PopupType type, params (string, object)[] arguments)
    {
        if (popups.User is { } userMessage)
            _popupSystem.PopupEntity(new KsPopupMessage(userMessage, arguments), uid, userUid, type);

        if (popups.Target is { } targetMessage && targetUid != userUid)
            _popupSystem.PopupEntity(new KsPopupMessage(targetMessage, arguments), uid, targetUid, type);

        if (popups.Others is not { } othersMessage)
            return;

        var others = Filter.Pvs(uid, entityManager: EntityManager)
            .RemoveWhereAttachedEntity(entity => entity == userUid || entity == targetUid);
        _popupSystem.PopupEntity(new KsPopupMessage(othersMessage, arguments), uid, others, true, type);
    }
}
