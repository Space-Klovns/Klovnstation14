using System.Threading.Tasks;
using Content.Client.Lobby;
using Content.Client.Lobby.UI;
using Content.IntegrationTests.Fixtures;
using Content.Server.GameTicking;
using Robust.Client.State;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Console;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests._KS14.Lobby;

/// <summary>
///     The ready button's text starts at the same place through NOT READY, JOIN and READY. The button's width can lag
///         a text change (the engine skips re-arranging a button whose label re-measures while the button itself is
///         arranging, which happens at round start), so a centred label shows its new text off-centre inside the old
///         width. This checks where the text is drawn, not only the alignment setting.
/// </summary>
[TestOf(typeof(LobbyGui))]
public sealed class KsLobbyReadyButtonAlignmentTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, InLobby = true, Dirty = true };

    [Test]
    public async Task ReadyButtonTextStaysPut()
    {
        var notReadyX = await GetTextLeftEdge("ks-lobby-ready-not-ready");

        // Round start swaps NOT READY for JOIN: the transition that left the width stale.
        await Server.WaitPost(() => Server.System<GameTicker>().StartRound());
        var joinX = await GetTextLeftEdge("ks-lobby-ready-join");

        await Server.WaitPost(() => Server.System<GameTicker>().RestartRound());
        await Pair.RunTicksSync(10);
        await Client.WaitPost(() => Client.Resolve<IConsoleHost>().ExecuteCommand("toggleready True"));
        var readyX = await GetTextLeftEdge("ks-lobby-ready-ready");

        Assert.Multiple(() =>
        {
            Assert.That(joinX, Is.EqualTo(notReadyX).Within(1f), "JOIN starts where NOT READY did");
            Assert.That(readyX, Is.EqualTo(notReadyX).Within(1f), "READY starts where NOT READY did");
        });
    }

    /// <summary>
    ///     Where the label draws its text: the label's left edge, plus the offset its resolved alignment gives the
    ///         text (as wide as the label's <c>DesiredSize</c>) inside the width it was actually arranged at.
    /// </summary>
    private async Task<float> GetTextLeftEdge(string expectedTextLocId)
    {
        await Pair.RunTicksSync(10);

        var textLeftEdge = 0f;
        await Client.WaitAssertion(() =>
        {
            var lobby = ((LobbyState)Client.Resolve<IStateManager>().CurrentState).Lobby!;
            var label = lobby.ReadyButton.Label;
            Assert.That(label.Text, Is.EqualTo(Client.Resolve<ILocalizationManager>().GetString(expectedTextLocId)),
                "the lobby reached this state");

            var slack = label.Size.X - label.DesiredSize.X;
            var alignOffset = label.Align switch
            {
                Label.AlignMode.Center => slack / 2f,
                Label.AlignMode.Right => slack,
                _ => 0f,
            };
            textLeftEdge = label.GlobalPosition.X + alignOffset;
        });

        return textLeftEdge;
    }
}
