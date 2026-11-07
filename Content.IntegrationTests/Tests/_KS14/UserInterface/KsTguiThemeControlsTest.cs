using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui;
using Content.Client.Stylesheets.Fonts;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Robust.Client.Graphics;
using Robust.Shared.Maths;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     The theme-level engine controls under tgui: the scrollbar, fonts, input and dropdown.
/// </summary>
public sealed class KsTguiThemeControlsTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private IUserInterfaceManager UiManager => Client.ResolveDependency<IUserInterfaceManager>();

    private KsTguiFeelUIController Feel => UiManager.GetUIController<KsTguiFeelUIController>();

    private async Task SetTheme(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);
    }

    private static void Press(BaseButton button)
    {
        var field = typeof(BaseButton).GetField(nameof(BaseButton.OnPressed), BindingFlags.Instance | BindingFlags.NonPublic);
        var handler = (Action<BaseButton.ButtonEventArgs>?)field!.GetValue(button);
        handler?.Invoke(new BaseButton.ButtonEventArgs(button, null!));
    }

    // Fonts

    [TestCase(12, 9, 1f)]
    [TestCase(16, 12, 1f)]
    [TestCase(14, 21, 0.5f)]
    [TestCase(18, 27, 0.5f)]
    [TestCase(13, 39, 0.25f)]
    [TestCase(15, 45, 0.25f)]
    public void EverySizeIsExact(int pixels, int points, float factor)
    {
        var (actualPoints, actualFactor) = KsTguiFonts.Quantize(pixels);

        Assert.That(actualPoints, Is.EqualTo(points));
        Assert.That(actualFactor, Is.EqualTo(factor));

        // Points at 96 DPI are three quarters of a pixel, drawn at the factor of the DPI.
        Assert.That((float)actualPoints * actualFactor * 96f / 72f, Is.EqualTo((float)pixels).Within(0.0001f));
    }

    [Test]
    public async Task FontIsDrawnAtAFractionOfTheScale()
    {
        var fonts = Client.ResolveDependency<KsTguiFonts>();

        await Client.WaitAssertion(() =>
        {
            var font = (StackedFont)fonts.GetFont(14);
            var main = (KsTguiScaledFont)font._main;

            Assert.That(((VectorFont)main.Inner).Size, Is.EqualTo(21));
            Assert.That(main.Factor, Is.EqualTo(0.5f));

            Assert.That(font.GetHeight(1f), Is.GreaterThan(fonts.GetFont(12).GetHeight(1f)));
        });
    }

    /// <summary>
    ///     A browser's line box is the font's line height, not the sum of its rounded-up ascent and rounded-down descent.
    /// </summary>
    [Test]
    public async Task LineBoxIsTheFontsLineHeight()
    {
        var fonts = Client.ResolveDependency<KsTguiFonts>();

        await Client.WaitAssertion(() =>
        {
            var font = (StackedFont)fonts.GetFont(12);
            var main = (KsTguiScaledFont)font._main;
            var inner = main.Inner;

            Assert.That(font.GetHeight(1f), Is.EqualTo(Math.Min(inner.GetHeight(1f), inner.GetLineHeight(1f))));
            Assert.That(font.GetAscent(1f) + font.GetDescent(1f), Is.EqualTo(font.GetHeight(1f)), "The baseline has to stay inside the box");
        });
    }

    // Scrollbar

    private ScrollContainer MakeScroll()
    {
        var scroll = new ScrollContainer
        {
            HScrollEnabled = false,
            MinSize = new Vector2(100, 100),
            MaxSize = new Vector2(100, 100),
            Children = { new Control { MinSize = new Vector2(50, 1000) } },
        };
        UiManager.PopupRoot.AddChild(scroll);
        scroll.Measure(new Vector2(100, 100));
        scroll.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(100, 100)));
        return scroll;
    }

    private static VScrollBar BarOf(ScrollContainer scroll)
    {
        foreach (var child in scroll.Children)
        {
            if (child is VScrollBar bar)
                return bar;
        }

        throw new InvalidOperationException();
    }

    [Test]
    public async Task ScrollBarIs15WideWithRoomForArrowsAtEachEnd()
    {
        await SetTheme(true);

        await Client.WaitAssertion(() =>
        {
            var scroll = MakeScroll();
            var bar = BarOf(scroll);
            bar.ForceRunStyleUpdate();
            scroll.Measure(new Vector2(100, 100));
            scroll.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(100, 100)));

            Assert.That(bar.Visible, Is.True);
            Assert.That(bar.Width, Is.EqualTo(15f), "tg reserves 15px");
            Assert.That(bar.Position.Y, Is.EqualTo(17f), "A 15px cell and 2px above the thumb's reach");
            Assert.That(bar.Height, Is.EqualTo(100f - 34f));

            Assert.That(bar.TryGetStyleProperty<StyleBox>(ScrollBar.StylePropertyGrabber, out var thumb));
            Assert.That(thumb, Is.TypeOf<KsTguiScrollThumbStyleBox>());
            Assert.That(thumb!.MinimumSize.X, Is.EqualTo(15f));

            scroll.Orphan();
        });
    }

    [Test]
    public async Task ScrollBarIsUntouchedUnderBaron()
    {
        await SetTheme(false);

        await Client.WaitAssertion(() =>
        {
            var scroll = MakeScroll();
            var bar = BarOf(scroll);
            bar.ForceRunStyleUpdate();

            Assert.That(bar.Margin, Is.EqualTo(new Thickness(0f)));
            Assert.That(bar.TryGetStyleProperty<StyleBox>(ScrollBar.StylePropertyGrabber, out var thumb));
            Assert.That(thumb, Is.Not.TypeOf<KsTguiScrollThumbStyleBox>());

            scroll.Orphan();
        });
    }

    [Test]
    public async Task ArrowsScrollALineAndRepeatWhenHeld()
    {
        await SetTheme(true);

        await Client.WaitAssertion(() =>
        {
            var scroll = MakeScroll();
            var bar = BarOf(scroll);
            bar.ForceRunStyleUpdate();
            scroll.Measure(new Vector2(100, 100));
            scroll.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(100, 100)));

            var barBox = UIBox2.FromDimensions(bar.GlobalPosition, bar.Size);
            var downCell = KsTguiScrollBarGeometry.Cell(barBox, horizontal: false, end: true, scale: 1f).Center;
            var upCell = KsTguiScrollBarGeometry.Cell(barBox, horizontal: false, end: false, scale: 1f).Center;
            var arrows = new KsTguiScrollArrows();

            arrows.Update(scroll, mouseDown: true, downCell, 0f);
            Assert.That(scroll.VScrollTarget, Is.EqualTo(40f), "A press scrolls a line");

            arrows.Update(scroll, mouseDown: true, downCell, 0.2f);
            Assert.That(scroll.VScrollTarget, Is.EqualTo(40f), "Not before the delay");

            arrows.Update(scroll, mouseDown: true, downCell, 0.06f);
            Assert.That(scroll.VScrollTarget, Is.EqualTo(80f), "After a quarter second it repeats");

            arrows.Update(scroll, mouseDown: true, downCell, 0.1f);
            Assert.That(scroll.VScrollTarget, Is.EqualTo(160f), "Then every 50ms");

            arrows.Update(scroll, mouseDown: false, downCell, 0f);
            arrows.Update(scroll, mouseDown: true, upCell, 0f);
            Assert.That(scroll.VScrollTarget, Is.EqualTo(120f), "The top arrow scrolls back");

            arrows.Update(scroll, mouseDown: false, upCell, 0f);
            arrows.Update(scroll, mouseDown: true, new Vector2(50f, 50f), 0f);
            Assert.That(scroll.VScrollTarget, Is.EqualTo(120f), "A press in the content is not an arrow");

            scroll.Orphan();
        });
    }

    // Input

    [Test]
    public async Task PlaceholderIsItalicAndDisabledBorderIsGrey()
    {
        await SetTheme(true);

        await Client.WaitAssertion(() =>
        {
            var input = new LineEdit { PlaceHolder = "search" };
            UiManager.PopupRoot.AddChild(input);
            input.ForceRunStyleUpdate();

            Assert.That(input.TryGetStyleProperty<Font>("font", out var font), Is.True, "The placeholder state sets a font");
            Assert.That(font, Is.TypeOf<StackedFont>());
            Assert.That(font, Is.SameAs(Client.ResolveDependency<KsTguiFonts>().GetFont(12, FontKind.Italic)));

            // White at .5 over #0D0D0D, not over the window.
            input.TryGetStyleProperty<Color>("font-color", out var color);
            Assert.That(color, Is.EqualTo(KsTguiPalettes.Translucent(Color.White, 0.5f, KsTguiThemePalette.Nanotrasen.InputBackground)));

            input.Text = "typed";
            input.ForceRunStyleUpdate();
            input.TryGetStyleProperty<Font>("font", out var typedFont);
            Assert.That(typedFont, Is.Not.SameAs(font), "Typed text is upright");

            input.Editable = false;
            input.ForceRunStyleUpdate();
            input.TryGetStyleProperty<StyleBox>(LineEdit.StylePropertyStyleBox, out var box);
            Assert.That(((KsTguiStyleBox)box!).BorderColor, Is.EqualTo(Color.FromHex("#808080")));

            input.Orphan();
        });
    }

    // Dropdown

    private static void Restyle(Control control)
    {
        control.ForceRunStyleUpdate();
        foreach (var child in control.Children)
            Restyle(child);
    }

    private OptionButton MakeDropdown()
    {
        var option = new OptionButton();
        option.AddItem("one");
        option.AddItem("two");
        UiManager.PopupRoot.AddChild(option);
        Restyle(option);
        return option;
    }

    [Test]
    public async Task DropdownIs22TallWithAChevronCell()
    {
        await SetTheme(true);

        await Client.WaitAssertion(() =>
        {
            var option = MakeDropdown();
            option.Measure(new Vector2(500, 500));

            var hBox = option.GetChild(0);

            Assert.That(hBox.DesiredSize.Y, Is.EqualTo(22f), "tg's dropdown is 22 tall");
            Assert.That(hBox.GetChild(1).DesiredSize, Is.EqualTo(new Vector2(22f, 22f)), "and its chevron cell 22 across");
            Assert.That(option.Margin, Is.EqualTo(new Thickness(0f, 0f, 2f, 2f)), "with a Button's gap");

            option.Orphan();
        });
    }

    [Test]
    public async Task DropdownOpensSixBelowCappedAndEasingIn()
    {
        await SetTheme(true);

        OptionButton option = null!;
        Popup popup = null!;
        await Client.WaitPost(() =>
        {
            option = MakeDropdown();
            for (var i = 0; i < 40; i++)
                option.AddItem("row " + i);

            option.Measure(new Vector2(300, 300));
            option.Arrange(UIBox2.FromDimensions(new Vector2(20, 20), option.DesiredSize));
            UiManager.SetHovered(option);
            Press(option);

            foreach (var child in UiManager.ModalRoot.Children)
            {
                if (child is Popup found)
                    popup = found;
            }
        });

        await Client.WaitAssertion(() =>
        {
            Assert.That(popup, Is.Not.Null);
            Assert.That(popup.MaxHeight, Is.EqualTo(202f), "200 with the border either side");
            Assert.That(popup.ChildCount, Is.EqualTo(1));
            Assert.That(popup.Children[0], Is.TypeOf<KsTguiScaleHost>());

            var host = (KsTguiScaleHost)popup.Children[0];
            Assert.That(host.Scale, Is.EqualTo(0.9f));

            Feel.FrameUpdate(new FrameEventArgs(0.01f));
            Assert.That(PopupContainer.GetPopupOrigin(popup).Y, Is.EqualTo(option.GlobalPosition.Y + option.Size.Y + 6f), "6px below");

            var triangle = option.Children[0].Children[1] as TextureRect;
            Assert.That(triangle!.ShaderOverride, Is.Not.Null, "The chevron is being turned");

            Feel.FrameUpdate(new FrameEventArgs(0.3f));
            Assert.That(host.Scale, Is.EqualTo(1f).Within(0.001f));
            Assert.That(host.Modulate.A, Is.EqualTo(1f).Within(0.001f));

            popup.Close();
            for (var i = 0; i < 4; i++)
                Feel.FrameUpdate(new FrameEventArgs(0.1f));

            Assert.That(triangle.ShaderOverride, Is.Null, "Turned back and handed back to the sheet");
            option.Orphan();
        });
    }

    [Test]
    public async Task DropdownRowsLightAtOnce()
    {
        await SetTheme(true);

        await Client.WaitAssertion(() =>
        {
            var entry = new Button { Text = "x" };
            entry.AddStyleClass(KsTguiSheetlet.StyleClassDropdownEntry);
            UiManager.PopupRoot.AddChild(entry);
            entry.ForceRunStyleUpdate();

            UiManager.SetHovered(entry);
            entry.ForceRunStyleUpdate();
            Feel.UpdateButtons(entry, 0f);
            Feel.UpdateButtons(entry, 0.001f);

            entry.TryGetStyleProperty<Color>(Control.StylePropertyModulateSelf, out var hoverColor);
            Assert.That(entry.ModulateSelfOverride, Is.EqualTo(hoverColor), "Hover is not eased in");

            Assert.That(entry.Margin, Is.EqualTo(new Thickness(0f)), "No stray 2px");

            UiManager.SetHovered(null);
            entry.Orphan();
        });
    }
}
