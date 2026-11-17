using System.Numerics;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     tgui's transitions, which <see cref="KsTguiFeelUIController"/> plays through overrides it has to give back.
/// </summary>
[TestOf(typeof(KsTguiFeelUIController))]
public sealed class KsTguiFeelTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    /// <summary>
    ///     The theme restyles on the frame after the cvar changes.
    /// </summary>
    private async Task SetTheme(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);
    }

    private IUserInterfaceManager UiManager => Client.ResolveDependency<IUserInterfaceManager>();

    private KsTguiFeelUIController Feel => UiManager.GetUIController<KsTguiFeelUIController>();

    private async Task<Button> MakeButton(bool toggled = false)
    {
        Button button = null;
        await Client.WaitPost(() =>
        {
            button = new Button { Text = "x", ToggleMode = toggled, Pressed = toggled };
            UiManager.PopupRoot.AddChild(button);
            button.ForceRunStyleUpdate();
        });

        return button;
    }

    /// <summary>
    ///     Hovers a button as far as the sheet and the fade can tell. Everything using this runs inside one post, because
    ///         the UI manager puts the hover back to the real mouse at the start of every frame.
    /// </summary>
    private void Hover(Button button)
    {
        UiManager.SetHovered(button);
        button.ForceRunStyleUpdate();
        Feel.UpdateButtons(button, 0f);
    }

    /// <summary>
    ///     By the time a hover is seen, the sheet has moved the button to its hover colour, so a fade has to get the normal
    ///         one from somewhere that doesn't change with the state, or it'd run from the target to the target.
    /// </summary>
    [Test]
    public async Task HoverFadesInFromTheNormalColour()
    {
        var button = await MakeButton();

        await Client.WaitAssertion(() =>
        {
            Hover(button);

            Assert.That(button.DrawMode, Is.EqualTo(BaseButton.DrawModeEnum.Hover));

            var normal = KsTguiPalettes.Primary.Element;
            var hover = KsTguiPalettes.Primary.HoveredElement;

            Assert.That(button.ModulateSelfOverride, Is.EqualTo(normal), "No frame should show the hover colour first");

            Feel.UpdateButtons(button, 0.1f);
            var midway = button.ModulateSelfOverride!.Value;

            Assert.That(midway.B, Is.GreaterThan(normal.B).And.LessThan(hover.B), "Halfway should be between the two");

            Feel.UpdateButtons(button, 0.2f);

            Assert.That(button.ModulateSelfOverride, Is.EqualTo(hover));

            UiManager.SetHovered(null);
            button.Orphan();
        });
    }

    [Test]
    public async Task FinishedFadeHandsTheColourBack()
    {
        var button = await MakeButton();

        await Client.WaitAssertion(() =>
        {
            Hover(button);
            Feel.UpdateButtons(button, 1f);

            Assert.That(button.ModulateSelfOverride, Is.Not.Null, "Held while hovered");

            UiManager.SetHovered(null);
            button.ForceRunStyleUpdate();
            Feel.UpdateButtons(null, 1f);

            Assert.That(button.ModulateSelfOverride, Is.Null, "A finished fade has to give the colour back to the sheet");

            button.Orphan();
        });
    }

    /// <summary>
    ///     A selected button stays held by its fade while it's pressed, so nothing but the toggle can clear it.
    /// </summary>
    [Test]
    public async Task TogglingOffClearsAFadeInProgress()
    {
        var button = await MakeButton(toggled: true);

        await Client.WaitAssertion(() =>
        {
            Hover(button);
            Feel.UpdateButtons(button, 1f);

            Assert.That(button.ModulateSelfOverride, Is.Not.Null);

            UiManager.SetHovered(null);
        });

        await SetTheme(false);

        await Client.WaitAssertion(() =>
        {
            Assert.That(button.ModulateSelfOverride, Is.Null, "Baron shouldn't get a button with a tgui colour stuck on it");
            button.Orphan();
        });
    }

    /// <summary>
    ///     tgui's selected and held buttons are both "pressed" to the engine.
    /// </summary>
    [Test]
    public async Task HoveringASelectedButtonLightensIt()
    {
        var button = await MakeButton(toggled: true);

        await Client.WaitAssertion(() =>
        {
            Hover(button);
            Feel.UpdateButtons(button, 1f);

            var selected = KsTguiPalettes.SelectedElement;
            var expected = KsTguiFeelUIController.ShiftLightness(selected, 0.1f);

            Assert.That(button.ModulateSelfOverride!.Value.G, Is.EqualTo(expected.G).Within(0.01f));

            UiManager.SetHovered(null);
            button.Orphan();
        });
    }

    /// <summary>
    ///     A disabled button doesn't react to the mouse, whether it was disabled before the hover, during it, or is
    ///         re-enabled while the mouse stays on it.
    /// </summary>
    [Test]
    public async Task DisabledHoverDoesNothing()
    {
        var button = await MakeButton();

        await Client.WaitAssertion(() =>
        {
            button.Disabled = true;
            Hover(button);
            Feel.UpdateButtons(button, 0.1f);
            Assert.That(button.ModulateSelfOverride, Is.Null, "Disabled before the hover");

            button.Disabled = false;
            button.ForceRunStyleUpdate();
            Feel.UpdateButtons(button, 0f);
            Assert.That(button.ModulateSelfOverride, Is.Not.Null, "Re-enabled under the mouse fades in");

            button.Disabled = true;
            button.ForceRunStyleUpdate();
            Feel.UpdateButtons(button, 0.1f);
            Assert.That(button.ModulateSelfOverride, Is.Null, "Disabled during the hover lets go");

            UiManager.SetHovered(null);
            button.Orphan();
        });
    }

    [Test]
    public async Task LeavesOthersOverridesAlone()
    {
        var chosen = Color.Red;

        var button = await MakeButton();
        await Client.WaitAssertion(() =>
        {
            button.ModulateSelfOverride = chosen;
            Hover(button);
            Feel.UpdateButtons(button, 1f);

            Assert.That(button.ModulateSelfOverride, Is.EqualTo(chosen));

            UiManager.SetHovered(null);
            button.Orphan();
        });
    }

    [Test]
    public async Task InertWithoutTheTheme()
    {
        await SetTheme(false);

        var button = await MakeButton();
        await Client.WaitAssertion(() =>
        {
            Hover(button);
            Feel.UpdateButtons(button, 1f);

            Assert.That(button.ModulateSelfOverride, Is.Null);

            UiManager.SetHovered(null);
            button.Orphan();
        });
    }

    /// <summary>
    ///     An option button's entries are plain buttons in an unclassed box, so no rule can reach them.
    /// </summary>
    [Test]
    public async Task DropdownEntriesAreTagged()
    {
        Button entry = null;
        Popup popup = null;
        await Client.WaitPost(() =>
        {
            entry = new Button();
            popup = new Popup
            {
                StyleClasses = { OptionButton.StyleClassPopup },
                Children = { new BoxContainer { Children = { entry } } },
            };
            UiManager.ModalRoot.AddChild(popup);
        });

        await Client.WaitAssertion(() =>
        {
            Assert.That(entry.HasStyleClass(KsTguiSheetlet.StyleClassDropdownEntry));
            popup.Orphan();
        });
    }

    [TestCase(0.5f, 0.1f, 0.6f)]
    [TestCase(0.95f, 0.1f, 1f)]
    public void LightnessShiftsByPoints(float lightness, float shift, float expected)
    {
        var color = Color.FromHsl(new Vector4(0.5f, 0.5f, lightness, 1f));

        var shifted = Color.ToHsl(KsTguiFeelUIController.ShiftLightness(color, shift));

        Assert.That(shifted.Z, Is.EqualTo(expected).Within(0.01f));
    }

    [Test]
    public void TranslucentWashesAreNotShifted()
    {
        var wash = KsTguiPalettes.Wash.WithAlpha(0.25f);

        Assert.That(KsTguiFeelUIController.ShiftLightness(wash, 0.1f), Is.EqualTo(wash));
    }
}
