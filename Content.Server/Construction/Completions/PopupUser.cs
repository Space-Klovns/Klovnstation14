using Content.Shared._KS14.PopupLocalization; // KS14: deferred popup localization
using Content.Server.Popups;
using Content.Shared.Construction;
using JetBrains.Annotations;
using Robust.Shared.Player;

namespace Content.Server.Construction.Completions
{
    [UsedImplicitly]
    [DataDefinition]
    public sealed partial class PopupUser : IGraphAction
    {
        [DataField("cursor")] public bool Cursor { get; private set; }
        [DataField("text")] public string Text { get; private set; } = string.Empty;

        public void PerformAction(EntityUid uid, EntityUid? userUid, IEntityManager entityManager)
        {
            if (userUid == null)
                return;

            var popupSystem = entityManager.EntitySysManager.GetEntitySystem<PopupSystem>();

            if(Cursor)
                popupSystem.PopupCursor(KsPopupMessage.Create /* KS14: localize popups on the recipient */(Text), userUid.Value);
            else
                popupSystem.PopupEntity(KsPopupMessage.Create /* KS14: localize popups on the recipient */(Text), uid, userUid.Value);
        }
    }
}
