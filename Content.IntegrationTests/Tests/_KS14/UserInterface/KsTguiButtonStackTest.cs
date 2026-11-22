using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui;
using Content.Client.Communications.UI;
using Content.Client.SurveillanceCamera.UI;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     Buttons stacked in a vertical box touched under tgui: a box's separation is 0 and the sheet gives a plain button no
///         bottom margin. <see cref="KsTguiButtonStackSelector"/> gives a button in such a box tg's margin-bottom, and
///         <see cref="KsTguiSheetlet.StyleClassButtonStack"/> spaces a box whose buttons set their own margins. Both under tgui
///         only: Baron, whose framed buttons look apart already, is left as it was.
/// </summary>
[TestOf(typeof(KsTguiSheetlet))]
public sealed class KsTguiButtonStackTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static IEnumerable<T> Descendants<T>(Control root) where T : Control
    {
        foreach (var child in root.Children)
        {
            if (child is T match)
                yield return match;

            foreach (var nested in Descendants<T>(child))
                yield return nested;
        }
    }

    private static float Gap(Control above, Control below)
    {
        return below.GlobalPosition.Y - (above.GlobalPosition.Y + above.Size.Y);
    }

    private async Task<float> GapIn(bool tgui, System.Func<BaseWindow> open, System.Func<BaseWindow, (Control Above, Control Below)> pick)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);

        BaseWindow window = null;
        await Client.WaitPost(() =>
        {
            window = open();
            window.OpenCentered();
        });
        await Pair.RunTicksSync(5);

        var gap = 0f;
        await Client.WaitPost(() =>
        {
            var (above, below) = pick(window);
            gap = Gap(above, below);
            window.Close();
        });

        return gap;
    }

    private static Button ButtonWithText(Control root, string locId)
    {
        var text = Robust.Shared.Localization.Loc.GetString(locId);
        return Descendants<Button>(root).First(button => button.Text == text);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task CommunicationsConsoleButtonsAreSpacedUnderTgui(bool tgui)
    {
        var gap = await GapIn(tgui,
            () => new CommunicationsConsoleMenu(),
            window => (((CommunicationsConsoleMenu)window).AnnounceButton, ((CommunicationsConsoleMenu)window).BroadcastButton));

        if (tgui)
            Assert.That(gap, Is.EqualTo(2f).Within(0.01f), "tg's 2px between stacked buttons.");
        else
            Assert.That(gap, Is.EqualTo(0f).Within(0.01f), "Baron keeps its layout.");
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task CameraMonitorButtonsAreSpacedUnderTgui(bool tgui)
    {
        var gap = await GapIn(tgui,
            () => new SurveillanceCameraMonitorWindow(),
            window => (Descendants<OptionButton>(window).First(), ButtonWithText(window, "surveillance-camera-monitor-ui-refresh-subnets")));

        if (tgui)
            Assert.That(gap, Is.EqualTo(2f).Within(0.01f), "tg's 2px between stacked buttons.");
        else
            Assert.That(gap, Is.EqualTo(0f).Within(0.01f), "Baron keeps its layout.");
    }

    private async Task SetTheme(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);
    }

    /// <summary>
    ///     A window filled from code after it opened, as most lists are: each button is styled as it is parented.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task ButtonsAddedToAnOpenWindowAreSpaced(bool tgui)
    {
        await SetTheme(tgui);

        DefaultWindow window = null;
        BoxContainer column = null;
        await Client.WaitPost(() =>
        {
            window = new DefaultWindow();
            column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
            window.Contents.AddChild(column);
            window.OpenCentered();
        });
        await Pair.RunTicksSync(2);

        await Client.WaitPost(() =>
        {
            for (var i = 0; i < 3; i++)
                column.AddChild(new Button { Text = $"Row {i}" });

            column.AddChild(new OptionButton());
        });
        await Pair.RunTicksSync(5);

        await Client.WaitAssertion(() =>
        {
            var gaps = new List<float>();
            for (var i = 0; i < column.ChildCount - 1; i++)
                gaps.Add(Gap(column.GetChild(i), column.GetChild(i + 1)));

            Assert.That(gaps, Has.All.EqualTo(tgui ? 2f : 0f).Within(0.01f), $"Gaps {string.Join(", ", gaps)}.");
            window.Close();
        });
    }

    /// <summary>
    ///     tg's margin-bottom in a row would leave a stretched button 2px shorter than the input beside it; a box that sets its
    ///         own separation keeps the gap its author chose.
    /// </summary>
    [Test]
    public async Task RowsAndSpacedBoxesAreLeftAlone()
    {
        await SetTheme(true);

        DefaultWindow window = null;
        Button inRow = null;
        LineEdit input = null;
        BoxContainer spaced = null;
        await Client.WaitPost(() =>
        {
            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
            row.AddChild(input = new LineEdit { HorizontalExpand = true });
            row.AddChild(inRow = new Button { Text = "Go" });
            spaced = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };
            spaced.AddChild(new Button { Text = "A" });
            spaced.AddChild(new Button { Text = "B" });
            var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 10 };
            root.AddChild(row);
            root.AddChild(spaced);
            window = new DefaultWindow();
            window.Contents.AddChild(root);
            window.OpenCentered();
        });
        await Pair.RunTicksSync(5);

        await Client.WaitAssertion(() =>
        {
            Assert.That(inRow.Margin.Bottom, Is.Zero, "A button in a row gets no bottom margin.");
            Assert.That(inRow.Size.Y, Is.EqualTo(input.Size.Y).Within(0.01f), "It stays as tall as the input beside it.");
            Assert.That(Gap(spaced.GetChild(0), spaced.GetChild(1)), Is.EqualTo(4f).Within(0.01f), "A box's own separation is kept.");
            window.Close();
        });
    }
}
