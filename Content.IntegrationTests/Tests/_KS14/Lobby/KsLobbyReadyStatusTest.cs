using System.Threading.Tasks;
using Content.Client.Lobby;
using Content.Client.Lobby.UI;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.GameTicking;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Lobby;
using Robust.Client.State;
using Robust.Shared.Console;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests._KS14.Lobby;

/// <summary>
///     In the Kharkiv layout, the ready item keeps its title, ESCAPE FROM KLOVN, and the line under it follows the player through
///         NOT READY, JOIN and READY, staying centred under the title each time. A label whose text changes can be left
///         at its old width when the change lands while its parent is arranging (it happened at round start to the old
///         ready button), so this checks where the text is, not only what it says.
/// </summary>
[TestOf(typeof(LobbyGui))]
public sealed class KsLobbyReadyStatusTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, InLobby = true, Dirty = true };

    [Test]
    public async Task StatusFollowsTheRoundAndStaysCentred()
    {
        await OverrideCVar(Side.Server, KsCCVars.LobbyLayout, KsLobbyLayouts.Kharkiv);
        await Pair.RunTicksSync(5);

        await AssertState("ks-lobby-ready-not-ready");

        // Round start swaps NOT READY for JOIN.
        await Server.WaitPost(() => Server.System<GameTicker>().StartRound());
        await AssertState("ks-lobby-ready-join");

        await Server.WaitPost(() => Server.System<GameTicker>().RestartRound());
        await Pair.RunTicksSync(10);
        await Client.WaitPost(() => Client.Resolve<IConsoleHost>().ExecuteCommand("toggleready True"));
        await AssertState("ks-lobby-ready-ready");
    }

    private async Task AssertState(string expectedStatusLocId)
    {
        await Pair.RunTicksSync(10);

        await Client.WaitAssertion(() =>
        {
            var localization = Client.Resolve<ILocalizationManager>();
            var lobby = ((LobbyState)Client.Resolve<IStateManager>().CurrentState).Lobby!;
            Assert.That(lobby.KsLayout, Is.EqualTo(KsLobbyLayout.Kharkiv), "the server's layout reached the client");
            var status = lobby.KsKharkiv.ReadyStatus;
            var title = lobby.ReadyButton;

            Assert.That(status.Text, Is.EqualTo(localization.GetString(expectedStatusLocId)), "the lobby reached this state");
            Assert.That(title.Text, Is.EqualTo(localization.GetString("ks-kharkiv-menu-escape")), "the title keeps its name");

            // Where the text is drawn: the label is centred in its parent, so its box must have the text's own width
            //      and sit centred under the title.
            Assert.That(status.Size.X, Is.EqualTo(status.DesiredSize.X).Within(1f), "the label's width follows its text");
            var statusCentre = status.GlobalPosition.X + status.Size.X / 2f;
            var titleCentre = title.GlobalPosition.X + title.Size.X / 2f;
            Assert.That(statusCentre, Is.EqualTo(titleCentre).Within(1f), "the status is centred under the title");
        });
    }
}
