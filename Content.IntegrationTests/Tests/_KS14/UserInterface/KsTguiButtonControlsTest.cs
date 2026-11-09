using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui;
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
///     tgui's Button and its relatives (Checkbox, Confirm, Input), Icon, Tabs, Section and Collapsible: the behaviour that
///         layout and state can show. Nothing headless draws, so how they look is checked on a real client.
/// </summary>
[TestOf(typeof(KsTguiButton))]
public sealed class KsTguiButtonControlsTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private async Task<DefaultWindow> Show(Control content)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Pair.RunTicksSync(2);

        DefaultWindow window = null;
        await Client.WaitPost(() =>
        {
            window = new DefaultWindow { Title = "Buttons" };
            window.Contents.AddChild(content);
            window.SetSize = new Vector2(400f, 400f);
            window.OpenCentered();
        });

        await Pair.RunTicksSync(5);
        return window;
    }

    [Test]
    public void CautionTextIsWhiteAndOnlyYellowAndWhiteAreBlack()
    {
        Assert.That(KsTguiColors.Text(KsTguiColor.Caution), Is.EqualTo(Color.White), "tgui's caution is not a low-contrast colour");
        Assert.That(KsTguiColors.Text(KsTguiColor.Yellow), Is.EqualTo(Color.Black));
        Assert.That(KsTguiColors.Text(KsTguiColor.White), Is.EqualTo(Color.Black));
    }

    [Test]
    public async Task TextIsLeftAlignedUnlessAskedOtherwise()
    {
        KsTguiButton button = null;
        BoxContainer box = null;
        await Client.WaitPost(() =>
        {
            box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
            button = new KsTguiButton { Text = "Wide", Fluid = true };
            box.AddChild(button);
        });

        var window = await Show(box);
        await Client.WaitAssertion(() =>
        {
            Assert.That(button.Label.Align, Is.EqualTo(Label.AlignMode.Left), "tgui's Button text-aligns to the start");
            Assert.That(button.Label.Position.X, Is.LessThan(8f));

            button.LeftAligned = false;
            button.ForceRunStyleUpdate();
            Assert.That(button.Label.Align, Is.EqualTo(Label.AlignMode.Center), "textAlign=center");
            window.Close();
        });
    }

    [Test]
    public async Task MarginsFollowTgui()
    {
        KsTguiButton first = null;
        KsTguiButton fluid = null;
        KsTguiButton last = null;
        BoxContainer box = null;
        await Client.WaitPost(() =>
        {
            box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
            first = new KsTguiButton { Text = "First" };
            fluid = new KsTguiButton { Text = "Fluid", Fluid = true };
            last = new KsTguiButton { Text = "Last" };
            box.AddChild(first);
            box.AddChild(fluid);
            box.AddChild(last);
        });

        var window = await Show(box);
        await Client.WaitAssertion(() =>
        {
            Assert.That(first.Margin, Is.EqualTo(new Thickness(0f, 0f, 2f, 2f)), "2px right and bottom");
            Assert.That(fluid.Margin, Is.EqualTo(new Thickness(0f, 0f, 0f, 2f)), "a fluid button has no side margins");
            Assert.That(last.Margin, Is.EqualTo(default(Thickness)), "the last child has none");
            window.Close();
        });
    }

    [Test]
    public async Task SelectedTransparentStaysClear()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Client.WaitAssertion(() =>
        {
            var button = new KsTguiButton { Text = "Toggle", Color = KsTguiColor.Transparent, Selected = true };
            Assert.That(button.HasStyleClass(KsTguiSheetlet.StyleClassButtonSelectedTransparent), Is.True);
            Assert.That(button.HasStyleClass(KsTguiSheetlet.StyleClassButtonSelected), Is.False, "not the solid green slab");

            var opaque = new KsTguiButton { Text = "Toggle", Color = KsTguiColor.Default, Selected = true };
            Assert.That(opaque.HasStyleClass(KsTguiSheetlet.StyleClassButtonSelected), Is.True, "an opaque button turns green");

            button.Selected = false;
            Assert.That(button.HasStyleClass(KsTguiColors.ButtonClass(KsTguiColor.Transparent)), Is.True);
        });
    }

    [Test]
    public async Task EllipsisCutsLongTextAndKeepsTheFullText()
    {
        KsTguiButton button = null;
        BoxContainer box = null;
        const string text = "A very long button caption that cannot possibly fit";
        await Client.WaitPost(() =>
        {
            box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
            button = new KsTguiButton { Text = text, EllipsisText = true, Fluid = true, MaxWidth = 90f };
            box.AddChild(button);
        });

        var window = await Show(box);
        await Client.WaitAssertion(() =>
        {
            Assert.That(button.Label.Text, Does.EndWith("…"));
            Assert.That(button.Label.Text.Length, Is.LessThan(text.Length));
            Assert.That(button.FullText, Is.EqualTo(text));

            button.EllipsisText = false;
            Assert.That(button.Label.Text, Is.EqualTo(text), "turning it off restores the text");
            window.Close();
        });
    }

    [Test]
    public async Task StrikethroughLineSitsInsideTheText()
    {
        KsTguiButton button = null;
        BoxContainer box = null;
        await Client.WaitPost(() =>
        {
            box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
            button = new KsTguiButton { Text = "Struck", Strikethrough = true };
            box.AddChild(button);
        });

        var window = await Show(box);
        await Client.WaitAssertion(() =>
        {
            Assert.That(button.StrikeLineBounds, Is.Not.Null);
            var (left, right, y) = button.StrikeLineBounds.Value;
            Assert.That(right - left, Is.EqualTo(button.TextWidth("Struck")).Within(0.5f));
            Assert.That(y, Is.GreaterThan(button.Label.Position.Y).And.LessThan(button.Label.Position.Y + button.Label.Size.Y));

            button.Strikethrough = false;
            Assert.That(button.StrikeLineBounds, Is.Null);
            window.Close();
        });
    }

    [Test]
    public async Task IconOnTheRightComesAfterTheText()
    {
        KsTguiButton left = null;
        KsTguiButton right = null;
        BoxContainer box = null;
        await Client.WaitPost(() =>
        {
            box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
            left = new KsTguiButton { Text = "Text", Icon = KsTguiIcons.Cog };
            right = new KsTguiButton { Text = "Text", Icon = KsTguiIcons.Cog, IconPosition = KsTguiIconPosition.Right };
            box.AddChild(left);
            box.AddChild(right);
        });

        var window = await Show(box);
        await Client.WaitAssertion(() =>
        {
            Assert.That(right.Label.Position.X, Is.LessThan(left.Label.Position.X), "text first when the icon is on the right");
            Assert.That(right.Size.X, Is.EqualTo(left.Size.X).Within(1f).Or.GreaterThan(0f));
            window.Close();
        });
    }

    [Test]
    public async Task CircularAndVerticalAlignAreAccepted()
    {
        KsTguiButton top = null;
        KsTguiButton bottom = null;
        BoxContainer box = null;
        await Client.WaitPost(() =>
        {
            box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
            top = new KsTguiButton { Text = "T", Circular = true, VerticalAlignContent = KsTguiButtonContentAlign.Top, MinHeight = 60f };
            bottom = new KsTguiButton { Text = "B", VerticalAlignContent = KsTguiButtonContentAlign.Bottom, MinHeight = 60f };
            box.AddChild(top);
            box.AddChild(bottom);
        });

        var window = await Show(box);
        await Client.WaitAssertion(() =>
        {
            Assert.That(top.HasStyleClass(KsTguiSheetlet.StyleClassButtonCircular), Is.True);
            Assert.That(top.Label.Position.Y, Is.LessThan(top.Size.Y / 3f), "top");
            Assert.That(bottom.Label.Position.Y, Is.GreaterThan(bottom.Size.Y * 2f / 3f), "bottom");
            window.Close();
        });
    }

    [Test]
    public async Task CheckboxButtonTicksAndTurnsGreen()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Client.WaitAssertion(() =>
        {
            var checkbox = new KsTguiCheckboxButton { Text = "Option" };
            var seen = new List<bool>();
            checkbox.Toggled += seen.Add;

            Assert.That(checkbox.Icon, Is.EqualTo(KsTguiIcons.SquareO));
            Assert.That(checkbox.IconRegular, Is.True, "tgui's -o icons are Font Awesome Regular");

            checkbox.Press();
            Assert.That(checkbox.Checked, Is.True);
            Assert.That(checkbox.Selected, Is.True);
            Assert.That(checkbox.Icon, Is.EqualTo(KsTguiIcons.CheckSquareO));
            Assert.That(checkbox.HasStyleClass(KsTguiSheetlet.StyleClassButtonSelectedTransparent), Is.True);

            checkbox.AutoToggle = false;
            checkbox.Press();
            Assert.That(checkbox.Checked, Is.True, "an owner that decides itself keeps control");
            Assert.That(seen, Is.EqualTo(new[] { true, false }));
        });
    }

    [Test]
    public async Task ConfirmButtonNeedsTwoPresses()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Client.WaitAssertion(() =>
        {
            var button = new KsTguiConfirmButton { Text = "Delete", Icon = KsTguiIcons.Trash };
            var confirmed = 0;
            button.Confirmed += () => confirmed++;

            button.Press();
            Assert.That(confirmed, Is.EqualTo(0));
            Assert.That(button.Armed, Is.True);
            Assert.That(button.Color, Is.EqualTo(KsTguiColor.Bad));
            Assert.That(button.FullText, Is.EqualTo("Confirm?"));
            Assert.That(button.Icon, Is.Null, "tgui swaps the icon for confirmIcon, which is unset");

            button.Disarm();
            Assert.That(button.Armed, Is.False, "blur puts it back");
            Assert.That(button.FullText, Is.EqualTo("Delete"));
            Assert.That(button.Icon, Is.EqualTo(KsTguiIcons.Trash));
            Assert.That(button.Color, Is.EqualTo(KsTguiColor.Default));
            Assert.That(confirmed, Is.EqualTo(0), "no confirmation without the second press");

            button.Press();
            button.Press();
            Assert.That(confirmed, Is.EqualTo(1));
            Assert.That(button.Armed, Is.False);
            Assert.That(button.FullText, Is.EqualTo("Delete"));
        });
    }

    [Test]
    public async Task InputButtonCommitsOnlyAChangedValue()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Client.WaitAssertion(() =>
        {
            var button = new KsTguiInputButton { Value = "old" };
            var committed = new List<string>();
            button.Committed += committed.Add;

            Assert.That(button.Text, Is.EqualTo("old"));

            button.BeginEdit();
            Assert.That(button.Editing, Is.True);
            button.Finish(cancel: false);
            Assert.That(committed, Is.Empty, "an unchanged value is not committed");

            button.BeginEdit();
            button.SetEditText("new");
            button.Finish(cancel: true);
            Assert.That(committed, Is.Empty, "Escape cancels");
            Assert.That(button.Text, Is.EqualTo("old"));

            button.BeginEdit();
            button.SetEditText("new");
            button.Finish(cancel: false);
            Assert.That(committed, Is.EqualTo(new[] { "new" }));
            Assert.That(button.Text, Is.EqualTo("new"));
        });
    }

    [Test]
    public async Task IconRotatesAndSpinsAtFontAwesomesRate()
    {
        await Client.WaitAssertion(() =>
        {
            var icon = new KsTguiIcon { Glyph = KsTguiIcons.Sync, Rotation = 90f };
            Assert.That(icon.CurrentAngle, Is.EqualTo(90f));

            icon.Spin = true;
            var first = icon.CurrentAngle;
            Assert.That(first, Is.InRange(90f, 450f));
            Assert.That(KsTguiIcon.SpinSecondsPerTurn, Is.EqualTo(2f), "fa-spin is 2s per turn");
        });

        await Pair.RunTicksSync(30);
        await Client.WaitAssertion(() =>
        {
            var icon = new KsTguiIcon { Glyph = KsTguiIcons.Sync, Spin = true };
            var one = icon.CurrentAngle;
            Assert.That(one, Is.InRange(0f, 360f));
        });
    }

    [Test]
    public async Task IconStackLayersAndScalesItsIcons()
    {
        await Client.WaitAssertion(() =>
        {
            var stack = new KsTguiIconStack { IconSize = 2f };
            var big = new KsTguiIcon { Glyph = KsTguiIcons.Square };
            var small = new KsTguiIcon { Glyph = KsTguiIcons.Check };
            stack.AddChild(big);
            stack.AddChild(small);

            Assert.That(big.IconSize, Is.EqualTo(2f));
            Assert.That(small.IconSize, Is.EqualTo(2f));

            stack.Measure(new Vector2(200f, 200f));
            stack.Arrange(new UIBox2(0f, 0f, stack.DesiredSize.X, stack.DesiredSize.Y));
            Assert.That(stack.DesiredSize.X, Is.EqualTo(MathF.Max(big.DesiredSize.X, small.DesiredSize.X)));
            Assert.That(small.Position.X + small.Size.X / 2f, Is.EqualTo(stack.DesiredSize.X / 2f).Within(0.5f), "centred");
        });
    }

    [Test]
    public async Task TabsHaveAStripAndGapsOutsideSectionsAndNegativeMarginsInside()
    {
        KsTguiTabs plain = null;
        KsTguiTabs inSection = null;
        KsTguiSection section = null;
        BoxContainer box = null;
        await Client.WaitPost(() =>
        {
            box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
            plain = new KsTguiTabs();
            plain.AddTab("One", selected: true);
            box.AddChild(plain);
            box.AddChild(new Label { Text = "after" });

            section = new KsTguiSection { Title = "S" };
            inSection = new KsTguiTabs();
            inSection.AddTab("Two", selected: true);
            section.AddChild(inSection);
            box.AddChild(section);
        });

        var window = await Show(box);
        await Client.WaitAssertion(() =>
        {
            Assert.That(plain.StripVisible, Is.True, "the Section-coloured strip behind the tabs");
            Assert.That(plain.OuterMargin.Bottom, Is.EqualTo(6f), "6px under a row of tabs");

            Assert.That(inSection.StripVisible, Is.False, "inside a Section the strip is the Section's own");
            Assert.That(inSection.OuterMargin, Is.EqualTo(new Thickness(-6f, -6f, -6f, 6f)));

            section.Fitted = true;
            inSection.Parent.InvalidateMeasure();
            window.Close();
        });
    }

    [Test]
    public async Task TabIndicatorGrowsAndTheBackgroundFades()
    {
        KsTguiTabs tabs = null;
        KsTguiTabItem first = null;
        KsTguiTabItem second = null;
        BoxContainer box = null;
        await Client.WaitPost(() =>
        {
            box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
            tabs = new KsTguiTabs();
            first = tabs.AddTab("One", selected: true);
            second = tabs.AddTab("Two");
            box.AddChild(tabs);
        });

        var window = await Show(box);
        await Client.WaitAssertion(() =>
        {
            first.SettleAnimations();
            Assert.That(first.IndicatorProgress, Is.EqualTo(1f));
            Assert.That(second.IndicatorProgress, Is.EqualTo(0f));

            tabs.Select(second);
            Assert.That(second.IndicatorProgress, Is.EqualTo(0f), "it starts at nothing and grows");
            Assert.That(first.IndicatorProgress, Is.EqualTo(1f), "and the old one has not vanished at once");
            Assert.That(second.CurrentBackground.A, Is.EqualTo(0f).Within(0.001f));
        });

        // Not a whole 200ms yet: partway.
        await Pair.RunTicksSync(3);
        await Client.WaitAssertion(() =>
        {
            Assert.That(second.IndicatorProgress, Is.GreaterThan(0f).And.LessThan(1f), "partway, from the middle");
        });

        await Pair.RunTicksSync(30);
        await Client.WaitAssertion(() =>
        {
            Assert.That(second.IndicatorProgress, Is.EqualTo(1f));
            Assert.That(first.IndicatorProgress, Is.EqualTo(0f));
            window.Close();
        });
    }

    [Test]
    public async Task NestedSectionsReachTheOuterEdgesAndNoTopPaddingWorks()
    {
        KsTguiSection outer = null;
        KsTguiSection inner = null;
        KsTguiSection plain = null;
        KsTguiSection noTop = null;
        BoxContainer box = null;
        await Client.WaitPost(() =>
        {
            box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
            outer = new KsTguiSection { Title = "Outer" };
            inner = new KsTguiSection { Title = "Inner" };
            inner.AddChild(new Label { Text = "x" });
            outer.AddChild(inner);
            box.AddChild(outer);

            plain = new KsTguiSection { Title = "A" };
            plain.AddChild(new Label { Text = "x" });
            noTop = new KsTguiSection { Title = "B", NoTopPadding = true };
            noTop.AddChild(new Label { Text = "x" });
            box.AddChild(plain);
            box.AddChild(noTop);
        });

        var window = await Show(box);
        await Client.WaitAssertion(() =>
        {
            Assert.That(inner.Margin.Left, Is.EqualTo(-6f), "pulled out over the outer padding: the title line is edge to edge");
            Assert.That(inner.Margin.Top, Is.EqualTo(-6f), "first child");
            Assert.That(inner.Margin.Bottom, Is.EqualTo(0f));
            Assert.That(inner.GlobalPosition.X, Is.EqualTo(outer.GlobalPosition.X).Within(0.5f));
            Assert.That(inner.Size.X, Is.EqualTo(outer.Size.X).Within(0.5f));
            Assert.That(outer.Margin, Is.EqualTo(default(Thickness)));

            Assert.That(noTop.Size.Y, Is.EqualTo(plain.Size.Y - 8f).Within(0.5f), "noTopPadding drops the 8px above the body");
            window.Close();
        });
    }

    [Test]
    public async Task CollapsibleOpensAndFlipsItsChevron()
    {
        KsTguiCollapsible collapsible = null;
        Label content = null;
        BoxContainer box = null;
        await Client.WaitPost(() =>
        {
            box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
            collapsible = new KsTguiCollapsible { Title = "More" };
            content = new Label { Text = "Hidden" };
            collapsible.AddChild(content);
            box.AddChild(collapsible);
        });

        var window = await Show(box);
        await Client.WaitAssertion(() =>
        {
            Assert.That(collapsible.Open, Is.False);
            Assert.That(content.VisibleInTree, Is.False);
            Assert.That(content.Parent, Is.Not.EqualTo(collapsible), "children go to the content");
            Assert.That(collapsible.Header.Icon, Is.EqualTo(KsTguiIcons.ChevronRight));
            Assert.That(collapsible.Header.Margin, Is.EqualTo(default(Thickness)), "the header is the only child of its cell");
            Assert.That(collapsible.Margin.Bottom, Is.EqualTo(6f));

            collapsible.Open = true;
            Assert.That(collapsible.Header.Icon, Is.EqualTo(KsTguiIcons.ChevronDown));
            Assert.That(content.Parent.Visible, Is.True);
            window.Close();
        });
    }

    [Test]
    public async Task ButtonsInARowAreTheSameHeightAndLineUpAtTopAndBottom()
    {
        KsTguiButton first = null;
        KsTguiButton second = null;
        KsTguiButton third = null;
        BoxContainer row = null;
        await Client.WaitPost(() =>
        {
            row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
            first = new KsTguiButton { Text = "Crew Manifest" };
            second = new KsTguiButton { Text = "Random Job!" };
            third = new KsTguiButton { Text = "Last", Icon = KsTguiIcons.Plus };
            row.AddChild(first);
            row.AddChild(second);
            row.AddChild(third);
        });

        var window = await Show(row);
        await Client.WaitAssertion(() =>
        {
            // tg's buttons in a row are inline blocks on a baseline, so margin-bottom never shortens one.
            Assert.That(first.Margin.Bottom, Is.EqualTo(0f), "no bottom margin in a horizontal box");
            Assert.That(first.Margin.Right, Is.EqualTo(2f), "the right margin is kept");
            Assert.That(second.Size.Y, Is.EqualTo(first.Size.Y), "the first button is as tall as the second");
            Assert.That(third.Size.Y, Is.EqualTo(first.Size.Y));
            Assert.That(second.Position.Y, Is.EqualTo(first.Position.Y), "tops line up");
            Assert.That(second.Position.Y + second.Size.Y, Is.EqualTo(first.Position.Y + first.Size.Y), "bottoms line up");
            window.Close();
        });
    }

    [Test]
    public async Task APlainButtonBesideALineEditIsTheSameHeight()
    {
        Button button = null;
        LineEdit edit = null;
        OptionButton selector = null;
        LineEdit edit2 = null;
        BoxContainer root = null;
        await Client.WaitPost(() =>
        {
            button = new Button { Text = "Go" };
            edit = new LineEdit { HorizontalExpand = true };
            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
            row.AddChild(edit);
            row.AddChild(button);
            selector = new OptionButton();
            selector.AddItem("One");
            var row2 = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
            edit2 = new LineEdit { HorizontalExpand = true };
            row2.AddChild(selector);
            row2.AddChild(edit2);
            root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
            root.AddChild(row);
            root.AddChild(row2);
        });

        var window = await Show(root);
        await Client.WaitAssertion(() =>
        {
            Assert.That(button.Size.Y, Is.EqualTo(edit.Size.Y), "a plain Button is not shortened by a margin");
            Assert.That(button.Position.Y + button.Size.Y, Is.EqualTo(edit.Position.Y + edit.Size.Y));
            Assert.That(selector.Size.Y, Is.EqualTo(edit2.Size.Y), "nor is a dropdown");
            window.Close();
        });
    }

    [Test]
    public async Task ButtonsInAColumnKeepTheTwoPixelGap()
    {
        KsTguiButton first = null;
        KsTguiButton second = null;
        BoxContainer column = null;
        await Client.WaitPost(() =>
        {
            first = new KsTguiButton { Text = "One" };
            second = new KsTguiButton { Text = "Two" };
            column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 0 };
            column.AddChild(first);
            column.AddChild(second);
        });

        var window = await Show(column);
        await Client.WaitAssertion(() =>
        {
            Assert.That(second.Position.Y - (first.Position.Y + first.Size.Y), Is.EqualTo(2f), "tg's margin-bottom between stacked buttons");
            window.Close();
        });
    }
}
