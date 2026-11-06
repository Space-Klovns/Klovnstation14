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
