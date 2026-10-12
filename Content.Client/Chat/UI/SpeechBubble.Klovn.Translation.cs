// KS14: refresh translated speech without restarting the bubble's lifetime or animation.
using Content.Shared.Chat;
using Robust.Shared.Utility;

namespace Content.Client.Chat.UI;

public abstract partial class SpeechBubble
{
    public int? MessageId { get; private set; }

    private string _translationSpeechStyleClass = default!;
    private Color? _translationFontColor;

    private void InitializeTranslation(ChatMessage message, string speechStyleClass, Color? fontColor)
    {
        MessageId = message.MessageId;
        _translationSpeechStyleClass = speechStyleClass;
        _translationFontColor = fontColor;
    }

    public void UpdateTranslation(ChatMessage message)
    {
        RemoveAllChildren();
        var bubble = BuildBubble(message, _translationSpeechStyleClass, fontColor: _translationFontColor);
        AddChild(bubble);
        ForceRunStyleUpdate();
        bubble.Measure(Vector2Helpers.Infinity);
        ContentSize = bubble.DesiredSize;
    }
}
