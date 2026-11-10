using System.Linq;
using Content.Shared._KS14.WordFilter;
using Content.Shared.Chat;
using Robust.Shared.Utility;

namespace Content.Client.UserInterface.Systems.Chat;

public sealed partial class ChatUIController
{
    private const int KsMaxHiddenTranslations = 512;
    private readonly Dictionary<int, (ChatMessage Message, string WrappedTemplate, bool Translated)> _ksHiddenTranslations = new();

    private void KsRememberHiddenTranslation(ChatMessage message, string wrappedTemplate)
    {
        if (message.MessageId is not { } id)
            return;

        if (_ksHiddenTranslations.Count >= KsMaxHiddenTranslations)
            _ksHiddenTranslations.Remove(_ksHiddenTranslations.Keys.First());
        _ksHiddenTranslations[id] = (message, wrappedTemplate, false);
        if (_pendingTranslations.Remove(id, out var pending))
            KsTryApplyHiddenTranslation(id, pending);
    }

    // UPSTREAM-BS-TRANSLATION-001: update queued/visible bubbles without exposing a hidden chat row.
    private bool KsTryApplyHiddenTranslation(int id, string translated)
    {
        if (!_ksHiddenTranslations.TryGetValue(id, out var entry))
            return false;
        if (entry.Translated)
            return true;
        var message = entry.Message;

        if (_ksSlurFilterEnabled && _wordFilterSystem != null)
            _wordFilterSystem.FilterAndReplaceString(ref translated, WordFilterCategory.Slur);

        var original = FormattedMessage.EscapeText(message.Message);
        var index = entry.WrappedTemplate.LastIndexOf(original, StringComparison.Ordinal);
        if (index < 0)
            return true;

        message.WrappedMessage = entry.WrappedTemplate[..index] + FormattedMessage.EscapeText(translated) +
                                 entry.WrappedTemplate[(index + original.Length)..];
        message.Message = translated;
        KsApplyChatHighlights(message);
        _ksHiddenTranslations[id] = (message, entry.WrappedTemplate, true);
        UpdateTranslatedSpeechBubbles(message);
        return true;
    }

    private void KsRemoveHiddenTranslations(MsgDeleteChatMessagesBy deletion)
    {
        foreach (var (id, entry) in _ksHiddenTranslations.ToArray())
        {
            if (entry.Message.SenderKey != deletion.Key && !deletion.Entities.Contains(entry.Message.SenderEntity))
                continue;
            _ksHiddenTranslations.Remove(id);
            _pendingTranslations.Remove(id);
        }
    }
}
