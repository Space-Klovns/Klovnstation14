using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui;
using Content.Client._KS14.Tgui.Controls;
using Content.Client.Stylesheets.Fonts;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     tgui's input components: the mono font, which needs no client, and the Slider, right-aligned inputs, TextArea and
///         blur-on-Enter.
/// </summary>
[TestOf(typeof(KsTguiFonts))]
public sealed class KsTguiMonoFontPureTest
{
    private static readonly string[] SystemNames = ["Verdana", "Verdana-Bold", "Consolas", "Consolas-Bold", "Consolas-Italic", "Arial"];

    /// <summary>
    ///     tg's <c>--font-family-mono</c> is <c>Consolas</c>: the face is found by PostScript name from plain data, and the
    ///         sans family is never mistaken for it.
    /// </summary>
    [TestCase(FontKind.Regular, "Consolas")]
    [TestCase(FontKind.Bold, "Consolas-Bold")]
    [TestCase(FontKind.Italic, "Consolas-Italic")]
    public void MonoFaceIsConsolasByPostscriptName(FontKind kind, string expected)
    {
        Assert.That(KsTguiFonts.SelectFace(SystemNames, name => name, kind, mono: true), Is.EqualTo(expected));
        Assert.That(KsTguiFonts.SelectFace(SystemNames, name => name, kind), Is.Not.EqualTo(expected), "The sans lookup still wants Verdana.");
    }

    [Test]
    public void NoConsolasMeansNoSystemMonoFace()
    {
        Assert.That(KsTguiFonts.SelectFace(new[] { "Verdana", "Arial" }, name => name, FontKind.Regular, mono: true), Is.Null);
        Assert.That(KsTguiFonts.SelectFace(new[] { "Consolas-Bold" }, name => name, FontKind.BoldItalic, mono: true), Is.Null);
    }

    /// <summary>
    ///     The bundled stand-in is Roboto Mono, which has no bold italic.
    /// </summary>
    [TestCase(FontKind.Regular, "/Fonts/RobotoMono/RobotoMono-Regular.ttf")]
    [TestCase(FontKind.Bold, "/Fonts/RobotoMono/RobotoMono-Bold.ttf")]
    [TestCase(FontKind.Italic, "/Fonts/RobotoMono/RobotoMono-Italic.ttf")]
    [TestCase(FontKind.BoldItalic, "/Fonts/RobotoMono/RobotoMono-Bold.ttf")]
    public void BundledMonoIsRobotoMono(FontKind kind, string path)
    {
        Assert.That(KsTguiFonts.MonoBundledPath(kind), Is.EqualTo(path));
    }
}

