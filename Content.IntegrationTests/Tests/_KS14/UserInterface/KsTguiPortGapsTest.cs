using System.Threading.Tasks;
using Content.Client._KS14.Tgui.Windows.Pda;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     What the stock windows did that their tgui ports had dropped, now that Baron opens the ports too.
/// </summary>
[TestOf(typeof(KsTguiPdaWindow))]
public sealed class KsTguiPortGapsTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    /// <summary>
    ///     A PDA's own colours (<c>PdaBorderColorComponent</c>) painted the stock frame; under Baron the port shows them as a
    ///         strip. tgui's PDA stays as tg's, without one.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task PdaShowsItsColoursUnderBaron(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);

        await Client.WaitAssertion(() =>
        {
            var window = new KsTguiPdaWindow();
            Assert.That(window.ColorStrip.Visible, Is.False, "a PDA with no colours has no strip");

            window.BorderColor = "#3F3F74";
            window.AccentHColor = "#FFD700";
            Assert.That(window.ColorStrip.Visible, Is.EqualTo(!tgui));

            window.BorderColor = null;
            window.AccentHColor = null;
            Assert.That(window.ColorStrip.Visible, Is.False);
        });
    }
}
