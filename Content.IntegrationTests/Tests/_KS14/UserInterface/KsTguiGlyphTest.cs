using System.Linq;
using System.Threading.Tasks;
using Content.Client.UserInterface.Controls;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     The checkbox, switch and chevron shapes are drawn by shaders chosen by style, so each has to follow its state.
/// </summary>
public sealed class KsTguiGlyphTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private IUserInterfaceManager UiManager => Client.ResolveDependency<IUserInterfaceManager>();

    private static ShaderInstance Shader(Control control)
    {
        control.TryGetStyleProperty<ShaderInstance>(TextureRect.StylePropertyShader, out var shader);
        return shader;
    }

    [Test]
    public async Task ToggleShapesFollowTheState()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Pair.RunTicksSync(2);

        await Client.WaitAssertion(() =>
        {
            var checkBox = new CheckBox { Text = "x" };
            var switchButton = new SwitchButton { Text = "x" };
            var option = new OptionButton();
            UiManager.PopupRoot.AddChild(checkBox);
            UiManager.PopupRoot.AddChild(switchButton);
            UiManager.PopupRoot.AddChild(option);

            void Restyle()
            {
                checkBox.ForceRunStyleUpdate();
                checkBox.TextureRect.ForceRunStyleUpdate();
                switchButton.ForceRunStyleUpdate();
                switchButton.TrackOutline.ForceRunStyleUpdate();
            }

            Restyle();
            var emptyBox = Shader(checkBox.TextureRect);
            var emptySwitch = Shader(switchButton.TrackOutline);
            Assert.That(emptyBox, Is.Not.Null);
            Assert.That(emptySwitch, Is.Not.Null);

            checkBox.Pressed = true;
            switchButton.Pressed = true;
            Restyle();
            var tickedBox = Shader(checkBox.TextureRect);
            Assert.That(tickedBox, Is.Not.Null.And.Not.SameAs(emptyBox), "Checked box shows the tick");
            Assert.That(Shader(switchButton.TrackOutline), Is.Not.Null.And.Not.SameAs(emptySwitch));

            checkBox.Pressed = false;
            switchButton.Pressed = false;
            Restyle();
            Assert.That(Shader(checkBox.TextureRect), Is.SameAs(emptyBox), "Unchecking goes back");
            Assert.That(Shader(switchButton.TrackOutline), Is.SameAs(emptySwitch));

            option.ForceRunStyleUpdate();
            foreach (var child in option.Children)
                child.ForceRunStyleUpdate();

            checkBox.Orphan();
            switchButton.Orphan();
            option.Orphan();
        });
    }

    /// <summary>
    ///     A heading's chevron is its own style box, chosen by the heading's pressed state, so opening it must change what
    ///         is drawn on both themes with no reopening.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task HeadingChevronFollowsOpenState(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);

        await Client.WaitAssertion(() =>
        {
            var heading = new CollapsibleHeading("x");
            var collapsible = new Collapsible(heading, new CollapsibleBody());
            UiManager.PopupRoot.AddChild(collapsible);

            Content.Client._KS14.Expander.KsCollapsibleChevronStyleBox? Box()
            {
                heading.ForceRunStyleUpdate();
                heading.TryGetStyleProperty<StyleBox>(ContainerButton.StylePropertyStyleBox, out var box);
                return box as Content.Client._KS14.Expander.KsCollapsibleChevronStyleBox;
            }

            Assert.That(Box(), Is.Not.Null);
            Assert.That(Box()!.Open, Is.False, "Closed shows the right chevron");

            heading.Pressed = true;
            Assert.That(Box()!.Open, Is.True, "Opening swaps it");

            heading.Pressed = false;
            Assert.That(Box()!.Open, Is.False, "Closing swaps it back");

            collapsible.Orphan();
        });
    }

    /// <summary>
    ///     The stock chevron is a grandchild of the heading (heading, box, texture). A rule that never matched it left it drawn
    ///         beside the style box's own, so under the tgui theme it must be hidden; Baron's copy is not this test's to judge.
    /// </summary>
    [Test]
    public async Task TguiHidesTheStockHeadingChevron()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Pair.RunTicksSync(2);

        await Client.WaitAssertion(() =>
        {
            var heading = new CollapsibleHeading("x");
            var collapsible = new Collapsible(heading, new CollapsibleBody());
            UiManager.PopupRoot.AddChild(collapsible);

            var chevron = heading.Children.Single().Children.OfType<TextureRect>().Single();
            chevron.ForceRunStyleUpdate();
            Assert.That(chevron.TryGetStyleProperty<Color>(Control.StylePropertyModulateSelf, out var modulate), Is.True, "The tgui sheet should style the chevron.");
            Assert.That(modulate.A, Is.Zero, "The stock chevron should be transparent.");

            collapsible.Orphan();
        });
    }
}
