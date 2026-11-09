#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui;
using Content.Client._KS14.Tgui.TextSelection;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Utility;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     The selectable text of tgui windows and the chat: that the layout it works out is the engine's, and that dragging over
///         text copies what was dragged over. How the highlight looks is for a real client.
/// </summary>
[TestOf(typeof(KsTguiTextSelectionUIController))]
public sealed class KsTguiTextSelectionTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private const string LabelText = "Hello selectable world";

    private async Task SetTheme(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);
    }

    private static Font FontOf(Control control)
    {
        return control.TryGetStyleProperty<Font>("font", out var font) ? font : IoCFont(control);
    }

    private static Font IoCFont(Control control)
    {
        // A control with no style of its own takes the theme's.
        var manager = Robust.Shared.IoC.IoCManager.Resolve<IUserInterfaceManager>();
        return control is Label ? manager.ThemeDefaults.LabelFont : manager.ThemeDefaults.DefaultFont;
    }

    private static Font LabelFont(Label label)
    {
        if (label.FontOverride != null)
            return label.FontOverride;

        return label.TryGetStyleProperty<Font>(Label.StylePropertyFont, out var font)
            ? font
            : Robust.Shared.IoC.IoCManager.Resolve<IUserInterfaceManager>().ThemeDefaults.LabelFont;
    }

    private static DefaultWindow OpenWindow(params Control[] contents)
    {
        var window = new DefaultWindow { Title = "Test" };
        foreach (var control in contents)
            window.Contents.AddChild(control);

        window.OpenCentered();
        return window;
    }

    // Layout parity.

    [TestCase("Hello selectable world")]
    [TestCase("one\ntwo lines\n\nand a gap")]
    [TestCase("日本語のテキストは折り返されません")]
    public async Task LabelLayoutMatchesTheEngine(string text)
    {
        await SetTheme(true);

        Label? label = null;
        DefaultWindow? window = null;
        await Client.WaitPost(() =>
        {
            label = new Label { Text = text };
            window = OpenWindow(label);
        });
        await Pair.RunTicksSync(5);

        await Client.WaitPost(() =>
        {
            var scale = label!.UIScale;
            var layout = KsTextLayouts.ForLabel(label, LabelFont(label))!;

            Assert.Multiple(() =>
            {
                Assert.That(layout.Width, Is.EqualTo(label.DesiredSize.X * scale).Within(0.6f), "width");
                Assert.That(layout.Height, Is.EqualTo(label.DesiredSize.Y * scale).Within(0.6f), "height");
                Assert.That(layout.Text, Is.EqualTo(text));
            });

            window!.Close();
        });
    }

    /// <summary>
    ///     Plain wrapping, bold and sized tags, CJK (no spaces to break at), and a link control, each at a spread of widths. The
    ///         line breaks decide the height and the widest line decides the width, so matching both at every width is matching
    ///         where the engine breaks.
    /// </summary>
    [TestCase("The quick brown fox jumps over the lazy dog and keeps running along the long wall")]
    [TestCase("Plain [bold]bold words that wrap around[/bold] and [italic]italic[/italic] ending [color=red]red text here[/color]")]
    [TestCase("Small [font size=9]tiny text in the middle[/font] then [font size=22]Big[/font] words after it")]
    [TestCase("日本語のテキストは、スペースがなくても、どこでも折り返されます。それはとても長い文章です。")]
    [TestCase("A line before\nand after a newline, with [cmdlink=\"Click here\" command=\"help\"] a link in it, and a hyphen-ated-word")]
    [TestCase("averyveryverylongwordwithnospacesatalltotestthebreakingofwordsbyletter and then some")]
    public async Task RichLayoutMatchesTheEngineAtEveryWidth(string markup)
    {
        await SetTheme(true);

        RichTextLabel? rich = null;
        DefaultWindow? window = null;
        await Client.WaitPost(() =>
        {
            rich = new RichTextLabel();
            rich.SetMessage(FormattedMessage.FromMarkupPermissive(markup), tagsAllowed: null);
            window = OpenWindow(rich);
        });
        await Pair.RunTicksSync(3);

        var tagManager = Client.ResolveDependency<MarkupTagManager>();
        await Client.WaitPost(() =>
        {
            var scale = rich!.UIScale;
            var font = FontOf(rich);
            var message = rich.GetFormattedMessage()!;

            var checkedWidths = 0;
            for (var width = 80f; width <= 420f; width += 13f)
            {
                rich.Measure(new Vector2(width, 100000f));
                var layout = KsTextLayouts.Rich(
                    message, rich, tagManager, null, font, width * scale, scale, rich.LineHeightScale, rich.Controls.ToList());

                Assert.Multiple(() =>
                {
                    Assert.That(layout.Height, Is.EqualTo(rich.DesiredPixelSize.Y).Within(0.6f), $"height at {width}");
                    // Measure clamps what the engine reports to the room it was given, which a word that cannot break can exceed.
                    Assert.That(Math.Min(layout.Width, width * scale), Is.EqualTo(rich.DesiredPixelSize.X).Within(1.01f), $"width at {width}");
                });
                checkedWidths++;
            }

            Assert.That(checkedWidths, Is.GreaterThan(20));
            window!.Close();
        });
    }

    // Selecting.

    [Test]
    public async Task DraggingOverALabelCopiesTheTextDraggedOver()
    {
        await SetTheme(true);

        Label? label = null;
        DefaultWindow? window = null;
        await Client.WaitPost(() =>
        {
            label = new Label { Text = LabelText };
            window = OpenWindow(label);
        });
        await Pair.RunTicksSync(5);

        var controller = Client.ResolveDependency<IUserInterfaceManager>().GetUIController<KsTguiTextSelectionUIController>();
        await Client.WaitPost(() =>
        {
            var layout = KsTextLayouts.ForLabel(label!, LabelFont(label!))!;
            var origin = (Vector2)label!.GlobalPixelPosition;
            Vector2 Over(int glyph) => origin + new Vector2(layout.Glyphs[glyph].Box.Left + 1f, layout.Glyphs[glyph].Box.Center.Y);

            Assert.That(controller.BeginSelection(new ScreenCoordinates(Over(6), WindowId.Main)), Is.True, "mouse down over the label");
            controller.DragTo(Over(17));
            controller.EndDrag();

            Assert.That(controller.SelectedText, Is.EqualTo(LabelText.Substring(6, 11)));
            Assert.That(controller.CopySelection(), Is.True);
            Assert.That(controller.LastCopiedText, Is.EqualTo("selectable "));

            // A click elsewhere lets go.
            Assert.That(controller.BeginSelection(new ScreenCoordinates(new Vector2(1f, 1f), WindowId.Main)), Is.False);
            Assert.That(controller.SelectedText, Is.Null);

            window!.Close();
        });
    }

    [Test]
    public async Task DoubleClickSelectsTheWordUnderTheCursor()
    {
        await SetTheme(true);

        Label? label = null;
        DefaultWindow? window = null;
        await Client.WaitPost(() =>
        {
            label = new Label { Text = LabelText };
            window = OpenWindow(label);
        });
        await Pair.RunTicksSync(5);

        var controller = Client.ResolveDependency<IUserInterfaceManager>().GetUIController<KsTguiTextSelectionUIController>();
        await Client.WaitPost(() =>
        {
            var layout = KsTextLayouts.ForLabel(label!, LabelFont(label!))!;
            var point = (Vector2)label!.GlobalPixelPosition + new Vector2(layout.Glyphs[8].Box.Center.X, layout.Glyphs[8].Box.Center.Y);
            var position = new ScreenCoordinates(point, WindowId.Main);

            controller.BeginSelection(position);
            controller.EndDrag();
            controller.BeginSelection(position);

            Assert.That(controller.SelectedText, Is.EqualTo("selectable"));
            window!.Close();
        });
    }

    [Test]
    public async Task RichTextAndWrappedLinesCopyAsOneRun()
    {
        await SetTheme(true);

        RichTextLabel? rich = null;
        DefaultWindow? window = null;
        const string text = "The quick brown fox jumps over the lazy dog and keeps running along the wall";
        await Client.WaitPost(() =>
        {
            rich = new RichTextLabel { MaxWidth = 150f };
            rich.SetMessage(FormattedMessage.FromMarkupPermissive("The quick [bold]brown fox[/bold] jumps over the lazy dog and keeps running along the wall"), tagsAllowed: null);
            window = OpenWindow(rich);
        });
        await Pair.RunTicksSync(5);

        var controller = Client.ResolveDependency<IUserInterfaceManager>().GetUIController<KsTguiTextSelectionUIController>();
        await Client.WaitPost(() =>
        {
            Assert.That(rich!.Height, Is.GreaterThan(FontOf(rich).GetLineHeight(rich.UIScale) / rich.UIScale * 1.5f), "the text wrapped");

            var origin = (Vector2)rich.GlobalPixelPosition;
            controller.BeginSelection(new ScreenCoordinates(origin + new Vector2(1f, 3f), WindowId.Main));
            controller.DragTo(origin + rich.PixelSize - new Vector2(1f, 1f));
            controller.EndDrag();

            // Soft wraps are not newlines, as in a browser.
            Assert.That(controller.SelectedText, Is.EqualTo(text));
            window!.Close();
        });
    }

    [Test]
    public async Task OutputPanelSelectsAcrossMessages()
    {
        await SetTheme(true);

        OutputPanel? panel = null;
        DefaultWindow? window = null;
        await Client.WaitPost(() =>
        {
            panel = new OutputPanel { MinSize = new Vector2(300f, 200f) };
            window = OpenWindow(panel);
            foreach (var line in new[] { "first message here", "second message goes here", "third and last message" })
                panel.AddMessage(FormattedMessage.FromMarkupPermissive(line), tagsAllowed: null);
        });
        await Pair.RunTicksSync(5);

        var controller = Client.ResolveDependency<IUserInterfaceManager>().GetUIController<KsTguiTextSelectionUIController>();
        await Client.WaitPost(() =>
        {
            var origin = (Vector2)panel!.GlobalPixelPosition;
            controller.BeginSelection(new ScreenCoordinates(origin + new Vector2(0.5f, 0.5f), WindowId.Main));
            controller.DragTo(origin + new Vector2(panel.PixelSize.X - 20f, 200f));
            controller.EndDrag();

            Assert.That(controller.SelectedText, Is.EqualTo("first message here\nsecond message goes here\nthird and last message"));
            window!.Close();
        });
    }

    [Test]
    public async Task ButtonTextIsNotSelectable()
    {
        await SetTheme(true);

        Button? button = null;
        DefaultWindow? window = null;
        await Client.WaitPost(() =>
        {
            button = new Button { Text = "Press this button" };
            window = OpenWindow(button);
        });
        await Pair.RunTicksSync(5);

        var controller = Client.ResolveDependency<IUserInterfaceManager>().GetUIController<KsTguiTextSelectionUIController>();
        await Client.WaitPost(() =>
        {
            var point = (Vector2)button!.GlobalPixelPosition + button.PixelSize / 2;
            Assert.That(controller.BeginSelection(new ScreenCoordinates(point, WindowId.Main)), Is.False);
            Assert.That(controller.SelectedText, Is.Null);
            window!.Close();
        });
    }

    [Test]
    public async Task TheTextCursorShowsOverSelectableTextOnly()
    {
        await SetTheme(true);

        Label? label = null;
        Button? button = null;
        DefaultWindow? window = null;
        await Client.WaitPost(() =>
        {
            label = new Label { Text = LabelText };
            button = new Button { Text = "Press this button" };
            var box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
            box.AddChild(label);
            box.AddChild(button);
            window = OpenWindow(box);
        });
        await Pair.RunTicksSync(5);

        var controller = Client.ResolveDependency<IUserInterfaceManager>().GetUIController<KsTguiTextSelectionUIController>();
        await Client.WaitPost(() =>
        {
            var overLabel = (Vector2)label!.GlobalPixelPosition + new Vector2(3f, label.PixelSize.Y / 2f);
            var overButton = (Vector2)button!.GlobalPixelPosition + button.PixelSize / 2;

            controller.UpdateCursor(overLabel);
            Assert.That(controller.ShowingTextCursor, Is.True, "over a label in a tgui window");

            controller.UpdateCursor(overButton);
            Assert.That(controller.ShowingTextCursor, Is.False, "over button text");

            controller.UpdateCursor(new Vector2(1f, 1f));
            Assert.That(controller.ShowingTextCursor, Is.False, "over nothing");
        });

        await SetTheme(false);
        await Client.WaitPost(() =>
        {
            controller.UpdateCursor((Vector2)label!.GlobalPixelPosition + new Vector2(3f, label.PixelSize.Y / 2f));
            Assert.That(controller.ShowingTextCursor, Is.False, "theme off");
            window!.Close();
        });
    }

    [Test]
    public async Task NothingIsSelectableWithTheThemeOff()
    {
        await SetTheme(false);

        Label? label = null;
        DefaultWindow? window = null;
        await Client.WaitPost(() =>
        {
            label = new Label { Text = LabelText };
            window = OpenWindow(label);
        });
        await Pair.RunTicksSync(5);

        var controller = Client.ResolveDependency<IUserInterfaceManager>().GetUIController<KsTguiTextSelectionUIController>();
        await Client.WaitPost(() =>
        {
            var point = (Vector2)label!.GlobalPixelPosition + new Vector2(3f, label.PixelSize.Y / 2f);
            Assert.That(controller.BeginSelection(new ScreenCoordinates(point, WindowId.Main)), Is.False);
            controller.DragTo(point + new Vector2(60f, 0f));
            Assert.That(controller.SelectedText, Is.Null);
            window!.Close();
        });
    }
}