[TestOf(typeof(KsTguiSlider))]
public sealed class KsTguiInputsTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private IUserInterfaceManager UiManager => Client.ResolveDependency<IUserInterfaceManager>();

    private async Task SetTheme(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);
    }

    private static void Layout(Control control, float width, float height)
    {
        control.Measure(new Vector2(width, height));
        control.Arrange(new UIBox2(0f, 0f, width, height));
    }

    private static KsTguiSlider MakeSlider(List<float> changes, float value = 50f)
    {
        var slider = new KsTguiSlider { MinValue = 0f, MaxValue = 100f, Step = 5f, StepPixelSize = 2f, Unit = "kPa" };
        slider.SetValue(value);
        slider.OnChanged += changes.Add;
        return slider;
    }

    // Slider

    /// <summary>
    ///     DraggableControl with the matrix [1, 0]: sideways, right is more, relative to the press, a step per step-pixels,
    ///         held to the range, and reported only when the button is let go.
    /// </summary>
    [Test]
    public async Task SliderDragsSidewaysByStepAndReportsOnRelease()
    {
        var changes = new List<float>();
        await Client.WaitPost(() =>
        {
            var slider = MakeSlider(changes);

            slider.BeginPress(300f);
            slider.DragTo(320f);
            Assert.That(slider.Value, Is.EqualTo(50f), "Nothing listens until the hold is up.");

            slider.Advance(KsTguiKnob.HoldSeconds);
            slider.DragTo(320f);
            Assert.That(slider.Value, Is.EqualTo(100f), "20 px right at 2 px per 5 kPa is 50 more, which is the top.");
            slider.DragTo(290f);
            Assert.That(slider.Value, Is.EqualTo(25f), "10 px left of the press is 25 less.");
            slider.DragTo(-5000f);
            Assert.That(slider.Value, Is.EqualTo(0f));
            slider.DragTo(304f);
            Assert.That(slider.Value, Is.EqualTo(60f));
            Assert.That(changes, Is.Empty);

            slider.Release();
            Assert.That(changes, Is.EqualTo(new[] { 60f }));
            Assert.That(slider.Value, Is.EqualTo(60f));
            Assert.That(slider.IsEditing, Is.False, "A drag does not start typing.");
        });
    }

    /// <summary>
    ///     Without a <c>stepPixelSize</c> the bar's own width is the whole range.
    /// </summary>
    [Test]
    public async Task SliderDefaultStepPixelSizeIsItsWidthOverTheSteps()
    {
        var changes = new List<float>();
        await Client.WaitPost(() =>
        {
            var slider = MakeSlider(changes);
            slider.StepPixelSize = null;
            Layout(slider, 200f, 20f);

            slider.BeginPress(0f);
            Assert.That(slider.EffectiveStepPixelSize, Is.EqualTo(10f), "200 px over 20 steps.");
            slider.Advance(KsTguiKnob.HoldSeconds);
            slider.DragTo(200f);
            Assert.That(slider.Value, Is.EqualTo(100f), "Dragging its whole width sweeps the whole range.");
        });
    }

    [Test]
    public async Task SliderTicksWhileDraggingOnlyWhenAsked()
    {
        var changes = new List<float>();
        await Client.WaitPost(() =>
        {
            var slider = MakeSlider(changes);
            slider.TickWhileDragging = true;

            slider.BeginPress(300f);
            slider.Advance(KsTguiKnob.HoldSeconds);
            slider.DragTo(310f);
            Assert.That(changes, Is.Empty);

            slider.Advance(KsTguiKnob.TickSeconds + 0.01f);
            Assert.That(changes, Is.EqualTo(new[] { 75f }));

            slider.Release();
            changes.Clear();
            slider.TickWhileDragging = false;
            slider.BeginPress(300f);
            slider.Advance(KsTguiKnob.HoldSeconds);
            slider.Advance(KsTguiKnob.TickSeconds * 3f);
            Assert.That(changes, Is.Empty);
        });
    }

    /// <summary>
    ///     <c>Slider__popupValue</c> is only there while dragging, once the value has moved, and says the number with its
    ///         unit. It is a control of its own in the popup layer, which is where a blur goes.
    /// </summary>
    [Test]
    public async Task SliderPopupIsASeparateControlOnlyWhileTheValueIsMoving()
    {
        var changes = new List<float>();
        KsTguiSlider slider = null;
        await Client.WaitPost(() =>
        {
            slider = MakeSlider(changes);
            Host(slider);
            Layout(slider, 200f, 20f);

            Assert.That(slider.PopupControl.Parent, Is.Null);
            slider.BeginPress(300f);
            slider.Advance(KsTguiKnob.HoldSeconds);
            Assert.That(slider.PopupVisible, Is.False, "Held, but nothing has moved.");

            slider.DragTo(310f);
            Assert.That(slider.PopupVisible);
            Assert.That(slider.PopupText, Is.EqualTo("75 kPa"));
        });

        await Pair.RunTicksSync(2);

        await Client.WaitAssertion(() =>
        {
            Assert.That(slider.PopupControl.Parent, Is.SameAs(UiManager.PopupRoot), "Shown in the popup layer, not inside the bar.");

            slider.Release();
            Assert.That(slider.PopupVisible, Is.False);
            Assert.That(slider.PopupControl.Parent, Is.Null);

            slider.Format = value => "x" + value;
            Assert.That(slider.PopupText, Is.EqualTo("x75 kPa"), "format and unit.");
            slider.Parent.Orphan();
        });
    }

    [Test]
    public async Task SliderPopupIsAboveTheCursor()
    {
        var changes = new List<float>();
        KsTguiSlider slider = null;
        await Client.WaitPost(() =>
        {
            slider = MakeSlider(changes, 50f);
            Host(slider);
            Layout(slider, 200f, 20f);
            slider.BeginPress(300f);
            slider.Advance(KsTguiKnob.HoldSeconds);
            slider.DragTo(300f);
            slider.DragTo(302f);
        });

        await Pair.RunTicksSync(2);

        await Client.WaitAssertion(() =>
        {
            var popup = slider.PopupControl;
            var cursorX = slider.GlobalPosition.X + 1f + (slider.Size.X - 2f) * 0.55f;
            Assert.That(popup.GlobalPosition.X + popup.Size.X / 2f, Is.EqualTo(cursorX).Within(1.5f), "Centred on the cursor.");
            Assert.That(popup.GlobalPosition.Y + popup.Size.Y, Is.LessThan(slider.GlobalPosition.Y), "Above the bar.");
            slider.Release();
            slider.Parent.Orphan();
        });
    }

    /// <summary>
    ///     A control in a window-sized host of its own: the popup goes in the popup root, which can't be the slider's own
    ///         parent while it is being updated.
    /// </summary>
    private void Host(Control control)
    {
        var host = new Control { MinSize = new Vector2(400f, 100f) };
        host.AddChild(control);
        UiManager.WindowRoot.AddChild(host);
        control.Margin = new Thickness(40f, 60f, 0f, 0f);
        control.HorizontalAlignment = Control.HAlignment.Left;
        control.VerticalAlignment = Control.VAlignment.Top;
        control.SetSize = new Vector2(200f, 20f);
    }

    /// <summary>
    ///     A click opens typing with everything selected; Enter commits a typed value held to the range, nonsense changes
    ///         nothing, and Escape reverts.
    /// </summary>
    [Test]
    public async Task SliderClickTypesAValue()
    {
        var changes = new List<float>();
        await Client.WaitPost(() =>
        {
            var slider = MakeSlider(changes);

            slider.BeginPress(300f);
            slider.Release();
            Assert.That(slider.IsEditing, Is.False, "The box opens when the hold would have been up.");
            slider.Advance(KsTguiKnob.HoldSeconds);

            Assert.That(slider.IsEditing);
            Assert.That(slider.Edit.Visible);
            Assert.That(slider.Edit.Text, Is.EqualTo("50"));
            Assert.That(slider.Edit.SelectionLength, Is.EqualTo(2));
            Assert.That(changes, Is.EqualTo(new[] { 50f }), "A click is reported, as tgui's is.");

            slider.Edit.Text = "5000";
            slider.Commit();
            Assert.That(slider.IsEditing, Is.False);
            Assert.That(slider.Value, Is.EqualTo(100f));
            Assert.That(changes, Is.EqualTo(new[] { 50f, 100f }));

            Click(slider);
            changes.Clear();
            slider.Edit.Text = "not a number";
            slider.Commit();
            Assert.That(slider.Value, Is.EqualTo(100f));
            Assert.That(changes, Is.Empty);

            Click(slider);
            slider.Edit.Text = "3";
            slider.Cancel();
            Assert.That(slider.Value, Is.EqualTo(100f), "Escape keeps the value there was.");

            slider.Unclamped = true;
            Click(slider);
            changes.Clear();
            slider.Edit.Text = "5000";
            slider.Commit();
            Assert.That(slider.Value, Is.EqualTo(5000f));
            Assert.That(changes, Is.EqualTo(new[] { 5000f }));
        });
    }

    private static void Click(KsTguiSlider slider)
    {
        slider.BeginPress(0f);
        slider.Release();
        slider.Advance(KsTguiKnob.HoldSeconds);
        Assert.That(slider.IsEditing);
    }

    /// <summary>
    ///     The bar fills to the value, the lighter wide fill to a fill value past it, and the colour is the colour, else the
    ///         range of the fill value, else the default.
    /// </summary>
    [Test]
    public async Task SliderFillsFollowTheFillValueAndRanges()
    {
        await Client.WaitPost(() =>
        {
            var slider = new KsTguiSlider { MinValue = 0f, MaxValue = 100f };
            slider.SetValue(25f);

            Assert.That(slider.CursorFraction, Is.EqualTo(0.25f));
            Assert.That(slider.DisplayedMainFill, Is.EqualTo(0.25f));
            Assert.That(slider.DisplayedWideFill, Is.EqualTo(0.25f));
            Assert.That(slider.ResolvedColor, Is.EqualTo(KsTguiColor.Default));

            slider.AddRange(KsTguiColor.Red, 0f, 50f);
            slider.AddRange(KsTguiColor.Green, 50f, 100f);
            Assert.That(slider.ResolvedColor, Is.EqualTo(KsTguiColor.Red));

            slider.FillValue = 75f;
            Assert.That(slider.ResolvedColor, Is.EqualTo(KsTguiColor.Green), "The range of the fill value.");
            Assert.That(slider.DisplayedMainFill, Is.EqualTo(0.25f), "Solid up to the value...");
            Assert.That(slider.DisplayedWideFill, Is.EqualTo(0.75f), "...and the wide fill to the fill value.");
            Assert.That(slider.CursorFraction, Is.EqualTo(0.25f), "The cursor still shows the value.");

            slider.Color = KsTguiColor.Yellow;
            Assert.That(slider.ResolvedColor, Is.EqualTo(KsTguiColor.Yellow));

            slider.SetValue(500f);
            Assert.That(slider.CursorFraction, Is.EqualTo(1f), "The cursor stops at the end.");
        });
    }

    /// <summary>
    ///     The fills ease for a value set from outside, and snap while dragging.
    /// </summary>
    [Test]
    public async Task SliderFillEasesWhenSetAndSnapsWhileDragging()
    {
        var changes = new List<float>();
        await Client.WaitPost(() =>
        {
            var slider = MakeSlider(changes, 0f);
            slider.AdvanceTransitions(0.1f);

            slider.SetValue(100f);
            Assert.That(slider.Transitioning);
            Assert.That(slider.DisplayedMainFill, Is.EqualTo(0f), "Starts where it was.");
            slider.AdvanceTransitions(0.5f);
            Assert.That(slider.DisplayedMainFill, Is.InRange(0.05f, 0.95f));
            slider.AdvanceTransitions(1f);
            Assert.That(slider.DisplayedMainFill, Is.EqualTo(1f));

            slider.BeginPress(300f);
            slider.Advance(KsTguiKnob.HoldSeconds);
            slider.DragTo(290f);
            Assert.That(slider.DisplayedMainFill, Is.EqualTo(0.75f).Within(0.0001f), "A drag is not eased.");
        });
    }

    [Test]
    public async Task DisabledSliderIgnoresThePress()
    {
        var changes = new List<float>();
        await Client.WaitPost(() =>
        {
            var slider = MakeSlider(changes);
            slider.Disabled = true;

            slider.BeginPress(300f);
            Assert.That(slider.IsPressed, Is.False);
            slider.Release();
            slider.Advance(1f);
            Assert.That(slider.IsEditing, Is.False);
            Assert.That(changes, Is.Empty);
            Assert.That(slider.Modulate.A, Is.EqualTo(0.5f), "--slider-disabled-opacity");
        });
    }

    // Right-aligned inputs

    private static readonly string[] AlignedTexts = ["5", "50", "1234", "-0.5", "100.25"];

    private static void ClickAt(LineEdit edit, float x, int count)
    {
        var method = typeof(LineEdit).GetMethod("KeyBindDown", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        var pointer = new ScreenCoordinates(new Vector2(100f * count, 0f), WindowId.Main);
        method!.Invoke(edit, new object[]
        {
            new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Down, pointer, true, new Vector2(x, 5f), new Vector2(x, 5f)),
        });
    }

    /// <summary>
    ///     Text ends at the right padding, whatever its length, and the caret positions the engine reports are the ones the
    ///         text is drawn at: a click on a letter's left edge puts the caret before it.
    /// </summary>
    private static void AssertRightAligned(KsTguiRightAlignedLineEdit edit, float padding)
    {
        // Select-all-on-focus swallows clicks made on the frame the box took the keyboard, which these boxes did in the test.
        edit.SelectAllOnFocus = false;

        var clicks = 0;
        foreach (var text in AlignedTexts)
        {
            edit.SetText(text, invokeEvent: true);

            var end = edit.GetOffsetAtIndex(text.Length);
            Assert.That(end, Is.EqualTo(edit.Size.X - padding - 1f).Within(0.01f), $"'{text}' ends at the right padding.");

            for (var index = 0; index < text.Length; index++)
            {
                ClickAt(edit, edit.GetOffsetAtIndex(index) + 0.1f, ++clicks);
                Assert.That(edit.CursorPosition, Is.EqualTo(index), $"A click at the left of '{text}'[{index}] puts the caret there (x {edit.GetOffsetAtIndex(index)}, size {edit.Size}, scale {edit.UIScale}, margin {edit.TextLeftMargin}, offsets {string.Join(',', System.Linq.Enumerable.Select(System.Linq.Enumerable.Range(0, text.Length + 1), i => edit.GetOffsetAtIndex(i)))}).");
            }

            ClickAt(edit, end + 0.1f, ++clicks);
            Assert.That(edit.CursorPosition, Is.EqualTo(text.Length), "A click past the end puts the caret at the end.");
        }

        // A longer text than the room scrolls like the engine's, from the plain padding.
        edit.SetText(new string('8', 80), invokeEvent: true);
        Assert.That(edit.TextLeftMargin, Is.EqualTo(padding));

        // And a shorter one goes back to the right.
        edit.SetText("7", invokeEvent: true);
        Assert.That(edit.GetOffsetAtIndex(1), Is.EqualTo(edit.Size.X - padding - 1f).Within(0.01f));
    }

    [Test]
    public async Task RestrictedInputTextEndsAtTheRight()
    {
        await SetTheme(true);

        await Client.WaitAssertion(() =>
        {
            var input = new KsTguiRestrictedInput { AllowFloats = true, MinValue = -10000f };
            UiManager.PopupRoot.AddChild(input);
            input.ForceRunStyleUpdate();
            Layout(input, 120f, 22f);

            AssertRightAligned(input, 5f);
            input.Orphan();
        });
    }

    [Test]
    public async Task KnobEditTextEndsAtTheRight()
    {
        await SetTheme(true);

        await Client.WaitAssertion(() =>
        {
            var knob = new KsTguiKnob { KnobSize = 3f, MinValue = 0f, MaxValue = 1000f };
            UiManager.PopupRoot.AddChild(knob);
            knob.SetValue(5f);
            knob.BeginPress(0f);
            knob.Release();
            knob.Advance(KsTguiKnob.HoldSeconds);
            Assert.That(knob.IsEditing);
            knob.ForceRunStyleUpdate();
            knob.Edit.ForceRunStyleUpdate();
            Layout(knob, 120f, 22f);

            AssertRightAligned(knob.Edit, 6f);
            knob.Orphan();
        });
    }

    [Test]
    public async Task NumberInputEditTextEndsAtTheRight()
    {
        await SetTheme(true);

        await Client.WaitAssertion(() =>
        {
            var input = new KsTguiNumberInput { MinValue = 0f, MaxValue = 1000f, WidthPixels = 120f };
            UiManager.PopupRoot.AddChild(input);
            input.SetValue(5f);
            input.BeginDrag(0f);
            input.EndDrag();
            Assert.That(input.IsEditing);
            input.Edit.ForceRunStyleUpdate();
            Layout(input, 120f, 22f);

            AssertRightAligned(input.Edit, 6f);
            input.Orphan();
        });
    }

    [Test]
    public async Task SliderEditTextEndsAtTheRight()
    {
        await SetTheme(true);

        await Client.WaitAssertion(() =>
        {
            var changes = new List<float>();
            var slider = MakeSlider(changes);
            UiManager.PopupRoot.AddChild(slider);
            Click(slider);
            slider.Edit.ForceRunStyleUpdate();
            Layout(slider, 200f, 22f);

            AssertRightAligned(slider.Edit, 6f);
            slider.Orphan();
        });
    }

    /// <summary>
    ///     A resize keeps the text at the right edge, and a font change moves the margin with the text's width.
    /// </summary>
    [Test]
    public async Task RightAlignedTextFollowsTheSize()
    {
        await SetTheme(true);

        await Client.WaitAssertion(() =>
        {
            var input = new KsTguiRestrictedInput();
            UiManager.PopupRoot.AddChild(input);
            input.ForceRunStyleUpdate();
            input.SetValue(123f);

            Layout(input, 100f, 22f);
            Assert.That(input.GetOffsetAtIndex(3), Is.EqualTo(100f - 5f - 1f).Within(0.01f));
            Layout(input, 160f, 22f);
            Assert.That(input.GetOffsetAtIndex(3), Is.EqualTo(160f - 5f - 1f).Within(0.01f));
            input.Orphan();
        });
    }

    // TextArea

    private static void Invoke(Control control, GUIBoundKeyEventArgs args)
    {
        var field = typeof(Control).GetField(nameof(Control.OnKeyBindDown), BindingFlags.Instance | BindingFlags.NonPublic);
        ((Action<GUIBoundKeyEventArgs>)field!.GetValue(control)!).Invoke(args);
    }

    private static GUIBoundKeyEventArgs Key(BoundKeyFunction function)
    {
        return new GUIBoundKeyEventArgs(function, BoundKeyState.Down, default, false, default, default);
    }

    /// <summary>
    ///     tg's input background, border, radius and padding come from the sheet's input, the border lights up while the
    ///         keyboard is in the box and is grey disabled, the placeholder is italic, and monospace is the mono face.
    /// </summary>
    [Test]
    public async Task TextAreaLooksLikeTgsInput()
    {
        await SetTheme(true);
        var fonts = Client.ResolveDependency<KsTguiFonts>();
        KsTguiTextArea area = null;
        var idle = default(Color);

        await Client.WaitPost(() =>
        {
            area = new KsTguiTextArea { Placeholder = "Say it", Fluid = true };
            UiManager.PopupRoot.AddChild(area);
            area.ForceRunStyleUpdate();
            area.Edit.ForceRunStyleUpdate();
            Layout(area, 200f, 60f);

            Assert.That(area.BorderColor, Is.Not.EqualTo(KsTguiTextArea.BorderDisabled));
            Assert.That(area.BackgroundColor.A, Is.EqualTo(1f));

            // Padding 1.02px / 3.96px and the 1px border. The right padding is the text's own, inside the TextEdit, so that
            //      the bar's column sits between it and the border (TextAreaKeepsAThinBarsColumnAndShowsItOnlyToScroll).
            Assert.That(area.Edit.Margin, Is.EqualTo(new Thickness(5f, 2f, 1f, 2f)), "Padding 1.02px / 3.96px and the 1px border.");

            area.Edit.TryGetStyleProperty<Font>("font", out var placeholderFont);
            Assert.That(placeholderFont, Is.SameAs(fonts.GetFont(12, FontKind.Italic)), "An empty box shows its placeholder in italics.");

            area.SetText("hello");
            area.Edit.ForceRunStyleUpdate();
            area.Edit.TryGetStyleProperty<Font>("font", out var font);
            Assert.That(font, Is.SameAs(fonts.GetFont(12)), "Typed text is the sheet's 12px.");

            area.Monospace = true;
            area.Edit.ForceRunStyleUpdate();
            area.Edit.TryGetStyleProperty<Font>("font", out var monoFont);
            Assert.That(monoFont, Is.SameAs(fonts.GetMonoFont(12)));
            Assert.That(monoFont, Is.Not.SameAs(font));
            area.Monospace = false;

            idle = area.BorderColor;
            area.Focus();
        });

        await Pair.RunTicksSync(2);

        await Client.WaitAssertion(() =>
        {
            Assert.That(area.IsFocused);
            Assert.That(area.BorderColor, Is.Not.EqualTo(idle), "--input-border-color-focus is lighter than the idle border.");
            Assert.That(area.BorderColor.B, Is.GreaterThan(idle.B));

            area.Disabled = true;
            Assert.That(area.BorderColor, Is.EqualTo(KsTguiTextArea.BorderDisabled));
            Assert.That(area.Edit.Editable, Is.False);
            area.Orphan();
        });
    }

    /// <summary>
    ///     tg's text area is <c>scrollbar-width: thin</c> with <c>scrollbar-gutter: stable</c>: the text always stops short of
    ///         the thin bar's column, and the bar is only there while the text overflows. Measured in tg's Adminhelp: a 10px
    ///         column, a 6px thumb, small arrows, and no track colour.
    /// </summary>
    [Test]
    public async Task TextAreaKeepsAThinBarsColumnAndShowsItOnlyToScroll()
    {
        var thin = KsTguiScrollBarMetrics.Thin;

        await SetTheme(true);
        KsTguiTextArea area = null;

        await Client.WaitPost(() =>
        {
            area = new KsTguiTextArea { Rows = 2, Fluid = true, MinSize = new Vector2(200f, 0f) };
            UiManager.PopupRoot.AddChild(area);
            LayoutContainer.SetPosition(area, new Vector2(10f, 10f));
        });

        await Pair.RunTicksSync(3);

        await Client.WaitAssertion(() =>
        {
            var bar = area.ScrollBar;
            var text = area.Edit.Children.First(child => child != bar);
            Assert.That(area.ScrollBarShown, Is.False, "Nothing to scroll: no bar.");
            Assert.That(thin.BarWidth, Is.EqualTo(10f));
            Assert.That(thin.ThumbWidth, Is.EqualTo(6f));
            // The column is kept anyway, between the text's padding and the border.
            var textRight = area.GlobalPosition.X + area.Width - KsTguiTextArea.BorderWidth - thin.BarWidth
                - (KsTguiTextArea.HorizontalMargin - KsTguiTextArea.BorderWidth);
            Assert.That(text.GlobalPosition.X + text.Width, Is.EqualTo(textRight).Within(0.01f), "The column is kept anyway.");
            Assert.That(bar.TryGetStyleProperty<StyleBox>(ScrollBar.StylePropertyGrabber, out var thumb));
            Assert.That(thumb!.MinimumSize.X, Is.EqualTo(thin.BarWidth));
            Assert.That(bar.TryGetStyleProperty<StyleBox>(ScrollBar.StylePropertyTrack, out var track));
            Assert.That(track, Is.TypeOf<KsTguiScrollTrackStyleBox>(), "It has arrows.");
            Assert.That(((KsTguiScrollTrackStyleBox)track).Metrics, Is.SameAs(thin), "Thin ones.");
            Assert.That(((KsTguiScrollTrackStyleBox)track).TrackColor, Is.Null, "TextArea.scss: scrollbar-color thumb transparent.");

            area.SetText(string.Join("\n", Enumerable.Range(0, 30).Select(i => $"line {i}")));
        });

        await Pair.RunTicksSync(3);

        await Client.WaitAssertion(() =>
        {
            Assert.That(area.ScrollBarShown, Is.True, "Thirty lines in two rows scroll.");
            var bar = area.ScrollBar;

            // Measured in tg's Adminhelp: the column ends at the border, with the text's padding to its left.
            var borderInside = area.GlobalPosition.X + area.Width - KsTguiTextArea.BorderWidth;
            Assert.That(bar.GlobalPosition.X + bar.Width, Is.EqualTo(borderInside).Within(0.01f), "The bar reaches the border.");
            Assert.That(bar.Width, Is.EqualTo(thin.BarWidth));
            var columnTop = area.GlobalPosition.Y + KsTguiTextArea.BorderWidth;
            Assert.That(bar.GlobalPosition.Y - columnTop, Is.EqualTo(thin.EndInset).Within(0.01f), "and leaves its arrows' cells free.");

            // The engine's TextEdit counts its bar in pixels, so a line is 40 UI units at any scale.
            var barBox = UIBox2.FromDimensions(bar.GlobalPosition, bar.Size);
            var cell = thin.Cell(barBox, horizontal: false, end: true, scale: 1f);

            // Next to the border, where a bar pulled out of a padded TextEdit left the cell over nothing that knew of it.
            var nearBorder = new Vector2(cell.Right - 1.5f, cell.Center.Y);
            var window = Client.ResolveDependency<IClyde>().MainWindow.Id;
            var hovered = UiManager.MouseGetControl(new ScreenCoordinates(nearBorder * UiManager.RootControl.UIScale, window));
            var before = bar.ValueTarget;
            var arrows = new KsTguiScrollArrows();
            arrows.Update(hovered, mouseDown: true, nearBorder, 0f);
            Assert.That(bar.ValueTarget - before, Is.EqualTo(KsTguiScrollBarGeometry.ArrowStep * bar.UIScale).Within(0.01f),
                "Its down arrow scrolls a line, right up to the border.");

            area.Orphan();
        });
    }

    /// <summary>
    ///     Enter raises <c>onEnter</c> with the text and lets go of the keyboard; Shift+Enter is not Enter; Escape raises
    ///         <c>onEscape</c> and lets go.
    /// </summary>
    [Test]
    public async Task TextAreaEnterAndEscapeLetGo()
    {
        await SetTheme(true);

        await Client.WaitAssertion(() =>
        {
            var area = new KsTguiTextArea();
            UiManager.PopupRoot.AddChild(area);
            var entered = new List<string>();
            var escaped = new List<string>();
            var changed = new List<string>();
            var focusInHandler = false;
            area.OnEnter += text =>
            {
                entered.Add(text);
                focusInHandler = area.Edit.HasKeyboardFocus();
            };
            area.OnEscape += escaped.Add;
            area.OnChanged += changed.Add;

            area.SetText("hello");
            Assert.That(changed, Is.Empty, "Setting the text raises nothing.");

            area.Focus();
            var enter = Key(EngineKeyFunctions.TextNewline);
            Invoke(area.Edit, enter);
            Assert.That(entered, Is.EqualTo(new[] { "hello" }));
            Assert.That(focusInHandler, Is.True, "The handler runs before the keyboard is let go.");
            Assert.That(area.Edit.HasKeyboardFocus(), Is.False);
            Assert.That(enter.Handled, Is.True, "It is not a new line.");

            area.Focus();
            var escape = Key(EngineKeyFunctions.TextReleaseFocus);
            Invoke(area.Edit, escape);
            Assert.That(escaped, Is.EqualTo(new[] { "hello" }));
            Assert.That(area.Edit.HasKeyboardFocus(), Is.False);

            area.Orphan();
        });
    }

    /// <summary>
    ///     tgui-core's TextArea: Tab inserts a tab character in place of the selection and raises onChange, unless
    ///         <c>dontUseTabForIndent</c>.
    /// </summary>
    [Test]
    public async Task TextAreaTabInsertsATab()
    {
        await SetTheme(true);

        await Client.WaitAssertion(() =>
        {
            var area = new KsTguiTextArea();
            UiManager.PopupRoot.AddChild(area);
            var changed = new List<string>();
            area.OnChanged += changed.Add;

            area.SetText("ab");
            area.Edit.CursorPosition = new TextEdit.CursorPos(1, TextEdit.LineBreakBias.Top);
            area.Edit.SelectionStart = area.Edit.CursorPosition;

            var tab = Key(EngineKeyFunctions.TextTabComplete);
            Invoke(area.Edit, tab);
            Assert.That(area.Text, Is.EqualTo("a	b"));
            Assert.That(changed, Is.EqualTo(new[] { "a	b" }));
            Assert.That(tab.Handled, Is.True);

            // In place of a selection.
            area.Edit.CursorPosition = new TextEdit.CursorPos(3, TextEdit.LineBreakBias.Top);
            area.Edit.SelectionStart = new TextEdit.CursorPos(0, TextEdit.LineBreakBias.Top);
            Invoke(area.Edit, Key(EngineKeyFunctions.TextTabComplete));
            Assert.That(area.Text, Is.EqualTo("	"));

            area.DontUseTabForIndent = true;
            var ignored = Key(EngineKeyFunctions.TextTabComplete);
            Invoke(area.Edit, ignored);
            Assert.That(area.Text, Is.EqualTo("	"));
            Assert.That(ignored.Handled, Is.False);

            area.Orphan();
        });
    }

    /// <summary>
    ///     A textarea's <c>maxlength</c>: typing stops at the limit, a paste is cut to fit, text set from code is left alone.
    /// </summary>
    [Test]
    public async Task TextAreaMaxLengthStopsTypingAndCutsPastes()
    {
        await SetTheme(true);

        KsTguiTextArea area = null;
        var changed = new List<string>();
        await Client.WaitPost(() =>
        {
            area = new KsTguiTextArea { MaxLength = 5 };
            UiManager.PopupRoot.AddChild(area);
            area.OnChanged += changed.Add;
            area.SetText("abc");
            area.Edit.InsertAtCursor("de"); // fits
        });
        await Pair.RunTicksSync(2);
        await Client.WaitPost(() =>
        {
            Assert.That(area.Text, Is.EqualTo("abcde"));
            area.Edit.InsertAtCursor("f"); // typed at the limit
        });
        await Pair.RunTicksSync(2);
        await Client.WaitPost(() =>
        {
            Assert.That(area.Text, Is.EqualTo("abcde"), "Nothing past the limit is typed.");
            Assert.That(Robust.Shared.Utility.Rope.Collapse(area.Edit.TextRope), Is.EqualTo("abcde"));
            Assert.That(changed, Is.EqualTo(new[] { "abcde" }), "A blocked key is no change.");

            // Select the last two and paste more than fits back.
            area.Edit.CursorPosition = new TextEdit.CursorPos(5, TextEdit.LineBreakBias.Top);
            area.Edit.SelectionStart = new TextEdit.CursorPos(3, TextEdit.LineBreakBias.Top);
            area.Edit.InsertAtCursor("123456");
        });
        await Pair.RunTicksSync(2);
        await Client.WaitPost(() =>
        {
            Assert.That(area.Text, Is.EqualTo("abc12"), "The paste is cut to the room the selection made.");
            Assert.That(area.Edit.CursorPosition.Index, Is.EqualTo(5));

            area.SetText("a longer text than five");
            Assert.That(area.Text, Is.EqualTo("a longer text than five"), "Code is not held to it.");
            area.Orphan();
        });
    }

    // Blur on Enter

    private static void PressEnter(IInputManager inputManager)
    {
        inputManager.KeyDown(new KeyEventArgs(Keyboard.Key.Return, false, false, false, false, false, 0));
        inputManager.KeyUp(new KeyEventArgs(Keyboard.Key.Return, false, false, false, false, false, 0));
    }

    /// <summary>
    ///     tgui-core's Input blurs after Enter, once its handler has run: in a window with the tgui chrome, a line edit lets go
    ///         of the keyboard.
    /// </summary>
    [Test]
    public async Task EnterInAWindowsLineEditLetsGoAfterTheHandlerRan()
    {
        await SetTheme(true);
        var inputManager = Client.ResolveDependency<IInputManager>();

        await Client.WaitAssertion(() =>
        {
            var window = new DefaultWindow();
            window.OpenCentered();
            Assert.That(Content.Client._KS14.Tgui.KsTguiChromeUIController.HasChrome(window), "The window is a tgui one.");

            var input = new LineEdit();
            window.Contents.AddChild(input);
            var entered = false;
            var focusedInHandler = false;
            input.OnTextEntered += _ =>
            {
                entered = true;
                focusedInHandler = input.HasKeyboardFocus();
            };

            input.GrabKeyboardFocus();
            Assert.That(UiManager.KeyboardFocused, Is.SameAs(input));

            PressEnter(inputManager);

            Assert.That(entered, "The handler runs.");
            Assert.That(focusedInHandler, "...before the line edit lets go.");
            Assert.That(UiManager.KeyboardFocused, Is.Not.SameAs(input), "tg's Input blurs after Enter.");
            window.Close();
        });
    }

    /// <summary>
    ///     An Enter handler that moves the keyboard to another box keeps it there: only the box that took the key is blurred.
    /// </summary>
    [Test]
    public async Task EnterThatHandsFocusToAnotherBoxKeepsIt()
    {
        await SetTheme(true);
        var inputManager = Client.ResolveDependency<IInputManager>();

        await Client.WaitAssertion(() =>
        {
            var window = new DefaultWindow();
            window.OpenCentered();
            var first = new LineEdit();
            var second = new LineEdit();
            window.Contents.AddChild(first);
            window.Contents.AddChild(second);
            first.OnTextEntered += _ => second.GrabKeyboardFocus();
            first.GrabKeyboardFocus();

            PressEnter(inputManager);

            Assert.That(UiManager.KeyboardFocused, Is.SameAs(second));
            window.Close();
        });
    }

    /// <summary>
    ///     Not in chat, the lobby or anything else outside a tgui window, nor in a window under Baron.
    /// </summary>
    [Test]
    public async Task EnterOutsideAWindowKeepsTheCaret()
    {
        await SetTheme(true);
        var inputManager = Client.ResolveDependency<IInputManager>();

        await Client.WaitAssertion(() =>
        {
            var input = new LineEdit();
            UiManager.PopupRoot.AddChild(input);
            var entered = false;
            input.OnTextEntered += _ => entered = true;
            input.GrabKeyboardFocus();

            PressEnter(inputManager);

            Assert.That(entered, "Enter did reach it.");
            Assert.That(UiManager.KeyboardFocused, Is.SameAs(input));
            input.Orphan();
        });
    }

    [Test]
    public async Task EnterInABaronWindowKeepsTheCaret()
    {
        await SetTheme(false);
        var inputManager = Client.ResolveDependency<IInputManager>();

        await Client.WaitAssertion(() =>
        {
            var window = new DefaultWindow();
            window.OpenCentered();
            var input = new LineEdit();
            window.Contents.AddChild(input);
            var entered = false;
            input.OnTextEntered += _ => entered = true;
            input.GrabKeyboardFocus();

            PressEnter(inputManager);

            Assert.That(entered);
            Assert.That(UiManager.KeyboardFocused, Is.SameAs(input));
            window.Close();
        });
    }
}
