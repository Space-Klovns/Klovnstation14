using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Content.Client._KS14.Chat;
using Content.Client.UserInterface.Systems.Bwoink;
using Content.Client.UserInterface.Systems.Chat;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Translation;
using Content.Shared._KS14.Chat;
using Content.Shared.Administration;
using Content.Shared.Chat;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._KS14.Translation;

public sealed class KsTranslationSessionTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    { Connected = true, InLobby = true, Fresh = true, Destructive = true };

    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [TestCase(false)]
    [TestCase(true)]
    public async Task ReconnectSeparatesReusedIdsAndPrivateAHelp(bool replacementFirst)
    {
        ChatMessage oldVisible = null!, oldFutureBroadcast = null!;
        IAHelpUIHandler oldHelp = null!;
        const string oldOriginal = "old [text] \\\"quote\\\"\nsecond line";
        const string oldTranslated = "старый перевод [kstranslated id=200001]";
        string oldRenderedText = null!;
        await Client.WaitAssertion(() =>
        {
            var ui = Client.Resolve<IUserInterfaceManager>();
            var chat = ui.GetUIController<ChatUIController>();
            var speaker = CEntMan.SpawnEntity(null, MapCoordinates.Nullspace);
            oldVisible = Message(speaker, 200001, oldOriginal);
            oldFutureBroadcast = Message(speaker, 200002, "old visible report");
            chat.ProcessChatMessage(oldVisible, speechBubble: false);
            chat.ProcessChatMessage(oldFutureBroadcast, speechBubble: false);
            chat.OnReplaceChatMessage(new() { MessageId = 200001, Message = oldTranslated });
            chat.OnReplaceChatMessage(new() { MessageId = 200002, Message = "старый отчёт" });
            oldRenderedText = FormattedMessage.RemoveMarkupOrThrow(oldVisible.WrappedMessage);
            chat.ProcessChatMessage(Message(speaker, 200004, "old hidden speech", hidden: true), speechBubble: false);
            chat.OnReplaceChatMessage(new() { MessageId = 200003, Message = "old pending result" });

            var help = ui.GetUIController<AHelpUIController>();
            var owner = ServerSession!.UserId;
            var request = new SharedBwoinkSystem.BwoinkTextMessage(owner, owner, "private previous session", playSound: false);
            typeof(AHelpUIController).GetMethod("ReceivedBwoink", PrivateInstance)!.Invoke(help, [null, request]);
            typeof(AHelpUIController).GetMethod("KsReceiveTranslation", PrivateInstance)!.Invoke(help,
                [new KsBwoinkTranslationEvent(owner, Guid.NewGuid(), "old pending AHelp"), default(EntitySessionEventArgs)]);
            help.Open();
            oldHelp = help.UIHelper!;
            Assert.That(oldHelp.IsOpen, Is.True);
        });

        await Pair.Disconnect();
        await Client.WaitAssertion(() =>
        {
            var ui = Client.Resolve<IUserInterfaceManager>();
            var chat = ui.GetUIController<ChatUIController>();
            var help = ui.GetUIController<AHelpUIController>();
            Assert.Multiple(() =>
            {
                Assert.That(oldVisible.MessageId, Is.Null, "old rows must not match a restarted server's message IDs");
                Assert.That(oldFutureBroadcast.MessageId, Is.Null);
                Assert.That(DictionaryCount(chat, "_pendingTranslations"), Is.Zero);
                Assert.That(DictionaryCount(chat, "_ksHiddenTranslations"), Is.Zero);
                Assert.That(DictionaryCount(help, "_ksPendingTranslations"), Is.Zero);
                Assert.That(help.UIHelper, Is.Null, "a private AHelp handler belongs to one connection");
                Assert.That(oldHelp.IsOpen, Is.False);
            });
        });

        await Pair.Connect();
        await Pair.RunTicksSync(10);
        await Client.WaitAssertion(() =>
        {
            var ui = Client.Resolve<IUserInterfaceManager>();
            var chat = ui.GetUIController<ChatUIController>();
            var speaker = CEntMan.SpawnEntity(null, MapCoordinates.Nullspace);
            var current = Message(speaker, 200001, "current session");
            var broadcast = Message(speaker, 200002, "current broadcast", hidden: true);
            var visibleResult = new MsgReplaceChatMessage { MessageId = 200001, Message = "новый перевод" };
            var hiddenResult = new MsgReplaceChatMessage { MessageId = 200002, Message = "новая трансляция" };
            if (replacementFirst)
            {
                chat.OnReplaceChatMessage(visibleResult);
                chat.OnReplaceChatMessage(hiddenResult);
            }
            chat.ProcessChatMessage(current, speechBubble: false);
            chat.ProcessChatMessage(broadcast, speechBubble: false);
            if (!replacementFirst)
            {
                chat.OnReplaceChatMessage(visibleResult);
                chat.OnReplaceChatMessage(hiddenResult);
            }
            Assert.Multiple(() =>
            {
                Assert.That(current.Message, Is.EqualTo(visibleResult.Message));
                Assert.That(broadcast.Message, Is.EqualTo(hiddenResult.Message));
                Assert.That(oldVisible.Message, Is.EqualTo(oldTranslated));
                Assert.That(oldFutureBroadcast.Message, Is.EqualTo("старый отчёт"));
                Assert.That(FormattedMessage.RemoveMarkupOrThrow(oldVisible.WrappedMessage), Is.EqualTo(oldRenderedText),
                    "freezing the real marker must preserve an escaped marker-like string in the body");
                Assert.That(chat.History.Any(row => ReferenceEquals(row.Msg, broadcast)), Is.False);
            });
            Assert.That(chat.TryGetTranslationOriginal(200001, out var currentOriginal), Is.True);
            Assert.That(currentOriginal, Is.EqualTo("current session"));

            // Retained history must keep its old tooltip after the live ID dictionary is reused.
            var oldMarker = FormattedMessage.FromMarkupOrThrow(oldVisible.WrappedMessage).Nodes
                .First(node => node.Name == KsTranslatedMarkerTag.TagName);
            Assert.That(oldMarker.Attributes.TryGetValue("original", out var frozen), Is.True);
            Assert.That(frozen.TryGetString(out var original), Is.True);
            Assert.That(original, Is.EqualTo(oldOriginal));
            Assert.That(new KsTranslatedMarkerTag().TryCreateControl(oldMarker, out var control), Is.True);
            Assert.That(((Label) control!).ToolTip, Is.EqualTo(oldOriginal));
            control.Dispose();

            var help = ui.GetUIController<AHelpUIController>();
            help.EnsureUIHelper();
            Assert.That(help.UIHelper, Is.Not.SameAs(oldHelp));
            var owner = help.UIHelper!.GetType().GetField("_ownerId", PrivateInstance)!.GetValue(help.UIHelper);
            Assert.That(owner, Is.EqualTo(ServerSession!.UserId));
        });
    }

    private ChatMessage Message(EntityUid speaker, int id, string text, bool hidden = false) =>
        new(ChatChannel.Local, text,
            $"[BubbleHeader]Speaker[/BubbleHeader] [BubbleContent]{FormattedMessage.EscapeText(text)}[/BubbleContent]",
            CEntMan.GetNetEntity(speaker), null, hideChat: hidden) { MessageId = id };

    private static int DictionaryCount(object owner, string field) =>
        ((IDictionary) owner.GetType().GetField(field, PrivateInstance)!.GetValue(owner)!).Count;
}
