using Content.Shared.Chat;

namespace Content.Client.UserInterface.Systems.Chat;

public sealed partial class ChatUIController
{
    private void UpdateTranslatedSpeechBubbles(ChatMessage message)
    {
        if (message.MessageId == null
            || !_activeSpeechBubbles.TryGetValue(EntityManager.GetEntity(message.SenderEntity), out var bubbles))
            return;

        for (var i = 0; i < bubbles.Count; i++)
        {
            var bubble = bubbles[i];
            if (bubble.MessageId != message.MessageId)
                continue;

            var oldHeight = bubble.ContentSize.Y;
            bubble.UpdateTranslation(message);
            var heightChange = bubble.ContentSize.Y - oldHeight;

            // Earlier bubbles sit above this one and must move with its new height.
            for (var olderIndex = 0; olderIndex < i; olderIndex++)
                bubbles[olderIndex].VerticalOffset += heightChange;
        }
    }
}
