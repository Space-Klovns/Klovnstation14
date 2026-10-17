using Content.Client._KS14.Chat;
using Robust.Shared.Utility;

namespace Content.Client.UserInterface.Systems.Chat;

public sealed partial class ChatUIController
{
    private void KsInitializeTranslationSession()
    {
        _net.Disconnect += (_, _) => KsResetTranslationSession();
    }

    // UPSTREAM-BS-TRANSLATION-001: IDs are local to one server lifetime, while History survives reconnects.
    private void KsResetTranslationSession()
    {
        foreach (var (_, message) in History)
        {
            if (message.MessageId is not { } id)
                continue;

            // Keep historical tooltips self-contained before the live ID table is reused by another server.
            if (_translationOriginals.TryGetValue(id, out var original))
            {
                var marker = $"[{KsTranslatedMarkerTag.TagName} {KsTranslatedMarkerTag.IdParam}={id}]";
                var frozen = $"[{KsTranslatedMarkerTag.TagName} {KsTranslatedMarkerTag.OriginalParam}=\"{FormattedMessage.EscapeStringParameter(original)}\"]";
                // Only the controller's prefix is markup; an escaped copy in the translated body is plain text.
                if (message.WrappedMessage.StartsWith(marker, StringComparison.Ordinal))
                    message.WrappedMessage = frozen + message.WrappedMessage[marker.Length..];
            }

            message.MessageId = null;
        }

        _translationOriginals.Clear();
        _pendingTranslations.Clear();
        _ksHiddenTranslations.Clear();
    }
}
