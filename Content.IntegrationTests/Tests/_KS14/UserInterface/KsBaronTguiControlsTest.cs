using System.Threading.Tasks;
using Content.Client._KS14.BaronUI;
using Content.Client._KS14.Tgui;
using Content.Client._KS14.Tgui.Controls;
using Content.Client._KS14.Tgui.Windows.Fax;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     Since #480 Baron opens the ported tgui windows, so its sheet has to style the tgui control library: a selected button,
///         a coloured notice and a selected list row must look different from a plain one, a section's title gets a line, and
///         a ported window gets the room Baron's taller header takes. Under tgui none of Baron's rules may apply.
/// </summary>
/// <remarks>
///     Nothing headless draws, so this reads what the sheet resolved (modulate, boxes, properties), not pixels.
/// </remarks>
[TestOf(typeof(KsBaronUiSheetlet))]
public sealed class KsBaronTguiControlsTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [Test]
    public async Task BaronStylesTheTguiControlLibrary()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, false);
        await Pair.RunTicksSync(2);

        DefaultWindow window = null;
        KsTguiButton plain = null;
        KsTguiButton selected = null;
        KsTguiButton green = null;
        PanelContainer bare = null;
        KsTguiNoticeBox notice = null;
        KsTguiList list = null;
        KsTguiSection section = null;

        await Client.WaitPost(() =>
        {
            var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
            plain = new KsTguiButton { Text = "Plain" };
            selected = new KsTguiButton { Text = "Selected", Selected = true };
            green = new KsTguiButton { Text = "Green", Color = KsTguiColor.Green };
            bare = new PanelContainer();
            notice = new KsTguiNoticeBox { Text = "Careful", NoticeColor = KsTguiNoticeColor.Danger };
            list = new KsTguiList();
            list.AddRow("First");
            list.AddRow("Second");
            list.Select(1);
            section = new KsTguiSection { Title = "Section" };

            foreach (var control in new Control[] { plain, selected, green, bare, notice, list, section })
                root.AddChild(control);

            window = new DefaultWindow { Title = "Baron" };
            window.Contents.AddChild(root);
            window.OpenCentered();
        });
        await Pair.RunTicksSync(5);

        await Client.WaitAssertion(() =>
        {
            foreach (var control in new Control[] { plain, selected, green, bare, notice, list.Rows[0], list.Rows[1] })
                control.ForceRunStyleUpdate();

            Assert.Multiple(() =>
            {
                var plainTint = Modulate(plain);
                Assert.That(Modulate(selected), Is.Not.EqualTo(plainTint), "a selected button must not look like a plain one");
                Assert.That(Modulate(green), Is.Not.EqualTo(plainTint), "a coloured button must not look like a plain one");
                Assert.That(Modulate(selected).G, Is.GreaterThan(Modulate(selected).R), "selected is Baron's green");

                // A coloured notice is its colour, not the opaque dark panel every bare PanelContainer gets.
                Assert.That(Panel(notice), Is.TypeOf<StyleBoxFlat>());
                Assert.That(((StyleBoxFlat)Panel(notice)).BackgroundColor,
                    Is.EqualTo(KsTguiNoticeBox.BackgroundOf(KsTguiNoticeColor.Danger, KsTguiPalettes.Notice)));
                Assert.That(Panel(notice), Is.Not.SameAs(Panel(bare)));

                // The selected row of a list is told apart from the others.
                Assert.That(list.Rows[1].TryGetStyleProperty<StyleBox>(ContainerButton.StylePropertyStyleBox, out var selectedRow), Is.True);
                Assert.That(list.Rows[0].TryGetStyleProperty<StyleBox>(ContainerButton.StylePropertyStyleBox, out var otherRow), Is.True);
                Assert.That(((StyleBoxFlat)selectedRow).BackgroundColor, Is.Not.EqualTo(((StyleBoxFlat)otherRow).BackgroundColor));

                // tgui's 2px gaps between buttons.
                Assert.That(plain.Margin.Right, Is.EqualTo(2f));

                // The title row draws its line.
                var title = FindTitle(section);
                Assert.That(title, Is.Not.Null);
                title!.ForceRunStyleUpdate();
                Assert.That(title.PanelOverride, Is.Null, "Baron's sheet draws a section's title row");
            });

            window.Close();
        });
    }

    /// <summary>
    ///     The Baron rules live in the Baron sheet alone: under tgui none of them reaches a control.
    /// </summary>
    [Test]
    public async Task TguiGetsNoneOfBaronsRules()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Pair.RunTicksSync(2);

        DefaultWindow window = null;
        KsTguiSection section = null;

        await Client.WaitPost(() =>
        {
            section = new KsTguiSection { Title = "Section" };
            window = new DefaultWindow { Title = "Tgui" };
            window.Contents.AddChild(section);
            window.OpenCentered();
        });
        await Pair.RunTicksSync(5);

        await Client.WaitAssertion(() =>
        {
            var title = FindTitle(section);
            Assert.That(title, Is.Not.Null);
            title!.ForceRunStyleUpdate();
            Assert.That(title.TryGetStyleProperty<bool>(KsBaronUiSheetlet.StylePropertySectionTitle, out _), Is.False);
            window.Close();
        });
    }

    /// <summary>
    ///     A ported window states tg's height, which counts on tgui's 32px title bar; under Baron it gets the difference.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task PortedWindowGetsBaronsHeaderRoom(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);

        await Client.WaitAssertion(() =>
        {
            var window = new KsTguiFaxWindow();
            var stated = window.SetHeight;
            window.OpenCentered();

            if (tgui)
                Assert.That(window.SetHeight, Is.EqualTo(stated), "tgui keeps tg's size");
            else
                Assert.That(window.SetHeight, Is.GreaterThan(stated + 20f), "Baron adds its taller header");

            // Reopening doesn't add it again.
            var grown = window.SetHeight;
            window.Close();
            window.OpenCentered();
            Assert.That(window.SetHeight, Is.EqualTo(grown));
            window.Close();
        });
    }

    private static Color Modulate(Control control)
    {
        return control.TryGetStyleProperty<Color>(Control.StylePropertyModulateSelf, out var color) ? color : Color.White;
    }

    private static StyleBox Panel(PanelContainer panel)
    {
        return panel.TryGetStyleProperty<StyleBox>(PanelContainer.StylePropertyPanel, out var box) ? box : null;
    }

    private static KsSectionTitle FindTitle(Control parent)
    {
        foreach (var child in parent.Children)
        {
            if (child is KsSectionTitle title)
                return title;

            if (FindTitle(child) is { } found)
                return found;
        }

        return null;
    }
}
