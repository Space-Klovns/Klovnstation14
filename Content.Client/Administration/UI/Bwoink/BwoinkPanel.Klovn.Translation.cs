using Content.Client._KS14.Translation;
using Content.Shared._KS14.Translation;
using Content.Shared.Administration;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Utility;

namespace Content.Client.Administration.UI.Bwoink;

public sealed partial class BwoinkPanel
{
    private static readonly Type[] KsTranslationTags =
    [
        typeof(BoldItalicTag), typeof(BoldTag), typeof(BulletTag),
        typeof(ColorTag), typeof(HeadingTag), typeof(ItalicTag), typeof(KsBwoinkTranslatedMarkerTag)
    ];
    private readonly Dictionary<Guid, (int Index, SharedBwoinkSystem.BwoinkTextMessage Message, bool Translated)> _ksTranslationLines = new();
    private int _ksLineCount;

    private void KsRememberTranslation(SharedBwoinkSystem.BwoinkTextMessage message)
    {
        if (message.KsTranslationId is { } id && message.KsTranslationText != null)
            _ksTranslationLines[id] = (_ksLineCount, message, false);
        _ksLineCount++;
    }

    // UPSTREAM-BS-TRANSLATION-001: replace only the existing row; no sound, unread count or window opening.
    public bool KsTryReplaceTranslation(KsBwoinkTranslationEvent replacement)
    {
        if (!_ksTranslationLines.TryGetValue(replacement.MessageId, out var line) || line.Message.UserId != replacement.Channel)
            return false;
        if (line.Translated)
            return true;

        var message = line.Message;
        var original = message.KsTranslationText!;
        var escaped = FormattedMessage.EscapeText(original);
        // The server appends the escaped body after the sender/prefix. Never replace names or markup.
        if (!message.Text.EndsWith(escaped, StringComparison.Ordinal))
            return true;

        var formatted = new FormattedMessage();
        formatted.AddMarkupOrThrow($"[color=gray]{message.SentAt.ToShortTimeString()}[/color] ");
        formatted.PushTag(new MarkupNode(KsBwoinkTranslatedMarkerTag.TagName, new MarkupParameter(original), null), selfClosing: true);
        formatted.AddMarkupOrThrow(message.Text[..^escaped.Length]);
        formatted.AddText(replacement.Text);
        TextOutput.SetMessage(line.Index, formatted, KsTranslationTags);
        _ksTranslationLines[replacement.MessageId] = (line.Index, message, true);
        return true;
    }
}
