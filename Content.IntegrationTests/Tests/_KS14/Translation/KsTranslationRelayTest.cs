using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using Content.Client.Administration.UI.Bwoink;
using Content.Client.Chat.UI;
using Content.Client.UserInterface.Systems.Bwoink;
using Content.Client.UserInterface.Systems.Chat;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.IntegrationTests.Tests._KS14.Translation;
using Content.Server._KS14.Translation;
using Content.Server.Administration.Managers;
using Content.Server.Chat.Systems;
using Content.Server.Power.EntitySystems;
using Content.Server.Telephone;
using Content.Shared._KS14.Translation;
using Content.Shared._KS14.Chat;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Language;
using Content.Shared.Administration;
using Content.Shared.Chat;
using Content.Shared.CCVar;
using Content.Shared.Ghost;
using Content.Shared.Holopad;
using Content.Shared.Telephone;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using ServerBwoink = Content.Server.Administration.Systems.BwoinkSystem;

namespace Content.IntegrationTests.Tests._KS14.Translation;

public sealed class KsTranslationRelayTest : GameTest
{
    // Backend mocks, translation cache and AHelp role/UI state must never survive between scenarios.
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true, Fresh = true, Destructive = true, InLobby = true };

    private static readonly EntProtoId SourceHolopadPrototype = "HolopadBluespace";
    private static readonly EntProtoId HolopadPrototype = "Holopad";
    private static readonly EntProtoId DwarfPrototype = "MobDwarf";
    private static readonly EntProtoId HumanPrototype = "MobHuman";
    private static readonly EntProtoId ObserverPrototype = "MobObserver";

    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly ProtoId<KsLanguagePrototype> Common = "KsLangCommon";
    private static readonly ProtoId<KsLanguagePrototype> Dwarvish = "KsLangDwarvish";

    // A real microphone -> telephone link -> hologram -> network chat -> replacement path.
    [TestCase("Holopad", InGameICChatType.Speak, TelephoneVolume.Speak, false, false, false)]
    [TestCase("Holopad", InGameICChatType.Whisper, TelephoneVolume.Whisper, false, false, false)]
    [TestCase("Holopad", InGameICChatType.Whisper, TelephoneVolume.Speak, true, false, false)]
    [TestCase("Holopad", InGameICChatType.Speak, TelephoneVolume.Speak, false, true, false)]
    [TestCase("Holopad", InGameICChatType.Speak, TelephoneVolume.Speak, false, false, true)]
    public async Task HolopadRespectsTranslationOriginAndPerception(string receiverPrototype,
        InGameICChatType input, TelephoneVolume output, bool muffledMicrophone, bool unknownLanguage, bool broadcast)
    {
        var sourceMap = await Pair.CreateTestMap();
        var receiverMap = await Pair.CreateTestMap();
        var speaker = await Server.AddDummySession();
        var distantObserver = broadcast ? await Server.AddDummySession() : null;
        var fake = new FakeKsTranslator();
        var gate = new TaskCompletionSource();
        fake.Gate = gate.Task;
        await OverrideCVar(Side.Server, KsCCVars.TranslateEnabled, true);
        await OverrideCVar(Side.Client, KsCCVars.TranslateLanguage, "RU");
        EntityUid source = default, receiver = default, user = default, extraReceiver = default;
        NetEntity hologram = default;
        await Server.WaitPost(() =>
        {
            SetDummyLanguage(speaker, "EN");
            Server.System<KsTranslationSystem>().Translator = fake;
            source = SEntMan.SpawnEntity(SourceHolopadPrototype, sourceMap.GridCoords);
            receiver = SEntMan.SpawnEntity(receiverPrototype, receiverMap.GridCoords);
            user = SEntMan.SpawnEntity(DwarfPrototype, sourceMap.GridCoords.Offset(new Vector2(muffledMicrophone ? 2.3f : 0, 0)));
            var listener = SEntMan.SpawnEntity(HumanPrototype, receiverMap.GridCoords);
            Server.PlayerMan.SetAttachedEntity(speaker, user);
            Server.PlayerMan.SetAttachedEntity(ServerSession!, listener);
            if (distantObserver != null)
            {
                SetDummyLanguage(distantObserver, "DE");
                var ghost = SEntMan.SpawnEntity(ObserverPrototype, receiverMap.GridCoords.Offset(new Vector2(100, 0)));
                Assert.That(SEntMan.HasComponent<GhostHearingComponent>(ghost), Is.True);
                Server.PlayerMan.SetAttachedEntity(distantObserver, ghost);
            }
            Server.System<PowerReceiverSystem>().SetNeedsPower(source, false);
            Server.System<PowerReceiverSystem>().SetNeedsPower(receiver, false);
            if (broadcast)
            {
                extraReceiver = SEntMan.SpawnEntity(HolopadPrototype, sourceMap.GridCoords.Offset(new Vector2(4, 0)));
                Server.System<PowerReceiverSystem>().SetNeedsPower(extraReceiver, false);
            }
            // Test the existing YAML-configurable volume without widening the component's write access.
            typeof(TelephoneComponent).GetField(nameof(TelephoneComponent.SpeakerVolume))!
                .SetValue(SEntMan.GetComponent<TelephoneComponent>(receiver), output);
        });
        await Pair.RunTicksSync(10);
        await Server.WaitAssertion(() =>
        {
            var phones = Server.System<TelephoneSystem>();
            Entity<TelephoneComponent> from = (source, SEntMan.GetComponent<TelephoneComponent>(source));
            Entity<TelephoneComponent> to = (receiver, SEntMan.GetComponent<TelephoneComponent>(receiver));
            if (broadcast)
                phones.BroadcastCallToTelephones(from, [to, (extraReceiver, SEntMan.GetComponent<TelephoneComponent>(extraReceiver))],
                    user, new TelephoneCallOptions { ForceConnect = true, MuteReceiver = true });
            else
            {
                phones.CallTelephone(from, to, user);
                phones.AnswerTelephone(to, ServerSession!.AttachedEntity!.Value);
            }
            Assert.That(from.Comp.CurrentState, Is.EqualTo(TelephoneState.InCall));
            hologram = SEntMan.GetNetEntity(SEntMan.GetComponent<HolopadComponent>(receiver).Hologram!.Value);
            var chat = Server.System<ChatSystem>();
            Assert.That(chat.TransformSpeech(user, "I like mining."), Is.Not.EqualTo("I like mining."));
            chat.TrySendInGameICMessage(user, "I like mining.", input, ChatTransmitRange.Normal,
                hideLog: true, checkRadioPrefix: false, ignoreActionBlocker: true,
                ksLanguageOverride: unknownLanguage ? Dwarvish : Common);
        });
        await Pair.RunTicksSync(10);
        await Client.WaitAssertion(() =>
        {
            var controller = Client.Resolve<IUserInterfaceManager>().GetUIController<ChatUIController>();
            var message = FindHolopadMessage(controller, hologram);
            Assert.That(message.MessageId.HasValue, Is.EqualTo(!muffledMicrophone && !unknownLanguage));
            Assert.That(message.Message, Is.Not.EqualTo("[RU] I like mining."), "the original is delivered before the API result");
        });
        gate.SetResult();
        await Pair.RunTicksSync(10);
        await Client.WaitAssertion(() =>
        {
            var controller = Client.Resolve<IUserInterfaceManager>().GetUIController<ChatUIController>();
            var message = FindHolopadMessage(controller, hologram);
            if (muffledMicrophone || unknownLanguage)
            {
                Assert.That(fake.Requests, Is.Empty, "unintelligible speech must never send clear text to DeepL");
                Assert.That(message.MessageId, Is.Null);
                return;
            }
            Assert.That(fake.Requests, Has.Count.EqualTo(1), "a distant observer must not receive a broadcast translation");
            Assert.That(fake.Requests[0].Text, Is.EqualTo("I like mining."), "preserve text before the dwarf accent");
            Assert.That(fake.Requests[0].Source, Is.EqualTo("EN"));
            Assert.That(fake.Requests[0].Target, Is.EqualTo("RU"));
            Assert.That(message.Message, Is.EqualTo("[RU] I like mining."));
            if (broadcast)
            {
                Assert.That(message.HideChat, Is.True);
                Assert.That(controller.History.Any(m => m.Msg.SenderEntity == hologram), Is.False,
                    "broadcast translation must not add a hidden message to chat history");
            }
            else
            {
                Assert.That(message.WrappedMessage, Does.Contain("[kstranslated id="));
                Assert.That(controller.TryGetTranslationOriginal(message.MessageId!.Value, out var original), Is.True);
                Assert.That(original, Is.Not.EqualTo("I like mining."), "hover retains the delivered accented original");
            }
        });
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task HiddenBroadcastTranslationUpdatesBubblesInEitherArrivalOrder(bool swapFirst, bool highlightBody)
    {
        await Client.WaitAssertion(() =>
        {
            var controller = Client.Resolve<IUserInterfaceManager>().GetUIController<ChatUIController>();
            var sender = CEntMan.SpawnEntity(null, Robust.Shared.Map.MapCoordinates.Nullspace);
            var highlights = (List<string>) typeof(ChatUIController).GetField("_highlights", PrivateInstance)!.GetValue(controller)!;
            highlights.Clear();
            if (highlightBody)
                highlights.Add("friend");
            typeof(ChatUIController).GetField("_highlightsColor", PrivateInstance)!.SetValue(controller, "#ff0000");
            var message = new ChatMessage(ChatChannel.Local, "hello friend",
                "[BubbleHeader]Hologram[/BubbleHeader] [BubbleContent][color=#00ff00]hello friend[/color][/BubbleContent]",
                CEntMan.GetNetEntity(sender), null, hideChat: true) { MessageId = 300001 };
            var swap = new MsgReplaceChatMessage { MessageId = 300001, Message = "перевод friend" };
            if (swapFirst)
                controller.OnReplaceChatMessage(swap);
            controller.ProcessChatMessage(message, speechBubble: false);
            var bubble = SpeechBubble.CreateSpeechBubble(SpeechBubble.SpeechType.Say, message, sender);
            var active = (Dictionary<EntityUid, List<SpeechBubble>>) typeof(ChatUIController)
                .GetField("_activeSpeechBubbles", PrivateInstance)!.GetValue(controller)!;
            active.Add(sender, [bubble]);
            try
            {
                if (!swapFirst)
                    controller.OnReplaceChatMessage(swap);
                Assert.That(Descendants(bubble).OfType<RichTextLabel>().Any(label =>
                    FormattedMessage.RemoveMarkupOrThrow(label.Text ?? "").Contains(swap.Message, StringComparison.Ordinal)), Is.True);
                Assert.That(message.WrappedMessage, Does.Contain("[color=#00ff00]"), "Keep the server's body formatting.");
                if (highlightBody)
                    Assert.That(message.WrappedMessage, Does.Contain("[color=#ff0000]friend[/color]"));
                Assert.That(controller.History.Any(row => row.Msg.MessageId == message.MessageId), Is.False);
                var wrapped = message.WrappedMessage;
                controller.OnReplaceChatMessage(swap);
                Assert.That(message.Message, Is.EqualTo(swap.Message));
                Assert.That(message.WrappedMessage, Is.EqualTo(wrapped), "Duplicate results must not decorate twice.");
            }
            finally
            {
                active.Remove(sender);
                bubble.Dispose();
                CEntMan.DeleteEntity(sender);
            }
        });
    }

    [Test]
    public async Task AHelpUsesPrivateContextAndExistingCacheAndDedup()
    {
        var speaker = await Server.AddDummySession();
        var gate = new TaskCompletionSource();
        var fake = new FakeKsTranslator { Gate = gate.Task };
        var received = new List<string>();
        KsTranslationContext context = null!;
        await OverrideCVar(Side.Server, KsCCVars.TranslateEnabled, true);
        await OverrideCVar(Side.Client, KsCCVars.TranslateLanguage, "RU");
        await Pair.RunTicksSync(10);
        await Server.WaitAssertion(() =>
        {
            SetDummyLanguage(speaker, "EN");
            var translation = Server.System<KsTranslationSystem>();
            translation.Translator = fake;
            Assert.That(translation.TryBeginSession(ChatChannel.Local, "public conversation", speaker, out _), Is.True);
            Assert.That(translation.KsTryBeginAHelp("private report", speaker, out var help), Is.True);
            context = help!;
            Assert.That(context.Context, Does.Not.Contain("public conversation"));
            Assert.That(translation.KsTryBeginAHelp("another private report", speaker, out var second), Is.True);
            Assert.That(second!.Context, Does.Not.Contain("private report"));
            Assert.That(translation.TryBeginSession(ChatChannel.AdminChat, "public admin chat", speaker, out var admin), Is.True);
            Assert.That(admin!.Context, Does.Not.Contain("private report"));
            Assert.That(translation.KsTryAHelpReader("private report", context, ServerSession!.Channel, (_, _, text) => received.Add(text)), Is.True);
            Assert.That(translation.KsTryAHelpReader("private report", context, ServerSession.Channel, (_, _, text) => received.Add(text)), Is.True);
            // An identical game-chat line must not reuse the private support-context request.
            translation.TryReader("private report", admin, ServerSession.Channel);
            Assert.That(fake.Requests, Has.Count.EqualTo(2));
            Assert.That(fake.Requests[0].Context, Is.EqualTo(context.Context));
            Assert.That(fake.Requests[1].Context, Is.Not.EqualTo(context.Context));
        });
        gate.SetResult();
        await Pair.RunTicksSync(10);
        await Server.WaitAssertion(() =>
        {
            Assert.That(received, Is.EqualTo(new[] { "[RU] private report", "[RU] private report" }));
            var translation = Server.System<KsTranslationSystem>();
            translation.KsTryAHelpReader("private report", context, ServerSession!.Channel, (_, _, text) => received.Add(text));
        });
        await Pair.RunTicksSync(10);
        await Server.WaitAssertion(() =>
        {
            Assert.That(fake.Requests, Has.Count.EqualTo(2), "repeat AHelp uses its own cache without another API call");
            Assert.That(received, Has.Count.EqualTo(3));
        });
        await OverrideCVar(Side.Client, KsCCVars.TranslateLanguage, "EN-GB");
        await Pair.RunTicksSync(10);
        await Server.WaitAssertion(() => Assert.That(Server.System<KsTranslationSystem>().KsTryAHelpReader(
            "private report", context, ServerSession!.Channel, (_, _, _) => Assert.Fail("same language")), Is.False));
        await OverrideCVar(Side.Client, KsCCVars.TranslateLanguage, "");
        await Pair.RunTicksSync(10);
        await Server.WaitAssertion(() => Assert.That(Server.System<KsTranslationSystem>().KsTryAHelpReader(
            "private report", context, ServerSession!.Channel, (_, _, _) => Assert.Fail("opted out")), Is.False));
        await Server.WaitAssertion(() =>
        {
            SetDummyLanguage(speaker, "");
            Assert.That(Server.System<KsTranslationSystem>().KsTryBeginAHelp("source opted out", speaker, out _), Is.False);
            SetDummyLanguage(speaker, "EN");
        });
        await OverrideCVar(Side.Server, KsCCVars.TranslateEnabled, false);
        await Server.WaitAssertion(() => Assert.That(Server.System<KsTranslationSystem>()
            .KsTryBeginAHelp("server disabled translation", speaker, out _), Is.False));
    }

    [Test]
    public async Task AHelpRechecksAdminOnlyAccessBeforeDeliveringAResult()
    {
        var sender = await Server.AddDummySession();
        var gate = new TaskCompletionSource();
        var fake = new FakeKsTranslator { Gate = gate.Task };
        await OverrideCVar(Side.Server, KsCCVars.TranslateEnabled, true);
        await OverrideCVar(Side.Client, KsCCVars.TranslateLanguage, "RU");
        await Server.WaitPost(() =>
        {
            SetDummyLanguage(sender, "EN");
            Server.System<KsTranslationSystem>().Translator = fake;
            var admins = Server.ResolveDependency<IAdminManager>();
            admins.PromoteHost(sender);
            admins.PromoteHost(ServerSession!);
        });
        await Pair.RunTicksSync(20);
        var thread = ServerSession!.UserId;
        var handler = typeof(ServerBwoink).GetMethod("OnBwoinkTextMessage", PrivateInstance)!;
        await Server.WaitPost(() => handler.Invoke(Server.System<ServerBwoink>(),
        [
            new SharedBwoinkSystem.BwoinkTextMessage(thread, default, "private admin-only report", playSound: false, adminOnly: true),
            new EntitySessionEventArgs(sender)
        ]));
        await Pair.RunTicksSync(10);
        BwoinkPanel panel = null!;
        await Client.WaitAssertion(() =>
        {
            var controller = Client.Resolve<IUserInterfaceManager>().GetUIController<AHelpUIController>();
            panel = GetPanel(controller, thread);
            Assert.That(panel.FindControl<OutputPanel>("TextOutput").GetMessage(^1).ToString(), Does.EndWith("private admin-only report"));
            controller.UIHelper!.Close();
        });
        await Server.WaitPost(() => Server.ResolveDependency<IAdminManager>().DeAdmin(ServerSession!));
        await Pair.RunTicksSync(10);
        gate.SetResult();
        await Pair.RunTicksSync(10);
        await Client.WaitAssertion(() =>
        {
            Assert.That(fake.Requests, Has.Count.EqualTo(1));
            var output = panel.FindControl<OutputPanel>("TextOutput");
            Assert.That(output.GetMessage(^1).ToString(), Does.Not.Contain("[RU]"));
            Assert.That(Descendants(output).OfType<Label>().Any(label => label.Text == "» "), Is.False);
            // Even if an unauthorized result arrived without a visible panel, it would be buffered here.
            var controller = Client.Resolve<IUserInterfaceManager>().GetUIController<AHelpUIController>();
            var pending = (System.Collections.IDictionary) typeof(AHelpUIController)
                .GetField("_ksPendingTranslations", PrivateInstance)!.GetValue(controller)!;
            Assert.That(pending.Count, Is.Zero);
        });
        // A normal player cannot start private admin-only or another player's AHelp translation.
        await Server.WaitPost(() =>
        {
            handler.Invoke(Server.System<ServerBwoink>(),
            [
                new SharedBwoinkSystem.BwoinkTextMessage(thread, sender.UserId, "forged admin-only message", adminOnly: true),
                new EntitySessionEventArgs(ServerSession!)
            ]);
            handler.Invoke(Server.System<ServerBwoink>(),
            [
                new SharedBwoinkSystem.BwoinkTextMessage(sender.UserId, sender.UserId, "forged foreign conversation"),
                new EntitySessionEventArgs(ServerSession!)
            ]);
        });
        await Pair.RunTicksSync(10);
        Assert.That(fake.Requests, Has.Count.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AHelpTranslatesBothDirectionsWithoutAnotherBwoink(bool playerToAdmin)
    {
        var sender = await Server.AddDummySession();
        var fake = new FakeKsTranslator();
        var gate = new TaskCompletionSource();
        fake.Gate = gate.Task;
        await OverrideCVar(Side.Server, KsCCVars.TranslateEnabled, true);
        await OverrideCVar(Side.Client, KsCCVars.TranslateLanguage, "RU");
        if (!playerToAdmin)
            await OverrideCVar(Side.Server, CCVars.AdminAhelpOverrideClientName, "Support");
        await Server.WaitPost(() =>
        {
            SetDummyLanguage(sender, "EN");
            Server.System<KsTranslationSystem>().Translator = fake;
            var admins = Server.ResolveDependency<IAdminManager>();
            admins.PromoteHost(ServerSession!);
            admins.PromoteHost(sender);
        });
        await Pair.RunTicksSync(20);
        await Server.WaitAssertion(() =>
        {
            var admins = Server.ResolveDependency<IAdminManager>();
            Assert.That(admins.ActiveAdmins, Does.Contain(ServerSession));
            Assert.That(admins.ActiveAdmins, Does.Contain(sender));
            admins.DeAdmin(playerToAdmin ? sender : ServerSession!);
        });
        await Pair.RunTicksSync(10);
        var thread = playerToAdmin ? sender.UserId : ServerSession!.UserId;
        var text = "Please help [with this] \\\"problem\\\"";
        await Server.WaitPost(() =>
        {
            var message = new SharedBwoinkSystem.BwoinkTextMessage(thread, default, text, playSound: false);
            // Drive the authenticated receive handler, including its normal permissions and recipient selection.
            typeof(ServerBwoink).GetMethod("OnBwoinkTextMessage", PrivateInstance)!.Invoke(Server.System<ServerBwoink>(),
                [message, new EntitySessionEventArgs(sender)]);
            Assert.That(fake.Requests, Has.Count.EqualTo(1), "the authenticated sender and reader have different translation languages");
        });
        await Pair.RunTicksSync(10);
        BwoinkPanel panel = null!;
        DateTime timestamp = default;
        int unread = 0;
        await Client.WaitAssertion(() =>
        {
            var controller = Client.Resolve<IUserInterfaceManager>().GetUIController<AHelpUIController>();
            panel = GetPanel(controller, thread);
            var output = panel.FindControl<OutputPanel>("TextOutput");
            Assert.That(output.GetMessage(^1).ToString(), Does.EndWith(text));
            timestamp = panel.LastMessage;
            unread = panel.Unread;
            controller.UIHelper!.Close();
        });
        gate.SetResult();
        await Pair.RunTicksSync(15);
        await Client.WaitAssertion(() =>
        {
            var controller = Client.Resolve<IUserInterfaceManager>().GetUIController<AHelpUIController>();
            var output = panel.FindControl<OutputPanel>("TextOutput");
            Assert.That(output.GetMessage(^1).ToString(), Does.EndWith("[RU] " + text));
            if (!playerToAdmin)
                Assert.That(output.GetMessage(^1).ToString(), Does.Contain("Support: "), "keep the configured administrator name override");
            var marker = Descendants(output).OfType<Label>().Single(label => label.Text == "» ");
            Assert.That(marker.ToolTip, Is.EqualTo(text));
            Assert.That(panel.LastMessage, Is.EqualTo(timestamp));
            Assert.That(panel.Unread, Is.EqualTo(unread));
            Assert.That(controller.UIHelper!.IsOpen, Is.False, "an asynchronous replacement must not reopen AHelp");
            Assert.That(fake.Requests.Single().Text, Is.EqualTo(text), "only the body goes to DeepL");
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AHelpReplacementSupportsEitherArrivalOrder(bool swapFirst)
    {
        await Client.WaitAssertion(() =>
        {
            var controller = Client.Resolve<IUserInterfaceManager>().GetUIController<AHelpUIController>();
            var owner = ServerSession!.UserId;
            var id = Guid.NewGuid();
            const string original = "original [bold]text \\\"quote\\\"";
            var message = new SharedBwoinkSystem.BwoinkTextMessage(owner, owner,
                "[color=red]Administrator[/color]: " + FormattedMessage.EscapeText(original), playSound: false)
            { KsTranslationId = id, KsTranslationText = original };
            var replacement = new KsBwoinkTranslationEvent(owner, id, "перевод [bold] \\\"текст\\\"");
            var receiveSwap = typeof(AHelpUIController).GetMethod("KsReceiveTranslation", PrivateInstance)!;
            if (swapFirst)
                receiveSwap.Invoke(controller, [replacement, default(EntitySessionEventArgs)]);
            typeof(AHelpUIController).GetMethod("ReceivedBwoink", PrivateInstance)!.Invoke(controller, [null, message]);
            if (!swapFirst)
                receiveSwap.Invoke(controller, [replacement, default(EntitySessionEventArgs)]);
            var panel = GetPanel(controller, owner);
            var output = panel.FindControl<OutputPanel>("TextOutput");
            Assert.That(output.GetMessage(^1).ToString(), Does.EndWith("Administrator: " + replacement.Text));
            var marker = Descendants(output).OfType<Label>().Single(label => label.Text == "» ");
            Assert.That(marker.ToolTip, Is.EqualTo(original));
            var unread = panel.Unread;
            receiveSwap.Invoke(controller, [replacement, default(EntitySessionEventArgs)]);
            Assert.That(panel.Unread, Is.EqualTo(unread));
            Assert.That(Descendants(output).OfType<Label>().Count(label => label.Text == "» "), Is.EqualTo(1));
        });
    }

    private void SetDummyLanguage(ICommonSession session, string language)
    {
        // Dummy clients have no CVar replication. Populate only their server-side replicated language.
        var config = Server.ResolveDependency<INetConfigurationManager>();
        var clients = (Dictionary<INetChannel, Dictionary<string, object>>) config.GetType()
            .GetField("_replicatedCVars", PrivateInstance)!.GetValue(config)!;
        clients[session.Channel] = new() { [KsCCVars.TranslateLanguage.Name] = language };
    }

    private static ChatMessage FindHolopadMessage(ChatUIController controller, NetEntity hologram)
    {
        var hidden = (Dictionary<int, (ChatMessage Message, string WrappedTemplate, bool Translated)>) typeof(ChatUIController)
            .GetField("_ksHiddenTranslations", PrivateInstance)!.GetValue(controller)!;
        return controller.History.Select(row => row.Msg).Concat(hidden.Values.Select(entry => entry.Message))
            .Last(message => message.SenderEntity == hologram);
    }

    private static BwoinkPanel GetPanel(AHelpUIController controller, NetUserId thread)
    {
        if (controller.UIHelper is AdminAHelpUIHandler admin)
        {
            Assert.That(admin.TryGetChannel(thread, out var panel), Is.True);
            return panel!;
        }
        return (BwoinkPanel) typeof(UserAHelpUIHandler).GetField("_chatPanel", PrivateInstance)!.GetValue(controller.UIHelper)!;
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
