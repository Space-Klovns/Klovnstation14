using System.Threading.Tasks;
using Content.Client._KS14.Tgui.Windows.Pda;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     What the stock windows did that their tgui ports had dropped, now that Baron opens the ports too (#480).
/// </summary>
[TestOf(typeof(KsTguiPdaWindow))]
public sealed class KsTguiPortGapsTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    /// <summary>
    ///     A PDA's own colours (<c>PdaBorderColorComponent</c>) painted the stock frame; the port shows them as a strip.
    /// </summary>
    [Test]
    public async Task PdaShowsItsColours()
    {
        await Client.WaitAssertion(() =>
        {
            var window = new KsTguiPdaWindow();
            Assert.That(window.ColorStrip.Visible, Is.False, "a PDA with no colours has no strip");

            window.BorderColor = "#3F3F74";
            window.AccentHColor = "#FFD700";
            Assert.That(window.ColorStrip.Visible, Is.True);

            window.BorderColor = null;
            window.AccentHColor = null;
            Assert.That(window.ColorStrip.Visible, Is.False);
        });
    }
}
