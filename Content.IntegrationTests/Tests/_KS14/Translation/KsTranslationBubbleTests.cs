using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Content.Client.Chat.UI;
using Content.Client.UserInterface.Systems.Chat;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.Chat;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Robust.Client.UserInterface;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._KS14.Translation;

public sealed class KsTranslationBubbleTests : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true, Fresh = true };

    [TestCase(ChatChannel.Local, true)]
    [TestCase(ChatChannel.Local, false)]
    [TestCase(ChatChannel.Whisper, true)]
    [TestCase(ChatChannel.Whisper, false)]
    public async Task TranslationRefreshesVisibleBubble(ChatChannel channel, bool fancy)
    {
        string[] labels = null;
        string translated = null;
        string displayedMessage = null;
        float originalHeight = 0;
        float translatedHeight = 0;
        float olderOffset = 0;
        TimeSpan originalDeathTime = default;
        TimeSpan translatedDeathTime = default;
        await OverrideCVar(Side.Client, CCVars.ChatEnableFancyBubbles, fancy);
        await Client.WaitPost(() =>
        {
            var controller = Client.Resolve<IUserInterfaceManager>().GetUIController<ChatUIController>();
            var senderUid = CEntMan.SpawnEntity(null, Robust.Shared.Map.MapCoordinates.Nullspace);
            var message = CreateMessage(channel, senderUid, 100001);
            controller.ProcessChatMessage(message, speechBubble: false);

            var speechType = channel == ChatChannel.Whisper ? SpeechBubble.SpeechType.Whisper : SpeechBubble.SpeechType.Say;
            var older = SpeechBubble.CreateSpeechBubble(speechType, CreateMessage(channel, senderUid, 100000), senderUid);
            var bubble = SpeechBubble.CreateSpeechBubble(speechType, message, senderUid);
            var bubbles = (Dictionary<EntityUid, List<SpeechBubble>>) typeof(ChatUIController)
                .GetField("_activeSpeechBubbles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
            bubbles.Add(senderUid, new List<SpeechBubble> { older, bubble });

            try
            {
                older.VerticalOffset = bubble.ContentSize.Y;
                var deathTime = typeof(SpeechBubble).GetField("_deathTime", BindingFlags.Instance | BindingFlags.NonPublic)!;
                originalDeathTime = (TimeSpan) deathTime.GetValue(bubble)!;
                originalHeight = bubble.ContentSize.Y;
                translated = string.Join(" ", Enumerable.Repeat("привет", 50));
                controller.OnReplaceChatMessage(new MsgReplaceChatMessage { MessageId = 100001, Message = translated });

                labels = Descendants(bubble).OfType<RichTextLabel>().Select(label => label.Text ?? "").ToArray();
                displayedMessage = message.Message;
                translatedHeight = bubble.ContentSize.Y;
                olderOffset = older.VerticalOffset;
                translatedDeathTime = (TimeSpan) deathTime.GetValue(bubble)!;
            }
            finally
            {
                bubbles.Remove(senderUid);
                older.Dispose();
                bubble.Dispose();
                CEntMan.DeleteEntity(senderUid);
            }
        });
        Assert.Multiple(() =>
        {
            Assert.That(labels.Any(text => text.Contains(translated, StringComparison.Ordinal)), Is.True,
                "the existing bubble must render the translated text");
            Assert.That(labels.Any(text => text.Contains("(Dwarvish) [color", StringComparison.Ordinal)), Is.True,
                "the fictional language tag and color must survive");
            Assert.That(displayedMessage, Is.EqualTo(translated));
            Assert.That(translatedHeight, Is.GreaterThan(originalHeight), "translated text must be remeasured");
            Assert.That(olderOffset, Is.EqualTo(translatedHeight), "older bubbles must move with the new height");
            Assert.That(translatedDeathTime, Is.EqualTo(originalDeathTime), "translation must not restart the lifetime");
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task TranslationBeforeBubbleCreationIsUsedByQueuedBubble(bool swapBeforeOriginal)
    {
        var renderedTranslation = false;
        await Client.WaitPost(() =>
        {
            var controller = Client.Resolve<IUserInterfaceManager>().GetUIController<ChatUIController>();
            var senderUid = CEntMan.SpawnEntity(null, Client.Resolve<IEyeManager>().CurrentEye.Position);
            var swap = new MsgReplaceChatMessage { MessageId = 100002, Message = "привет" };
            if (swapBeforeOriginal)
                controller.OnReplaceChatMessage(swap);
            var message = CreateMessage(ChatChannel.Local, senderUid, 100002);
            controller.ProcessChatMessage(message);
            if (!swapBeforeOriginal)
                controller.OnReplaceChatMessage(swap);

            var queued = (IDictionary) typeof(ChatUIController)
                .GetField("_queuedSpeechBubbles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
            var queueData = queued[senderUid]!;
            var queue = queueData.GetType().GetProperty("MessageQueue")!.GetValue(queueData)!;
            var speechData = queue.GetType().GetMethod("Dequeue")!.Invoke(queue, null)!;
            typeof(ChatUIController).GetMethod("CreateSpeechBubble", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(controller, new[] { (object) senderUid, speechData });
            var active = (Dictionary<EntityUid, List<SpeechBubble>>) typeof(ChatUIController)
                .GetField("_activeSpeechBubbles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
            var bubble = active[senderUid].Single();
            try
            {
                renderedTranslation = Descendants(bubble).OfType<RichTextLabel>().Any(label => label.Text!.Contains("привет", StringComparison.Ordinal));
            }
            finally
            {
                controller.RemoveSpeechBubble(senderUid, bubble);
                queued.Remove(senderUid);
            }
            CEntMan.DeleteEntity(senderUid);
        });
        Assert.That(renderedTranslation, Is.True, "the queued bubble must use the translation regardless of delivery order");
    }

    private ChatMessage CreateMessage(ChatChannel channel, EntityUid senderUid, int messageId)
    {
        return new ChatMessage(channel, "hello",
            "[BubbleHeader][Name]Speaker[/Name][/BubbleHeader] says, [BubbleContent](Dwarvish) [color=#AABBCC]hello[/color][/BubbleContent]",
            CEntMan.GetNetEntity(senderUid), null) { MessageId = messageId };
    }

    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (var child in control.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}
