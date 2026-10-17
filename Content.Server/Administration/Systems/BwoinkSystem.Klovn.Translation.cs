using System;
using System.Linq;
using Content.Server._KS14.Translation;
using Content.Shared._KS14.Translation;
using Content.Shared.Administration;
using Robust.Shared.Network;

namespace Content.Server.Administration.Systems;

public sealed partial class BwoinkSystem
{
    [Dependency] private KsTranslationSystem _ksTranslation = default!;

    // UPSTREAM-BS-TRANSLATION-001: called only for recipients authorized by OnBwoinkTextMessage.
    private void KsSendTranslatedBwoink(BwoinkTextMessage message, string plain,
        KsTranslationContext? context, INetChannel channel)
    {
        var id = Guid.NewGuid();
        if (context != null && _ksTranslation.KsTryAHelpReader(plain, context, channel, (_, reader, translated) =>
            {
                // An async result must not outlive this connection or an admin's permission to read AHelp.
                if (!_playerManager.TryGetSessionByChannel(reader, out var session))
                    return;

                var isPlayer = session.UserId == message.UserId && !message.AdminOnly;
                var isAdmin = _adminManager.ActiveAdmins.Contains(session) &&
                              (_adminManager.GetAdminData(session)?.HasFlag(AdminFlags.Adminhelp) ?? false);
                if (!isPlayer && !isAdmin)
                    return;

                RaiseNetworkEvent(new KsBwoinkTranslationEvent(message.UserId, id, translated), reader);
            }))
        {
            message = new BwoinkTextMessage(message.UserId, message.TrueSender, message.Text,
                sentAt: message.SentAt, playSound: message.PlaySound, adminOnly: message.AdminOnly)
            {
                KsTranslationId = id,
                KsTranslationText = plain,
            };
        }

        RaiseNetworkEvent(message, channel);
    }
}
