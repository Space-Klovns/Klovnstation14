using System.Numerics;
using System.Threading.Tasks;
using Content.Client.Lobby.UI;
using Content.IntegrationTests.Fixtures;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.Lobby;

/// <summary>
///     The name, job and summary lines under the lobby character sprite take up room and are not collapsed to nothing.
/// </summary>
[TestOf(typeof(LobbyCharacterPreviewPanel))]
public sealed class KsLobbyCharacterPreviewPanelTests : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [Test]
    public async Task InfoLinesHaveSize()
    {
        await Client.WaitPost(() =>
        {
            var panel = new LobbyCharacterPreviewPanel();
            Client.Resolve<IUserInterfaceManager>().WindowRoot.AddChild(panel);
            panel.SetLoaded(true);
            panel.SetCharacterInfo("Test Name", "Test Job");
            panel.SetSummaryText("A short summary of the character.");
            panel.Measure(Vector2Helpers.Infinity);

            try
            {
                Assert.Multiple(() =>
                {
                    AssertSized(panel.CharacterName);
                    AssertSized(panel.CharacterJob);
                    AssertSized(panel.Summary);
                    Assert.That(panel.ProfilePreviewSpriteView.DesiredSize.Y, Is.LessThanOrEqualTo(200f));
                });
            }
            finally
            {
                panel.Orphan();
            }
        });
    }

    private static void AssertSized(Control control)
    {
        control.Measure(Vector2Helpers.Infinity);
        Assert.That(control.Visible, Is.True, $"{control.Name} is hidden");
        Assert.That(control.DesiredSize.X, Is.GreaterThan(10f), $"{control.Name} is measured with no width");
        Assert.That(control.DesiredSize.Y, Is.GreaterThan(5f), $"{control.Name} is measured with no height");
    }
}
