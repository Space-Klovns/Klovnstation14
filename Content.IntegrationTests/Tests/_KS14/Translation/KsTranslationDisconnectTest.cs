using Content.Client.UserInterface.Systems.Bwoink;
using Content.Client.UserInterface.Systems.Chat;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Administration;
using Content.Shared.Chat;
using NUnit.Framework.Interfaces;
using Robust.Client.UserInterface;

namespace Content.IntegrationTests.Tests._KS14.Translation;

public sealed class KsTranslationDisconnectTest : GameTest
{
    public override PoolSettings PoolSettings => new()
        { Connected = true, InLobby = true, Fresh = true, Destructive = true };

    [Test]
    public async Task DisconnectDetachesRetainedChatAndClosesPrivateAHelp()
    {
        ChatMessage retainedMessage = null;
        IAHelpUIHandler previousAHelp = null;
        await Client.WaitAssertion(() =>
        {
            var userInterfaceManager = Client.Resolve<IUserInterfaceManager>();
            var chatController = userInterfaceManager.GetUIController<ChatUIController>();
            retainedMessage = new ChatMessage(ChatChannel.Local, "previous connection", "previous connection",
                default, null) { MessageId = 200001 };
            chatController.ProcessChatMessage(retainedMessage, speechBubble: false);
            var aHelpController = userInterfaceManager.GetUIController<AHelpUIController>();
            aHelpController.EnsureUIHelper();
            aHelpController.Open();
            previousAHelp = aHelpController.UIHelper;
            Assert.That(previousAHelp.IsOpen, Is.True);
        });

        await Pair.Disconnect();
        await Client.WaitAssertion(() =>
        {
            var aHelpController = Client.Resolve<IUserInterfaceManager>().GetUIController<AHelpUIController>();
            Assert.Multiple(() =>
            {
                Assert.That(retainedMessage.MessageId, Is.Null, "Retained rows must not match another connection's IDs.");
                Assert.That(aHelpController.UIHelper, Is.Null, "Private AHelp belongs to one connection.");
                Assert.That(previousAHelp.IsOpen, Is.False);
            });
        });
    }

    public override async Task DoTeardown()
    {
        var result = TestContext.CurrentContext.Result;
        var failure = result.Outcome.Status == TestStatus.Failed ? result.Message : null;
        await base.DoTeardown();
        if (failure != null)
            Assert.Fail(failure);
    }
}
