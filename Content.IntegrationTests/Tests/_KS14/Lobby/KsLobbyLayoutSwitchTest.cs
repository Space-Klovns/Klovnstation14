using System.Linq;
using System.Threading.Tasks;
using Content.Client.Lobby;
using Content.Client.Lobby.UI;
using Content.Client._KS14.Lobby.Kharkiv;
using Content.Client.UserInterface.Systems.Chat.Widgets;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Lobby;
using Robust.Client.State;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Console;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests._KS14.Lobby;

/// <summary>
///     An admin's <c>lobbylayout</c> switches every client's lobby live, both ways. The controls both layouts show are the
///         same instances moved between them, so what LobbyState and the controllers hold stays good, and each comes back to
///         its exact place in the tactical layout. Nothing headless draws, so the looks are unchecked.
/// </summary>
[TestOf(typeof(LobbyGui))]
public sealed class KsLobbyLayoutSwitchTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, InLobby = true, Dirty = true };

    [Test]
    public async Task AdminCommandSwitchesTheLobbyLiveAndBack()
    {
        // The default, auto, follows the calendar: start from a forced tactical so the test doesn't depend on the date.
        await OverrideCVar(Side.Server, KsCCVars.LobbyLayout, KsLobbyLayouts.Tactical);
        await Pair.RunTicksSync(5);

        LobbyGui lobby = default!;
        Control tacticalParent = default!;
        var tacticalIndex = 0;
        var manifestIndex = 0;
        var leaveIndex = 0;
        var countdownIndex = 0;
        ChatBox chat = default!;

        await Client.WaitAssertion(() =>
        {
            lobby = ((LobbyState) Client.Resolve<IStateManager>().CurrentState).Lobby!;
            Assert.That(lobby.KsLayout, Is.EqualTo(KsLobbyLayout.Tactical), "the forced layout reached the client");
            Assert.That(lobby.KsKharkiv.Visible, Is.False);
            tacticalParent = lobby.ReadyButton.Parent!;
            tacticalIndex = lobby.ReadyButton.GetPositionInParent();
            manifestIndex = lobby.ManifestButton.GetPositionInParent();
            leaveIndex = lobby.LeaveButton.GetPositionInParent();
            countdownIndex = lobby.StartTimeValue.GetPositionInParent();
            chat = lobby.Chat;
        });

        await RunServerCommand("lobbylayout " + KsLobbyLayouts.Kharkiv);

        await Client.WaitAssertion(() =>
        {
            var localization = Client.Resolve<ILocalizationManager>();
            Assert.That(lobby.KsLayout, Is.EqualTo(KsLobbyLayout.Kharkiv), "the client followed the server");
            Assert.That(lobby.KsKharkiv.VisibleInTree, "the Kharkiv layout shows");
            Assert.That(lobby.RightSide.VisibleInTree, Is.False, "the tactical layout is hidden");
            Assert.That(lobby.ReadyButton.Parent, Is.SameAs(lobby.KsKharkiv.ReadySlot.Parent), "the ready button moved");
            Assert.That(lobby.ReadyButton.GetPositionInParent(), Is.EqualTo(lobby.KsKharkiv.ReadySlot.GetPositionInParent() + 1), "right after its marker");
            Assert.That(lobby.VoteContainer.Parent, Is.SameAs(lobby.KsKharkiv.VoteSlot.Parent), "vote popups moved");
            Assert.That(lobby.VoteContainer.GetPositionInParent(), Is.EqualTo(lobby.KsKharkiv.VoteSlot.GetPositionInParent() + 1), "right after its marker");
            Assert.That(lobby.CharacterSetupState.Parent, Is.SameAs(lobby.KsKharkiv.SetupSlot.Parent), "character setup moved");
            Assert.That(lobby.CharacterSetupState.GetPositionInParent(), Is.EqualTo(lobby.KsKharkiv.SetupSlot.GetPositionInParent() + 1), "right after its marker");
            AssertRightAfter(lobby.ManifestButton, lobby.KsKharkiv.ManifestSlot, "manifest");
            AssertRightAfter(lobby.LeaveButton, lobby.KsKharkiv.LeaveSlot, "leave");
            AssertRightAfter(lobby.StartTimeValue, lobby.KsKharkiv.TimerSlot, "countdown");
            Assert.That(lobby.StartTimeValue.Visible, "the banner's timer always shows");
            Assert.That(lobby.StartTimeValue.Text, Is.Not.Empty, "the countdown kept running across the switch");
            Assert.That(lobby.ReadyButton.Text, Is.EqualTo(localization.GetString("ks-kharkiv-menu-escape")));
            Assert.That(lobby.ObserveButton.Text, Is.EqualTo(localization.GetString("ks-kharkiv-menu-trading")));
            Assert.That(lobby.ReadyButton.StyleClasses, Does.Contain(KsKharkivStyleClasses.MenuTitle), "with the Kharkiv style");
            Assert.That(lobby.ReadyButton.StyleClasses, Does.Contain(ContainerButton.StyleClassButton),
                "keeping its own class, which every button rule selects on");
            Assert.That(CountChatBoxes(lobby), Is.EqualTo(1), "still exactly one chat box on the screen");
            Assert.That(lobby.Chat, Is.SameAs(chat), "the chat was not rebuilt");
        });

        await RunServerCommand("lobbylayout " + KsLobbyLayouts.Tactical);

        await Client.WaitAssertion(() =>
        {
            var localization = Client.Resolve<ILocalizationManager>();
            Assert.That(lobby.KsLayout, Is.EqualTo(KsLobbyLayout.Tactical));
            Assert.That(lobby.KsKharkiv.Visible, Is.False);
            Assert.That(lobby.RightSide.VisibleInTree, "the tactical layout shows again");
            Assert.That(lobby.ReadyButton.Parent, Is.SameAs(tacticalParent), "the ready button came back");
            Assert.That(lobby.ReadyButton.GetPositionInParent(), Is.EqualTo(tacticalIndex), "to its own place");
            Assert.That(lobby.ManifestButton.Parent, Is.SameAs(tacticalParent));
            Assert.That(lobby.ManifestButton.GetPositionInParent(), Is.EqualTo(manifestIndex), "manifest back in place");
            Assert.That(lobby.LeaveButton.GetPositionInParent(), Is.EqualTo(leaveIndex), "leave back in place");
            Assert.That(lobby.StartTimeValue.Parent, Is.SameAs(tacticalParent));
            Assert.That(lobby.StartTimeValue.GetPositionInParent(), Is.EqualTo(countdownIndex), "countdown back in place");
            Assert.That(lobby.StartTimeValue.StyleClasses, Does.Contain("KsLobbyCountdown"));
            Assert.That(lobby.StartTimeValue.Text, Is.Not.Empty, "the countdown kept running across the switch back");
            Assert.That(lobby.ReadyButton.Text, Is.EqualTo(localization.GetString("ks-lobby-ready-not-ready")),
                "the tactical ready button says the status again");
            Assert.That(lobby.ObserveButton.Text, Is.EqualTo(localization.GetString("ks-lobby-menu-observe")));
            Assert.That(lobby.ReadyButton.StyleClasses, Does.Contain("KsLobbyMenuButton"), "with its own style");
            Assert.That(lobby.ReadyButton.StyleClasses, Does.Not.Contain(KsKharkivStyleClasses.MenuTitle));
            Assert.That(lobby.ReadyButton.StyleClasses, Does.Contain(ContainerButton.StyleClassButton));
        });
    }

    /// <summary>
    ///     A second request for character setup while it is open is ignored: it would reload the editor from the saved
    ///         profile (throwing away unsaved edits) and un-ready the player. The Kharkiv layout's CHARACTER tab stays on
    ///         screen during setup, so it can be pressed then.
    /// </summary>
    [Test]
    public async Task CharacterRequestIsIgnoredWhileSetupIsOpen()
    {
        await OverrideCVar(Side.Server, KsCCVars.LobbyLayout, KsLobbyLayouts.Kharkiv);
        await Pair.RunTicksSync(5);

        LobbyGui lobby = default!;
        var requests = 0;
        await Client.WaitPost(() =>
        {
            lobby = ((LobbyState) Client.Resolve<IStateManager>().CurrentState).Lobby!;
            lobby.KsCharacterSetupRequested += () => requests++;
            lobby.KsRequestCharacterSetup();
        });
        await Pair.RunTicksSync(5);

        await Client.WaitAssertion(() =>
        {
            Assert.That(requests, Is.EqualTo(1), "the first request went through");
            Assert.That(lobby.CharacterSetupState.Visible, "and opened setup");
            lobby.KsRequestCharacterSetup();
            Assert.That(requests, Is.EqualTo(1), "the second, with setup open, did not");
        });

        await Client.WaitPost(() => lobby.SwitchState(LobbyGui.LobbyGuiState.Default));
    }

    /// <summary>
    ///     Character setup in the tactical layout collapses the chat column only if setup needs the room, judged from the
    ///         column beside it. A lobby that opened in the Kharkiv layout never laid that column out, so entering setup
    ///         there and then switching to tactical must judge again, and agree with a lobby that was tactical all along.
    /// </summary>
    // A narrow chat column leaves setup room, so it stays (a stale judgement hid it); a wide one doesn't, so it collapses
    //      (judging only once, before the tactical layout had a width, would leave it showing).
    [Test]
    [TestCase(160f, true)]
    [TestCase(900f, false)]
    public async Task SwitchDuringSetupRestoresTheRightSide(float rightSideWidth, bool expectVisible)
    {
        await OverrideCVar(Side.Server, KsCCVars.LobbyLayout, KsLobbyLayouts.Kharkiv);
        await Pair.RunTicksSync(5);

        var tacticalAllAlong = await RightSideAfterSetup(switchToTacticalInSetup: false, rightSideWidth);
        var switchedInSetup = await RightSideAfterSetup(switchToTacticalInSetup: true, rightSideWidth);

        Assert.That(tacticalAllAlong, Is.EqualTo(expectVisible), "control: tactical setup all along");
        Assert.That(switchedInSetup, Is.EqualTo(tacticalAllAlong),
            "switched to tactical during setup, the chat column is judged as if tactical all along");
    }

    private async Task<bool> RightSideAfterSetup(bool switchToTacticalInSetup, float rightSideWidth)
    {
        LobbyGui lobby = default!;
        await Client.WaitPost(() =>
        {
            lobby = new LobbyGui();
            LayoutContainer.SetAnchorPreset(lobby, LayoutContainer.LayoutPreset.Wide);
            Client.Resolve<IUserInterfaceManager>().WindowRoot.AddChild(lobby);
            lobby.RightSide.SetWidth = rightSideWidth;
            if (!switchToTacticalInSetup)
                lobby.KsSetLayout(KsLobbyLayout.Tactical);
        });
        await Pair.RunTicksSync(5);

        await Client.WaitPost(() => lobby.SwitchState(LobbyGui.LobbyGuiState.CharacterSetup));
        await Pair.RunTicksSync(5);

        if (switchToTacticalInSetup)
        {
            await Client.WaitPost(() => lobby.KsSetLayout(KsLobbyLayout.Tactical));
            await Pair.RunTicksSync(5);
        }

        var visible = false;
        await Client.WaitPost(() =>
        {
            Assert.That(lobby.KsLayout, Is.EqualTo(KsLobbyLayout.Tactical));
            visible = lobby.RightSide.Visible;
            lobby.Orphan();
            lobby.Dispose();
        });

        return visible;
    }

    /// <summary>
    ///     A lobby out of the tree no longer follows the layout cvar: its handler is gone, so a dropped lobby is neither
    ///         kept alive by the config manager nor switched.
    /// </summary>
    [Test]
    public async Task LayoutIsNotFollowedOutOfTheTree()
    {
        await OverrideCVar(Side.Server, KsCCVars.LobbyLayout, KsLobbyLayouts.Tactical);
        await Pair.RunTicksSync(5);

        LobbyGui lobby = default!;
        await Client.WaitPost(() =>
        {
            lobby = new LobbyGui();
            Client.Resolve<IUserInterfaceManager>().WindowRoot.AddChild(lobby);
        });

        await OverrideCVar(Side.Server, KsCCVars.LobbyLayout, KsLobbyLayouts.Kharkiv);
        await Pair.RunTicksSync(5);
        await Client.WaitAssertion(() =>
        {
            Assert.That(lobby.KsLayout, Is.EqualTo(KsLobbyLayout.Kharkiv), "in the tree, it follows");
            lobby.Orphan();
        });

        await OverrideCVar(Side.Server, KsCCVars.LobbyLayout, KsLobbyLayouts.Tactical);
        await Pair.RunTicksSync(5);
        await Client.WaitAssertion(() =>
        {
            Assert.That(lobby.KsLayout, Is.EqualTo(KsLobbyLayout.Kharkiv), "out of the tree, it doesn't");
            lobby.Dispose();
        });
    }

    private static void AssertRightAfter(Control control, Control marker, string what)
    {
        Assert.That(control.Parent, Is.SameAs(marker.Parent), $"{what} moved");
        Assert.That(control.GetPositionInParent(), Is.EqualTo(marker.GetPositionInParent() + 1), $"{what} right after its marker");
    }

    private async Task RunServerCommand(string command)
    {
        await Server.WaitPost(() => Server.ResolveDependency<IConsoleHost>().ExecuteCommand(command));
        await Pair.RunTicksSync(5);
    }

    private static int CountChatBoxes(Control root)
    {
        var count = root is ChatBox ? 1 : 0;
        return count + root.Children.Sum(CountChatBoxes);
    }
}
