using System.Linq;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui.Controls;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     The tgui control library, built from code and laid out under both themes. Nothing headless draws, so this pins
///         construction, layout and the style-gated choices, not how any of it looks.
/// </summary>
[TestOf(typeof(KsTguiSection))]
public sealed class KsTguiControlsTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [TestCase(true)]
    [TestCase(false)]
    public async Task EveryControlConstructsAndLaysOut(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);

        DefaultWindow window = null;
        KsTguiSection section = null;
        KsTguiLabeledList list = null;
        KsTguiLabeledListItem shortItem = null;
        KsTguiLabeledListItem longItem = null;
        KsTguiNoticeBox notice = null;
        KsTguiButton plainButton = null;
        KsTguiButton iconButton = null;
        KsTguiTabs tabs = null;
        KsTguiProgressBar progress = null;
        KsSectionPanel retrofitPanel = null;
        KsSectionTitle retrofitTitle = null;
        Label bodyLabel = null;

        await Client.WaitPost(() =>
        {
            var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };

            section = new KsTguiSection { Title = "Test" };
            bodyLabel = new Label { Text = "Body" };
            section.AddChild(bodyLabel);
            root.AddChild(section);

            list = new KsTguiLabeledList();
            shortItem = list.AddItem("Id", "one");
            longItem = list.AddItem("A longer label", "two", Color.Red);
            longItem.Buttons = new KsTguiButton { Text = "Go", Compact = true };
            root.AddChild(list);

            notice = new KsTguiNoticeBox { Text = "Careful", NoticeColor = KsTguiNoticeColor.Warning };
            root.AddChild(notice);

            plainButton = new KsTguiButton { Text = "Plain" };
            iconButton = new KsTguiButton { Text = "Plain", Icon = KsTguiIcons.Cart, Color = KsTguiColor.Green };
            root.AddChild(plainButton);
            root.AddChild(iconButton);
            root.AddChild(new KsTguiIcon { Glyph = KsTguiIcons.Cog });

            tabs = new KsTguiTabs();
            tabs.AddTab("First", icon: KsTguiIcons.Home, selected: true);
            tabs.AddTab("Second", count: "3");
            root.AddChild(tabs);

            progress = new KsTguiProgressBar { Value = 0.5f };
            progress.AddRange(KsTguiColor.Bad, 0f, 0.6f);
            root.AddChild(progress);

            retrofitPanel = new KsSectionPanel { BaronColor = Color.Red };
            retrofitPanel.AddChild(new Label { Text = "Retrofit" });
            retrofitTitle = new KsSectionTitle();
            retrofitTitle.AddChild(new Label { Text = "Title" });
            root.AddChild(retrofitPanel);
            root.AddChild(retrofitTitle);

            window = new DefaultWindow { Title = "Controls" };
            window.Contents.AddChild(root);
            window.OpenCentered();
        });

        await Pair.RunTicksSync(5);

        await Client.WaitAssertion(() =>
        {
            // Left on the panel itself, a child added from code would sit over the title with no padding.
            var labelTop = (bodyLabel.GlobalPosition - section.GlobalPosition).Y;
            Assert.That(labelTop, Is.GreaterThanOrEqualTo(section.Children.First().Children.First().Size.Y), "A child added from code must sit below the title row");
            Assert.That((bodyLabel.GlobalPosition - section.GlobalPosition).X, Is.GreaterThanOrEqualTo(6f), "A child added from code must get the body's padding");

            foreach (var control in new Control[]
                     {
                         section, list, notice, plainButton, iconButton, tabs, progress, retrofitPanel, retrofitTitle,
                     })
            {
                Assert.That(control.Size.X, Is.GreaterThan(0f), $"{control.GetType().Name} has no width");
                Assert.That(control.Size.Y, Is.GreaterThan(0f), $"{control.GetType().Name} has no height");
            }

            // The body sits below the title row, inside the section.
            Assert.That(bodyLabel.GlobalPosition.Y, Is.GreaterThan(section.GlobalPosition.Y));
            Assert.That(bodyLabel.GlobalPosition.Y + bodyLabel.Size.Y, Is.LessThanOrEqualTo(section.GlobalPosition.Y + section.Size.Y + 0.5f));

            // Values line up whatever the labels' widths.
            Assert.That(GetValueLeft(shortItem), Is.EqualTo(GetValueLeft(longItem)).Within(0.5f));

            // The icon takes room.
            Assert.That(iconButton.DesiredSize.X, Is.GreaterThan(plainButton.DesiredSize.X));

            Assert.That(tabs.SelectedIndex, Is.EqualTo(0));
            Assert.That(progress.Fraction, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(progress.ResolvedColor, Is.EqualTo(KsTguiColor.Bad));

            // The retrofit panel is flat under Baron and the sheet's under tgui.
            retrofitPanel.ForceRunStyleUpdate();
            retrofitTitle.ForceRunStyleUpdate();
            Assert.That(retrofitPanel.PanelOverride == null, Is.EqualTo(tgui));
            Assert.That(retrofitTitle.PanelOverride == null, Is.EqualTo(tgui));

            window.Close();
        });
    }

    private static float GetValueLeft(KsTguiLabeledListItem item)
    {
        // The value box is the second child of a row.
        var index = 0;
        foreach (var child in item.Children)
        {
            if (index++ == 1)
                return child.GlobalPosition.X;
        }

        return float.NaN;
    }

    /// <summary>
    ///     A button toggled disabled while shown dims its icon with its text. <c>Button</c> restyles only its own label when its
    ///         state changes, so an icon child kept its enabled colour.
    /// </summary>
    [Test]
    public async Task DisabledButtonsDimTheirIcons()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Pair.RunTicksSync(2);

        KsTguiButton tguiButton = null;
        KsRestylingButton stockButton = null;
        Label stockIcon = null;
        DefaultWindow window = null;

        await Client.WaitPost(() =>
        {
            var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
            tguiButton = new KsTguiButton { Text = "Eject", Icon = KsTguiIcons.Cart };
            stockButton = new KsRestylingButton { Text = "" };
            stockIcon = new Label { Text = "x" };
            stockButton.AddChild(stockIcon);
            root.AddChild(tguiButton);
            root.AddChild(stockButton);

            window = new DefaultWindow { Title = "Icons" };
            window.Contents.AddChild(root);
            window.OpenCentered();
        });

        await Pair.RunTicksSync(3);

        var tguiIcon = tguiButton.Children.OfType<KsTguiIcon>().Single();

        static Color Colour(Control control)
        {
            return control.TryGetStyleProperty<Color>(Label.StylePropertyFontColor, out var colour) ? colour : Color.White;
        }

        Color enabledIcon = default;
        Color enabledStock = default;
        await Client.WaitPost(() =>
        {
            enabledIcon = Colour(tguiIcon);
            enabledStock = Colour(stockIcon);
            tguiButton.Disabled = true;
            stockButton.Disabled = true;
        });

        await Pair.RunTicksSync(3);

        await Client.WaitAssertion(() =>
        {
            Assert.That(Colour(tguiIcon), Is.Not.EqualTo(enabledIcon), "The icon keeps its enabled colour");
            Assert.That(Colour(tguiIcon), Is.EqualTo(Colour(tguiButton.Label)), "The icon dims as the text does");
            Assert.That(Colour(stockIcon), Is.Not.EqualTo(enabledStock), "A stock button's icon label keeps its enabled colour");
            window.Close();
        });
    }

    /// <summary>
    ///     Stripes follow the visible index, as CSS <c>nth-child(odd)</c> does: hiding a row or adding one re-alternates the
    ///         rest, and a table counts its header row.
    /// </summary>
    [Test]
    public async Task CandystripeAlternatesByVisibleIndex()
    {
        await Client.WaitAssertion(() =>
        {
            var list = new KsTguiLabeledList { Candystripe = true };
            var items = Enumerable.Range(0, 5).Select(i => list.AddItem("row" + i)).ToArray();
            Assert.That(items.Select(list.IsStriped), Is.EqualTo(new[] { true, false, true, false, true }));

            items[1].Visible = false;
            Assert.That(items.Where(item => item.Visible).Select(list.IsStriped), Is.EqualTo(new[] { true, false, true, false }));

            list.AddItem("added");
            Assert.That(list.IsStriped(items[4]), Is.False);

            list.Candystripe = false;
            Assert.That(items.Any(list.IsStriped), Is.False);

            var table = new KsTguiTable();
            var header = new KsTguiTableRow { Header = true };
            var first = new KsTguiTableRow { Candystripe = true };
            var second = new KsTguiTableRow { Candystripe = true };
            var third = new KsTguiTableRow { Candystripe = true };
            table.AddChild(header);
            table.AddChild(first);
            table.AddChild(second);
            table.AddChild(third);
            Assert.That(new[] { header, first, second, third }.Select(table.IsStriped), Is.EqualTo(new[] { false, false, true, false }));

            var stack = new KsTguiCandystripe();
            var children = Enumerable.Range(0, 4).Select(_ => new Label()).ToArray();
            foreach (var child in children)
                stack.AddChild(child);
            children[0].Visible = false;
            Assert.That(children.Skip(1).Select(stack.IsStriped), Is.EqualTo(new[] { true, false, true }));
        });
    }

    [Test]
    public void ColourShadesMatchTguiCore()
    {
        // The green tgui draws as a selected button, and the primary blue behind a default one.
        AssertColor(KsTguiColors.Element(KsTguiColor.Green), "#1B9844");
        AssertColor(KsTguiColors.Hovered(KsTguiColor.Green), "#22C358");
        AssertColor(KsTguiColors.Element(KsTguiColor.Default), "#40668C");
        AssertColor(KsTguiColors.Hovered(KsTguiColor.Default), "#507FAF");
    }

    private static void AssertColor(Color actual, string hex)
    {
        var expected = Color.FromHex(hex);
        Assert.That(actual.R, Is.EqualTo(expected.R).Within(1.5f / 255f), hex);
        Assert.That(actual.G, Is.EqualTo(expected.G).Within(1.5f / 255f), hex);
        Assert.That(actual.B, Is.EqualTo(expected.B).Within(1.5f / 255f), hex);
    }
}
