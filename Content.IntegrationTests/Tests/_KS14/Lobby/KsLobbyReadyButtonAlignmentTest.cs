using System.Threading.Tasks;
using Content.Client.Lobby;
using Content.Client.Lobby.UI;
using Content.IntegrationTests.Fixtures;
using Content.Server.GameTicking;
using Robust.Client.State;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Console;

namespace Content.IntegrationTests.Tests._KS14.Lobby;

/// <summary>
///     The ready button's text stays pinned to its left edge through NOT READY, READY and JOIN. Its width can lag a
///         text change (the engine skips re-arranging a button whose label re-measures while the button itself is
///         arranging, which happens at round start), and a centred label then visibly shifts inside the stale width.
/// </summary>
[TestOf(typeof(LobbyGui))]
public sealed class KsLobbyReadyButtonAlignmentTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, InLobby = true, Dirty = true };

    [Test]
    public async Task ReadyButtonTextIsLeftAligned()
    {
        await AssertLeftAligned("not ready");

        // Round start swaps NOT READY for JOIN: the transition that left the width stale.
        await Server.WaitPost(() => Server.System<GameTicker>().StartRound());
        await AssertLeftAligned("join");

        await Server.WaitPost(() => Server.System<GameTicker>().RestartRound());
        await Pair.RunTicksSync(10);
        await Client.WaitPost(() => Client.Resolve<IConsoleHost>().ExecuteCommand("toggleready True"));
        await AssertLeftAligned("ready");
    }

    private async Task AssertLeftAligned(string state)
    {
        await Pair.RunTicksSync(10);
        await Client.WaitAssertion(() =>
        {
            var lobby = ((LobbyState)Client.Resolve<IStateManager>().CurrentState).Lobby!;
            Assert.That(lobby.ReadyButton.Label.Align, Is.EqualTo(Label.AlignMode.Left), state);
        });
    }
}
