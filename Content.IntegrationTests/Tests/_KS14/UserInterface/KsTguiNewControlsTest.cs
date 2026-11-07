using System;
using System.Collections.Generic;
using System.Reflection;
using System.Numerics;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui;
using Content.Client._KS14.Tgui.Controls;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     The tgui-core components KS14 had no counterpart for: their sizes, layout, state and value logic. Nothing headless
///         draws, so how any of it looks is for a real client.
/// </summary>
[TestOf(typeof(KsTguiLabeledControls))]
public sealed class KsTguiNewControlsTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static void Layout(Control control, Vector2 size)
    {
        control.Measure(size);
        control.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
    }

    private static Control Block(float width, float height)
    {
        return new Control { MinSize = new Vector2(width, height) };
    }

    [Test]
    public async Task LabeledControlsSpreadItemsBetweenTheEdges()
    {
        await Client.WaitPost(() =>
        {
            var row = new KsTguiLabeledControls();
            var first = row.AddItem("A", Block(40f, 20f));
            var second = row.AddItem("B", Block(40f, 20f));
            var third = row.AddItem("C", Block(40f, 20f));

            Layout(row, new Vector2(300f, 80f));

            // The row's -3px margin and an item's 6px leave 3px at each edge, and the gaps are shared out equally.
            Assert.That(first.Position.X, Is.EqualTo(3f).Within(0.01f), "First item sits 3px in.");
            Assert.That(third.Position.X + third.Size.X, Is.EqualTo(297f).Within(0.01f), "Last item ends 3px short of the edge.");

            var gapOne = second.Position.X - (first.Position.X + first.Size.X);
            var gapTwo = third.Position.X - (second.Position.X + second.Size.X);
            Assert.That(gapOne, Is.EqualTo(gapTwo).Within(0.01f));
            Assert.That(gapOne, Is.GreaterThan(12f), "Space-between puts the leftover room between the items, over their margins.");

            Assert.That(first.Size.Y, Is.EqualTo(80f), "Items stretch to the row's height.");
        });
    }

    [Test]
    public async Task LabeledControlsItemPutsTheLabelAtTheBottomAndTheContentHalfwayUp()
    {
        await Client.WaitPost(() =>
        {
            var content = Block(40f, 20f);
            var item = new KsTguiLabeledControlsItem { Text = "Label", Content = content };

            Layout(item, new Vector2(100f, 100f));

            var label = (Label)item.GetChild(0);
            Assert.That(label.Position.Y + label.Size.Y, Is.EqualTo(100f).Within(0.01f), "The label is at the bottom.");

            var expectedTop = KsTguiLabeledControlsItem.ContentTop(100f, 20f, label.DesiredSize.Y);
            Assert.That(content.Position.Y, Is.EqualTo(expectedTop).Within(0.01f));
            Assert.That(content.Position.Y, Is.EqualTo((100f - 20f - label.DesiredSize.Y) / 2f).Within(0.01f), "Halfway between the top and the label.");
            Assert.That(content.Position.X, Is.EqualTo(30f).Within(0.01f), "Centred.");
        });
    }

    [Test]
    public async Task LabeledControlsWrapStartsANewLine()
    {
        await Client.WaitPost(() =>
        {
            var row = new KsTguiLabeledControls { Wrap = true };
            var first = row.AddItem("A", Block(60f, 20f));
            var second = row.AddItem("B", Block(60f, 20f));

            // Each item is 60 + 12 wide; two do not fit 100.
            Layout(row, new Vector2(100f, 200f));
            Assert.That(second.Position.Y, Is.GreaterThan(first.Position.Y), "The second item went to the next line.");

            Layout(row, new Vector2(200f, 200f));
            Assert.That(second.Position.Y, Is.EqualTo(first.Position.Y), "Both fit one line at 200.");
        });
    }

    [Test]
    public async Task DialogHasTguiDimensions()
    {
        await Client.WaitPost(() =>
        {
            var dialog = new KsTguiDialog { Title = "Title" };
            var button = dialog.AddButton("OK");
            var other = dialog.AddButton("Cancel");
            dialog.Body = Block(50f, 40f);

            Layout(dialog, new Vector2(800f, 600f));

            Assert.That(dialog.Panel.Size.X, Is.EqualTo(370f), "The default width.");
            Assert.That(dialog.Panel.Position.X, Is.EqualTo(215f).Within(0.01f), "Centred.");
            Assert.That(dialog.Panel.Position.Y + dialog.Panel.Size.Y / 2f, Is.EqualTo(300f).Within(0.5f));

            Assert.That(button.DesiredSize.X, Is.GreaterThanOrEqualTo(72f).Within(0.01f), "Dialog.Button is at least 6rem wide.");
            Assert.That(button.Size.Y, Is.EqualTo(24f).Within(0.01f).Or.GreaterThan(24f), "...and 2rem tall.");

            // The footer's buttons sit at the right with 14px margins and 14px of padding.
            var panelRight = dialog.Panel.Position.X + dialog.Panel.Size.X;
            Assert.That(panelRight - (other.GlobalPosition.X + other.Size.X), Is.EqualTo(28f).Within(0.5f), "Pushed to the right, 1em margin plus 1em padding.");
            Assert.That(other.GlobalPosition.X - (button.GlobalPosition.X + button.Size.X), Is.EqualTo(28f).Within(0.5f), "Each has 14px either side.");
            Assert.That(other.GlobalPosition.X, Is.GreaterThan(button.GlobalPosition.X + button.Size.X));

            // Fresh dialogs: one outside the UI tree only queues its invalidation up to its parents, so laying the same root
            //      out again would reuse its measure.
            var wide = new KsTguiDialog { DialogWidth = 500f, Body = Block(50f, 40f) };
            Layout(wide, new Vector2(800f, 600f));
            Assert.That(wide.Panel.Size.X, Is.EqualTo(500f));

            var tall = new KsTguiDialog { DialogHeight = 300f, Body = Block(50f, 40f) };
            Layout(tall, new Vector2(800f, 600f));
            Assert.That(tall.Panel.Size.Y, Is.EqualTo(300f));
        });
    }

    [Test]
    public async Task DialogBodyHasItsMarginAndHeaderItsHeight()
    {
        await Client.WaitPost(() =>
        {
            var dialog = new KsTguiDialog();
            var body = Block(50f, 40f);
            dialog.Body = body;

            Layout(dialog, new Vector2(800f, 600f));

            Assert.That(body.GlobalPosition.X - dialog.Panel.Position.X, Is.EqualTo(14f).Within(0.01f), "margin: 2em 1em");
            Assert.That(dialog.CloseButton.GlobalPosition.Y - dialog.Panel.Position.Y, Is.LessThan(KsTguiDialog.HeaderHeight));
            Assert.That(body.GlobalPosition.Y - dialog.Panel.Position.Y, Is.EqualTo(KsTguiDialog.HeaderHeight + 28f).Within(0.5f),
                "The body starts under the 28px header and its 28px margin.");
        });
    }

    [Test]
    public async Task DialogCloseButtonRaisesClosed()
    {
        await Client.WaitPost(() =>
        {
            var dialog = new KsTguiDialog();
            var closed = 0;
            dialog.OnClose += () => closed++;

            var onPressed = (Action<BaseButton.ButtonEventArgs>)typeof(BaseButton)
                .GetField("OnPressed", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(dialog.CloseButton)!;
            onPressed(new BaseButton.ButtonEventArgs(dialog.CloseButton, new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick,
                BoundKeyState.Down, default, false, default, default)));

            Assert.That(closed, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RestrictedInputIsRedOnlyOutOfRange()
    {
        await Client.WaitPost(() =>
        {
            var input = new KsTguiRestrictedInput { MinValue = 0f, MaxValue = 100f };
            Assert.That(input.CurrentBorderColor, Is.EqualTo(KsTguiRestrictedInput.BorderIdle), "Green to begin with.");

            var validity = new List<bool>();
            input.OnValidationChanged += validity.Add;

            input.SetText("500", invokeEvent: true);
            Assert.That(input.Value, Is.EqualTo(500f));
            Assert.That(input.IsInvalid);
            Assert.That(input.CurrentBorderColor, Is.EqualTo(KsTguiRestrictedInput.BorderInvalid));

            input.SetText("50", invokeEvent: true);
            Assert.That(input.IsInvalid, Is.False);
            Assert.That(input.CurrentBorderColor, Is.EqualTo(KsTguiRestrictedInput.BorderIdle));

            Assert.That(validity, Is.EqualTo(new[] { false, true }));
        });
    }

    [Test]
    public void RestrictedInputBorderAndValidityRules()
    {
        Assert.That(KsTguiRestrictedInput.BorderFor(focused: false, invalid: false), Is.EqualTo(KsTguiRestrictedInput.BorderIdle));
        Assert.That(KsTguiRestrictedInput.BorderFor(focused: true, invalid: false), Is.EqualTo(KsTguiRestrictedInput.BorderFocused));
        Assert.That(KsTguiRestrictedInput.BorderFocused, Is.Not.EqualTo(KsTguiRestrictedInput.BorderIdle));
        Assert.That(KsTguiRestrictedInput.BorderFor(focused: true, invalid: true), Is.EqualTo(KsTguiRestrictedInput.BorderInvalid), "Red focused too.");
        Assert.That(KsTguiRestrictedInput.BorderFor(focused: false, invalid: true), Is.EqualTo(KsTguiRestrictedInput.BorderInvalid));

        Assert.That(KsTguiRestrictedInput.IsValidNumber(5f, 0f, 10f, allowFloats: false));
        Assert.That(KsTguiRestrictedInput.IsValidNumber(11f, 0f, 10f, allowFloats: false), Is.False);
        Assert.That(KsTguiRestrictedInput.IsValidNumber(-1f, 0f, 10f, allowFloats: false), Is.False);
        Assert.That(KsTguiRestrictedInput.IsValidNumber(2.5f, 0f, 10f, allowFloats: false), Is.False, "Off the whole step.");
        Assert.That(KsTguiRestrictedInput.IsValidNumber(2.5f, 0f, 10f, allowFloats: true));
        Assert.That(KsTguiRestrictedInput.IsValidNumber(1.5f, 0.5f, 10f, allowFloats: false), Is.True, "Steps count from the minimum.");
    }

    [Test]
    public async Task RestrictedInputMinusNegatesAndTypingIsFiltered()
    {
        await Client.WaitPost(() =>
        {
            var input = new KsTguiRestrictedInput { MinValue = -100f, MaxValue = 100f };
            var changes = new List<float>();
            input.OnValueChanged += changes.Add;

            input.SetText("12", invokeEvent: true);
            input.Negate();
            Assert.That(input.Text, Is.EqualTo("-12"));
            Assert.That(input.Value, Is.EqualTo(-12f));
            input.Negate();
            Assert.That(input.Text, Is.EqualTo("12"));
            Assert.That(changes, Is.EqualTo(new[] { 12f, -12f, 12f }));

            input.SetText("0", invokeEvent: true);
            input.Negate();
            Assert.That(input.Text, Is.EqualTo("0"), "Zero has no sign.");

            // The filter the box applies to what is typed.
            Assert.That(input.IsValid("123"));
            Assert.That(input.IsValid("12a"), Is.False);
            Assert.That(input.IsValid("1.5"), Is.False, "No point without allowFloats.");
            input.AllowFloats = true;
            Assert.That(input.IsValid("1.5"));
            Assert.That(input.IsValid("1.5.5"), Is.False);
        });
    }

    [Test]
    public async Task RestrictedInputTextIsRightAligned()
    {
        await Client.WaitPost(() =>
        {
            var input = new KsTguiRestrictedInput();
            input.SetSize = new Vector2(200f, 20f);
            Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.AddChild(input);
            Layout(input, new Vector2(200f, 20f));
            input.ForceRunStyleUpdate();

            input.SetText("1", invokeEvent: true);
            var shortMargin = input.TextLeftMargin;
            input.SetText("1000", invokeEvent: true);
            var longMargin = input.TextLeftMargin;

            Assert.That(shortMargin, Is.GreaterThan(150f), "A short number is pushed to the right.");
            Assert.That(longMargin, Is.LessThan(shortMargin), "A longer one takes more of the room.");
            Assert.That(longMargin, Is.GreaterThanOrEqualTo(5f));
            input.Orphan();
        });
    }

    [TestCase(0f, "00:00:00")]
    [TestCase(10f, "00:00:01")]
    [TestCase(9f, "00:00:00")]
    [TestCase(610f, "00:01:01")]
    [TestCase(36610f, "01:01:01")]
    public void TimeDisplayDefaultFormat(float deciseconds, string expected)
    {
        Assert.That(KsTguiTimeDisplay.FormatTime(deciseconds), Is.EqualTo(expected));
    }

    [TestCase(36610f, "1h1m1s")]
    [TestCase(600f, "1m")]
    [TestCase(30f, "3s")]
    [TestCase(0f, "")]
    public void TimeDisplayShortFormat(float deciseconds, string expected)
    {
        Assert.That(KsTguiTimeDisplay.FormatTime(deciseconds, shortFormat: true), Is.EqualTo(expected));
    }

    [Test]
    public async Task TimeDisplayCountsAndRestartsOnANewValue()
    {
        await Client.WaitPost(() =>
        {
            var display = new KsTguiTimeDisplay { Value = 30f, Auto = KsTguiTimeAuto.Down };
            Assert.That(display.Text, Is.EqualTo("00:00:03"));

            display.Tick();
            Assert.That(display.Text, Is.EqualTo("00:00:02"));
            display.Tick();
            display.Tick();
            display.Tick();
            Assert.That(display.Shown, Is.EqualTo(0f), "Counting down stops at zero.");

            // The same value again does not restart; another does.
            display.Value = 30f;
            Assert.That(display.Shown, Is.EqualTo(0f));
            display.Value = 50f;
            Assert.That(display.Text, Is.EqualTo("00:00:05"));

            display.Auto = KsTguiTimeAuto.Up;
            display.Tick();
            Assert.That(display.Text, Is.EqualTo("00:00:06"));

            display.Auto = KsTguiTimeAuto.None;
            display.Tick();
            Assert.That(display.Text, Is.EqualTo("00:00:06"), "Without auto nothing counts.");

            display.Format = deciseconds => $"T{deciseconds}";
            display.Value = 70f;
            Assert.That(display.Text, Is.EqualTo("T70"), "A custom format replaces the default.");
        });
    }

    [Test]
    public void BlinkFollowsTgui()
    {
        // Visible for the first interval + time, then hidden for `time` out of every period.
        Assert.That(KsTguiBlink.IsHidden(0f, 1000, 1000), Is.False);
        Assert.That(KsTguiBlink.IsHidden(1.5f, 1000, 1000), Is.False, "The first timer fires after 2 seconds.");
        Assert.That(KsTguiBlink.IsHidden(2.5f, 1000, 1000), Is.True);
        Assert.That(KsTguiBlink.IsHidden(3.5f, 1000, 1000), Is.False);
        Assert.That(KsTguiBlink.IsHidden(4.5f, 1000, 1000), Is.True);

        // Visible 200ms, hidden 100ms: period 0.3.
        Assert.That(KsTguiBlink.IsHidden(0.35f, 200, 100), Is.True);
        Assert.That(KsTguiBlink.IsHidden(0.45f, 200, 100), Is.False);
    }

    [Test]
    public async Task BlinkHidesItsContentAndKeepsItsRoom()
    {
        await Client.WaitPost(() =>
        {
            var blink = new KsTguiBlink { Interval = 100, Time = 100 };
            var content = Block(30f, 10f);
            blink.AddChild(content);

            blink.Advance(0.1f);
            Assert.That(blink.Hidden, Is.False);
            blink.Advance(0.15f);
            Assert.That(blink.Hidden, Is.True);

            Layout(blink, new Vector2(100f, 100f));
            Assert.That(blink.DesiredSize, Is.EqualTo(new Vector2(30f, 10f)), "Hidden content still takes its room.");

            blink.Advance(0.1f);
            Assert.That(blink.Hidden, Is.False);

            blink.Advance(0.1f);
            blink.Interval = 500;
            Assert.That(blink.Hidden, Is.False, "A new interval restarts the timer.");
        });
    }

    [TestCase(10, 100, 20, 5, 11)]
    [TestCase(10, 100, 20, 0, 10)]
    [TestCase(3, 100, 50, 5, 34)]
    [TestCase(3, 100, 50, 1, 34)]
    [TestCase(0, 100, 50, 5, 50)]
    [TestCase(10, 100, 0, 5, 0)]
    [TestCase(6, 100, 12, 5, 12)]
    public void FitTextSearchMatchesTgui(int widthPerSize, float maxWidth, float maxFontSize, float acceptable, int expected)
    {
        // Expected values were run through tgui-core's FitText.resize loop.
        var size = KsTguiFitText.Fit(fontSize => (float)(widthPerSize * fontSize), maxWidth, maxFontSize, acceptable);
        Assert.That(size, Is.EqualTo(expected));
    }

    [Test]
    public async Task FitTextShrinksALongTextToItsWidth()
    {
        await Client.WaitPost(() =>
        {
            var fit = new KsTguiFitText { Text = "A fairly long line of text", MaxFontSize = 40f, TextMaxWidth = 400f };
            var roomy = fit.FitFontSize;
            Assert.That(roomy, Is.GreaterThan(12), "A wide limit lets it grow.");

            fit.TextMaxWidth = 100f;
            Assert.That(fit.FitFontSize, Is.LessThan(roomy));
            Assert.That(fit.FitFontSize, Is.GreaterThan(0));

            fit.TextMaxWidth = 100f;
            Layout(fit, new Vector2(1000f, 100f));
            Assert.That(fit.DesiredSize.X, Is.LessThan(125f), "The text came out about the limit, not far over.");

            var before = fit.FitFontSize;
            fit.Text = "Short";
            Assert.That(fit.FitFontSize, Is.GreaterThan(before), "A new text searches again.");
        });
    }

    [Test]
    public async Task StackDividerTakesTwoPixelsAndNoMargin()
    {
        await Client.WaitPost(() =>
        {
            var stackDivider = new KsTguiStackDivider();
            var divider = new KsTguiDivider();
            stackDivider.Measure(new Vector2(100f, 100f));
            divider.Measure(new Vector2(100f, 100f));

            Assert.That(stackDivider.DesiredSize.Y, Is.EqualTo(2f));
            Assert.That(divider.DesiredSize.Y, Is.EqualTo(14f));

            // In a stack with tgui's 6px gap both come to 14px between the neighbours.
            var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };
            column.AddChild(Block(10f, 10f));
            column.AddChild(stackDivider);
            column.AddChild(Block(10f, 10f));
            column.Measure(new Vector2(100f, 100f));
            Assert.That(column.DesiredSize.Y, Is.EqualTo(10f + 6f + 2f + 6f + 10f));

            stackDivider.Hidden = true;
            column.InvalidateMeasure();
            column.Measure(new Vector2(100f, 101f));
            Assert.That(stackDivider.DesiredSize.Y, Is.EqualTo(0f), "Hidden has no line.");
            Assert.That(column.DesiredSize.Y, Is.EqualTo(10f + 6f + 0f + 6f + 10f), "Hidden keeps the gaps, not the line.");

            stackDivider.Hidden = false;
            stackDivider.Vertical = true;
            stackDivider.Measure(new Vector2(100f, 100f));
            Assert.That(stackDivider.DesiredSize.X, Is.EqualTo(2f));
        });
    }

    [Test]
    public void ChartLinePointsScaleToTheDataAndFlip()
    {
        var data = new[] { new Vector2(0f, 0f), new Vector2(10f, 5f), new Vector2(20f, 10f) };

        var points = KsTguiChartLine.ComputePoints(data, new Vector2(100f, 50f), null, null, 2f);

        // Three data points, then the four that close the polygon off the box.
        Assert.That(points, Has.Count.EqualTo(7));
        Assert.That(points[0], Is.EqualTo(new Vector2(0f, 50f)), "The lowest value is at the bottom.");
        Assert.That(points[1], Is.EqualTo(new Vector2(50f, 25f)));
        Assert.That(points[2], Is.EqualTo(new Vector2(100f, 0f)));
        Assert.That(points[3], Is.EqualTo(new Vector2(102f, 0f)));
        Assert.That(points[4], Is.EqualTo(new Vector2(102f, 52f)));
        Assert.That(points[5], Is.EqualTo(new Vector2(-2f, 52f)));
        Assert.That(points[6], Is.EqualTo(new Vector2(-2f, 50f)));

        // A range wider than the data leaves room, and a flat axis is not a number.
        var ranged = KsTguiChartLine.ComputePoints(data, new Vector2(100f, 50f), (0f, 40f), (0f, 20f), 2f);
        Assert.That(ranged[2], Is.EqualTo(new Vector2(50f, 25f)));

        var flat = KsTguiChartLine.ComputePoints(new[] { new Vector2(1f, 3f), new Vector2(2f, 3f) }, new Vector2(100f, 50f), null, null, 2f);
        Assert.That(float.IsFinite(flat[0].Y), Is.False);

        Assert.That(KsTguiChartLine.ComputePoints(new List<Vector2>(), new Vector2(100f, 50f), null, null, 2f), Is.Empty);
    }

    [Test]
    public async Task MenuBarKeepsTgusOpenAndHoverState()
    {
        await Client.WaitPost(() =>
        {
            var bar = new KsTguiMenuBar();
            var file = bar.AddDropdown("File");
            var edit = bar.AddDropdown("Edit");
            var off = bar.AddDropdown("Off");
            off.Disabled = true;
            Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.AddChild(bar);

            try
            {
                bar.HoverDropdown(edit);
                Assert.That(bar.OpenDropdown, Is.Null, "Hovering opens nothing until a click turned hover-opening on.");

                bar.ClickDropdown(file);
                Assert.That(bar.OpenDropdown, Is.SameAs(file));
                Assert.That(bar.OpenOnHover);
                Assert.That(file.Menu.IsOpen);

                bar.HoverDropdown(edit);
                Assert.That(bar.OpenDropdown, Is.SameAs(edit), "Hovering another button switches.");
                Assert.That(file.Menu.IsOpen, Is.False);
                Assert.That(edit.Menu.IsOpen);

                bar.HoverDropdown(off);
                Assert.That(bar.OpenDropdown, Is.SameAs(edit), "A disabled one does not open.");

                bar.ClickDropdown(edit);
                Assert.That(bar.OpenDropdown, Is.Null, "Clicking the open one closes it.");
                Assert.That(bar.OpenOnHover, Is.False);

                bar.ClickDropdown(file);
                bar.DismissOutside();
                Assert.That(bar.OpenDropdown, Is.Null);
                Assert.That(bar.OpenOnHover, Is.False);
                Assert.That(file.Menu.IsOpen, Is.False);
            }
            finally
            {
                bar.Orphan();
            }
        });
    }

    [Test]
    public async Task MenuBarEntriesHaveTgusSizes()
    {
        await Client.WaitPost(() =>
        {
            var bar = new KsTguiMenuBar();
            var dropdown = bar.AddDropdown("File");
            var plain = dropdown.AddItem("Open");
            var toggle = dropdown.AddToggle("Grid", isChecked: true);
            dropdown.AddSeparator();
            var submenu = dropdown.AddSubmenu("More");

            bar.Measure(new Vector2(500f, 100f));
            Assert.That(dropdown.DesiredSize.Y, Is.EqualTo(17f + 6f), "A 17px line with 3px of padding.");

            dropdown.Menu.Measure(new Vector2(500f, 500f));
            Assert.That(plain.DesiredSize.Y, Is.EqualTo(25f));
            Assert.That(plain.TextLeft, Is.EqualTo(36f));
            Assert.That(toggle.TextLeft, Is.EqualTo(30f), "A toggle's check cell is 2.5rem.");
            Assert.That(toggle.Checked);
            Assert.That(submenu.DesiredSize.X, Is.GreaterThan(plain.TextLeft + 12f));

            foreach (var entry in dropdown.Menu.Entries)
            {
                if (entry is KsTguiMenuBarSeparator separator)
                    Assert.That(separator.DesiredSize.Y, Is.EqualTo(9f), "1px line with 4px margins.");
            }

            dropdown.Menu.OpenWidth = 400f;
            dropdown.Menu.InvalidateMeasure();
            dropdown.Menu.Measure(new Vector2(800f, 800f));
            Assert.That(dropdown.Menu.DesiredSize.X, Is.GreaterThanOrEqualTo(400f), "openWidth is the menu's width.");
        });
    }

    [Test]
    public async Task MenuBarItemsClickHoverAndFade()
    {
        await Client.WaitPost(() =>
        {
            var item = new KsTguiMenuBarItem { Text = "Open", Value = 7 };
            var clicks = new List<object> { };
            item.Clicked += value => clicks.Add(value);

            item.Activate();
            Assert.That(clicks, Is.EqualTo(new object[] { 7 }));

            item.Disabled = true;
            item.Activate();
            Assert.That(clicks, Has.Count.EqualTo(1), "A disabled item does nothing.");
            item.SetHovered(true);
            Assert.That(item.Hovered, Is.False);
            item.Disabled = false;

            item.SetHovered(true);
            Assert.That(item.Fade, Is.EqualTo(1f), "The wash comes on at once.");
            item.AdvanceFade(0.1f);
            Assert.That(item.Fade, Is.EqualTo(1f), "...and stays while hovered.");

            item.SetHovered(false);
            item.AdvanceFade(0.1f);
            Assert.That(item.Fade, Is.EqualTo(0.5f).Within(0.001f), "...then fades out over 0.2 seconds.");
            item.AdvanceFade(0.2f);
            Assert.That(item.Fade, Is.EqualTo(0f));
        });
    }

    [Test]
    public async Task MenuBarSubmenuOpensAndClosesAfterTheHoverDelay()
    {
        await Client.WaitPost(() =>
        {
            var bar = new KsTguiMenuBar();
            var dropdown = bar.AddDropdown("File");
            var submenu = dropdown.AddSubmenu("More");
            submenu.AddItem("Inner");
            Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.AddChild(bar);
            bar.Measure(new Vector2(500f, 100f));
            bar.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(500f, 100f)));
            dropdown.Menu.Measure(new Vector2(500f, 500f));

            try
            {
                submenu.AdvanceHover(0.2f, overItem: true, overMenu: false);
                Assert.That(submenu.IsOpen, Is.False, "Not yet: 0.25 seconds of hover first.");

                submenu.AdvanceHover(0.1f, overItem: true, overMenu: false);
                Assert.That(submenu.IsOpen);

                submenu.AdvanceHover(1f, overItem: false, overMenu: true);
                Assert.That(submenu.IsOpen, Is.True, "Over the submenu itself keeps it open.");

                submenu.AdvanceHover(0.2f, overItem: false, overMenu: false);
                Assert.That(submenu.IsOpen, Is.True, "Closing waits as long as opening does.");
                submenu.AdvanceHover(0.1f, overItem: false, overMenu: false);
                Assert.That(submenu.IsOpen, Is.False);
            }
            finally
            {
                submenu.Menu.Close();
                bar.Orphan();
            }
        });
    }
}
