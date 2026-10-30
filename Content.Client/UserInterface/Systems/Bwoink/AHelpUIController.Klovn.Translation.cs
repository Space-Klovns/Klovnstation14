using Content.Shared._KS14.Translation;
using Content.Shared.Administration;
using Robust.Shared.Network;

namespace Content.Client.UserInterface.Systems.Bwoink;

public sealed partial class AHelpUIController
{
    [Dependency] private IClientNetManager _ksTranslationNet = default!;
    private const int KsMaxPendingTranslations = 512;
    private readonly Dictionary<(NetUserId Channel, Guid Id), KsBwoinkTranslationEvent> _ksPendingTranslations = new();

    private void KsInitializeTranslationLifetime()
    {
        _ksTranslationNet.Disconnect += (_, _) => KsResetTranslationSession();
    }

    // UPSTREAM-BS-TRANSLATION-001: handlers retain a player ID and private panels; neither crosses connections.
    private void KsResetTranslationSession()
    {
        UIHelper?.Close(); // Includes the admin's detached native window.
        UIHelper?.Dispose();
        UIHelper = null;
        _ksPendingTranslations.Clear();
        _discordRelayActive = false;
        UnreadAHelpRead();
    }

    private void KsReceiveTranslation(KsBwoinkTranslationEvent replacement, EntitySessionEventArgs args)
    {
        if (UIHelper?.KsTryReplaceTranslation(replacement) == true)
            return;

        // Original and replacement are independent network events; allow either arrival order.
        if (_ksPendingTranslations.Count >= KsMaxPendingTranslations)
            _ksPendingTranslations.Clear();
        _ksPendingTranslations[(replacement.Channel, replacement.MessageId)] = replacement;
    }

    private void KsApplyPendingTranslation(SharedBwoinkSystem.BwoinkTextMessage message)
    {
        if (message.KsTranslationId is { } id &&
            _ksPendingTranslations.Remove((message.UserId, id), out var replacement))
            UIHelper?.KsTryReplaceTranslation(replacement);
    }
}

public sealed partial class AdminAHelpUIHandler
{
    // BS: UPSTREAM-BS-TRANSLATION-001, route exclusively to an already existing conversation.
    public bool KsTryReplaceTranslation(KsBwoinkTranslationEvent replacement) =>
        _activePanelMap.TryGetValue(replacement.Channel, out var panel) && panel.KsTryReplaceTranslation(replacement);
}

public sealed partial class UserAHelpUIHandler
{
    // BS: UPSTREAM-BS-TRANSLATION-001, a player can only replace a row in their own AHelp.
    public bool KsTryReplaceTranslation(KsBwoinkTranslationEvent replacement) =>
        replacement.Channel == _ownerId && _chatPanel?.KsTryReplaceTranslation(replacement) == true;
}
