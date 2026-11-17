using System.Threading.Tasks;
using Content.Client.Atmos.UI;
using Content.Client.UserInterface.Controls;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     A <c>SwitchButton</c>'s checkbox texture is chosen by a rule on the button's pressed pseudo-class, matched on a child.
///         A child is only restyled when something queues it, so toggling the button has to do that.
/// </summary>
public sealed class KsSwitchButtonRestyleTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [TestCase(true)]
    [TestCase(false)]
    public async Task TogglingAPumpSwitchChangesTheTextureShown(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2); // The theme restyles on the frame after the cvar changes.

        GasPressurePumpWindow window = null;
        await Client.WaitPost(() =>
        {
            window = new GasPressurePumpWindow();
            window.OpenCentered();
        });
        await Pair.RunTicksSync(3);

        // Across frames: the style queue only drains between them.
        Texture offTexture = null;
        Color offColour = default;
        await Client.WaitPost(() =>
        {
            window.FindControl<SwitchButton>("ToggleStatusButton").Pressed = false;
        });
        await Pair.RunTicksSync(3);
        await Client.WaitPost(() =>
        {
            offTexture = window.FindControl<SwitchButton>("ToggleStatusButton").TrackOutline.Texture;
            offColour = window.FindControl<SwitchButton>("ToggleStatusButton").TrackOutline.ActualModulateSelf;
            window.FindControl<SwitchButton>("ToggleStatusButton").Pressed = true;
        });
        await Pair.RunTicksSync(3);

        await Client.WaitAssertion(() =>
        {
            var onTexture = window.FindControl<SwitchButton>("ToggleStatusButton").TrackOutline.Texture;
            var onColour = window.FindControl<SwitchButton>("ToggleStatusButton").TrackOutline.ActualModulateSelf;
            Assert.That(onTexture != offTexture || onColour != offColour, Is.True,
                "The indicator looks the same pressed as unpressed");
            window.Close();
        });
    }
}
