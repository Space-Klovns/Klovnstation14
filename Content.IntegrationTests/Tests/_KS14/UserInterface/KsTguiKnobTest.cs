using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui.Controls;
using Content.Client._KS14.Tgui.Windows.Atmos;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;
using Robust.Shared.Input;
using Robust.UnitTesting.Pool;
using XColor = Robust.Shared.Maths.Color;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     tgui's Knob: the numbers the drag, cursor and ring come from, which need no client.
/// </summary>
[TestOf(typeof(KsTguiKnobMath))]
public sealed class KsTguiKnobMathTest
{
    /// <summary>
    ///     DraggableControl's <c>floor(original / step) * step + trunc(drag / stepPixelSize) * step</c>, held to the range.
    /// </summary>
    [Test]
    public void DragValueIsRelativeSnappedAndClamped()
    {
        // 101.325 is snapped down to 100 before the drag is added: no jump to the click, and nothing moves for a drag under a
        // step's pixels (the truncation).
        Assert.That(KsTguiKnobMath.DragValue(101.325f, 0.9f, 5f, 1f, 0f, 1000f), Is.EqualTo(100f));
        Assert.That(KsTguiKnobMath.DragValue(101.325f, 12f, 5f, 1f, 0f, 1000f), Is.EqualTo(160f));

        // Downwards truncates towards zero too: -3.7 pixels is 3 steps.
        Assert.That(KsTguiKnobMath.DragValue(100f, -3.7f, 5f, 1f, 0f, 1000f), Is.EqualTo(85f));

        // Coarser pixels per step: 10 pixels at 4 per step is 2 steps.
        Assert.That(KsTguiKnobMath.DragValue(50f, 10f, 10f, 4f, 0f, 100f), Is.EqualTo(70f));

        Assert.That(KsTguiKnobMath.DragValue(990f, 500f, 5f, 1f, 0f, 1000f), Is.EqualTo(1000f), "The top holds.");
        Assert.That(KsTguiKnobMath.DragValue(10f, -500f, 5f, 1f, 10f, 1000f), Is.EqualTo(10f), "The bottom holds.");
    }

    /// <summary>
    ///     Without a <c>stepPixelSize</c>, dragging up the knob's own width sweeps the whole range.
    /// </summary>
    [Test]
    public void DefaultStepPixelSizeSweepsTheRangeInTheKnobsWidth()
    {
        // A size 1.25 knob is 39 px wide: 39 / (1000 / 5) steps.
        Assert.That(KsTguiKnobMath.DefaultStepPixelSize(39f, 0f, 1000f, 5f), Is.EqualTo(0.195f).Within(0.0001f));

        var pixelsPerStep = KsTguiKnobMath.DefaultStepPixelSize(31.2f, 0f, 100f, 1f);
        Assert.That(KsTguiKnobMath.DragValue(0f, 31.2f, 1f, pixelsPerStep, 0f, 100f), Is.EqualTo(100f));
    }

    /// <summary>
    ///     <c>min((f - .5) * 270, 225)</c>: bottom left at the minimum, up in the middle, bottom right at the maximum.
    /// </summary>
    [Test]
    public void CursorTurnsTwoSeventyDegreesAcrossTheRange()
    {
        Assert.That(KsTguiKnobMath.CursorDegrees(0f, 0f, 200f), Is.EqualTo(-135f).Within(0.001f));
        Assert.That(KsTguiKnobMath.CursorDegrees(100f, 0f, 200f), Is.EqualTo(0f).Within(0.001f));
        Assert.That(KsTguiKnobMath.CursorDegrees(200f, 0f, 200f), Is.EqualTo(135f).Within(0.001f));
        Assert.That(KsTguiKnobMath.CursorDegrees(150f, 0f, 200f), Is.EqualTo(67.5f).Within(0.001f));
        Assert.That(KsTguiKnobMath.CursorDegrees(1000f, 0f, 200f), Is.EqualTo(225f), "A value past the range is capped at 225.");
    }

    /// <summary>
    ///     The fill is the dash: <c>(2 - 1.5f) * pi * 50</c> of offset, which is f * 270 degrees from the bottom left; a bipolar
    ///         knob starts at the top and is <c>(2.75 - 1.5f)</c>, clockwise above the middle and counter-clockwise below.
    /// </summary>
    [Test]
    public void FillArcSweepsFromTheStartForTheValue()
    {
        Assert.That(KsTguiKnobMath.FillArc(0f, bipolar: false), Is.EqualTo((135f, 0f)));
        Assert.That(KsTguiKnobMath.FillArc(0.5f, bipolar: false), Is.EqualTo((135f, 135f)));
        Assert.That(KsTguiKnobMath.FillArc(1f, bipolar: false), Is.EqualTo((135f, 270f)));
        Assert.That(KsTguiKnobMath.FillArc(1.5f, bipolar: false).Sweep, Is.EqualTo(360f), "Past the end it is a full turn at most.");

        Assert.That(KsTguiKnobMath.FillArc(0.5f, bipolar: true), Is.EqualTo((270f, 0f)));
        Assert.That(KsTguiKnobMath.FillArc(1f, bipolar: true), Is.EqualTo((270f, 135f)));
        Assert.That(KsTguiKnobMath.FillArc(0f, bipolar: true), Is.EqualTo((270f, -135f)));
    }

    [Test]
    public void TypedValueIsClampedUnlessUnclamped()
    {
        Assert.That(KsTguiKnobMath.TypedValue(5000f, 0f, 1000f, unclamped: false), Is.EqualTo(1000f));
        Assert.That(KsTguiKnobMath.TypedValue(-5f, 0f, 1000f, unclamped: false), Is.EqualTo(0f));
        Assert.That(KsTguiKnobMath.TypedValue(5000f, 0f, 1000f, unclamped: true), Is.EqualTo(5000f));
        Assert.That(KsTguiKnobMath.TypedValue(float.NaN, 0f, 1000f, unclamped: true), Is.Null, "Not a number cancels.");
    }

    /// <summary>
    ///     The glow's Gaussian edge: half the alpha at the cap's edge, falling away to nothing by the blur radius.
    /// </summary>
    [Test]
    public void ShadowFallsOffFromAHalfAtTheEdge()
    {
        Assert.That(KsTguiKnobMath.ShadowFalloff(0f, 12f), Is.EqualTo(0.5f).Within(0.001f));
        Assert.That(KsTguiKnobMath.ShadowFalloff(6f, 12f), Is.EqualTo(0.1587f).Within(0.002f), "One sigma out.");
        Assert.That(KsTguiKnobMath.ShadowFalloff(12f, 12f), Is.LessThan(0.03f));
        Assert.That(KsTguiKnobMath.ShadowFalloff(3f, 12f), Is.GreaterThan(KsTguiKnobMath.ShadowFalloff(4f, 12f)));
    }
}

/// <summary>
///     tgui's Knob as a control: the press, the drag, the click that turns into typing, and what is drawn. The mouse is driven
///         through the methods its handlers call.
/// </summary>
[TestOf(typeof(KsTguiKnob))]
public sealed class KsTguiKnobTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static KsTguiKnob Make(List<float> changes, float value = 500f)
    {
        var knob = new KsTguiKnob { KnobSize = 1.25f, MinValue = 0f, MaxValue = 1000f, Step = 5f, StepPixelSize = 1f, Unit = "kPa" };
        knob.SetValue(value);
        knob.OnChanged += changes.Add;
        return knob;
    }

    /// <summary>
    ///     Up is more, relative to the press, only once held for a tenth of a second, and nothing is reported until the button
    ///         is let go.
    /// </summary>
    [Test]
    public async Task DragChangesTheValueUpwardsAndReportsOnlyOnRelease()
    {
        var changes = new List<float>();
        await Client.WaitPost(() =>
        {
            var knob = Make(changes);

            knob.BeginPress(300f);

            // DraggableControl only listens for moves once the hold is up.
            knob.DragTo(290f);
            Assert.That(knob.Value, Is.EqualTo(500f));

            knob.Advance(KsTguiKnob.HoldSeconds);
            Assert.That(knob.IsDragging);
            knob.DragTo(290f);
            Assert.That(knob.Value, Is.EqualTo(550f), "10 px up at 1 px per 5 kPa.");
            knob.DragTo(310f);
            Assert.That(knob.Value, Is.EqualTo(450f), "Relative to the press, so 10 px down is 50 below where it was.");
            knob.DragTo(280f);
            Assert.That(knob.Value, Is.EqualTo(600f));
            Assert.That(changes, Is.Empty, "A drag reports nothing until the button is let go.");

            knob.Release();
            Assert.That(changes, Is.EqualTo(new[] { 600f }));
            Assert.That(knob.Value, Is.EqualTo(600f), "The knob stays where it was let go.");
            Assert.That(knob.IsEditing, Is.False, "A drag does not start editing.");
        });
    }

    [Test]
    public async Task DragStaysInsideTheRange()
    {
        var changes = new List<float>();
        await Client.WaitPost(() =>
        {
            var knob = Make(changes, value: 990f);

            knob.BeginPress(300f);
            knob.Advance(KsTguiKnob.HoldSeconds);
            knob.DragTo(-5000f);
            Assert.That(knob.Value, Is.EqualTo(1000f));
            knob.DragTo(9000f);
            Assert.That(knob.Value, Is.EqualTo(0f));
        });
    }

    /// <summary>
    ///     With no <c>stepPixelSize</c> the knob's own width is the whole range.
    /// </summary>
    [Test]
    public async Task DefaultStepPixelSizeIsTheKnobsWidthOverTheSteps()
    {
        var changes = new List<float>();
        await Client.WaitPost(() =>
        {
            var knob = Make(changes);
            knob.StepPixelSize = null;

            knob.BeginPress(300f);
            Assert.That(knob.EffectiveStepPixelSize, Is.EqualTo(39f / 200f).Within(0.0001f));
        });
    }

    [Test]
    public async Task TickWhileDraggingReportsEveryFourTenthsOfASecond()
    {
        var changes = new List<float>();
        await Client.WaitPost(() =>
        {
            var knob = Make(changes);
            knob.TickWhileDragging = true;

            knob.BeginPress(300f);
            knob.Advance(KsTguiKnob.HoldSeconds);
            knob.DragTo(290f);
            Assert.That(changes, Is.Empty);

            knob.Advance(KsTguiKnob.TickSeconds - 0.01f);
            Assert.That(changes, Is.Empty);
            knob.Advance(0.02f);
            Assert.That(changes, Is.EqualTo(new[] { 550f }));
            knob.Advance(KsTguiKnob.TickSeconds);
            Assert.That(changes, Is.EqualTo(new[] { 550f, 550f }));

            // And without the flag a drag is silent.
            knob.Release();
            changes.Clear();
            knob.TickWhileDragging = false;
            knob.BeginPress(300f);
            knob.Advance(KsTguiKnob.HoldSeconds);
            knob.Advance(KsTguiKnob.TickSeconds * 3f);
            Assert.That(changes, Is.Empty);
        });
    }

    /// <summary>
    ///     A press let go inside a tenth of a second is a click: the box opens when that time is up, with everything selected,
    ///         and what is typed is held to the range and reported.
    /// </summary>
    [Test]
    public async Task ClickOpensTheTextBoxAndTypedValueIsCommitted()
    {
        var changes = new List<float>();
        await Client.WaitPost(() =>
        {
            var knob = Make(changes);

            knob.BeginPress(300f);
            knob.Release();
            Assert.That(knob.IsEditing, Is.False, "The box opens when the hold would have been up.");
            knob.Advance(KsTguiKnob.HoldSeconds);

            Assert.That(knob.IsEditing);
            Assert.That(knob.Edit.Visible);
            Assert.That(knob.Edit.Text, Is.EqualTo("500"));
            Assert.That(knob.Edit.SelectionLength, Is.EqualTo(knob.Edit.Text.Length));

            // tgui reports the let go of a click too, as a change to the value it already had.
            Assert.That(changes, Is.EqualTo(new[] { 500f }));

            knob.Edit.Text = "5000";
            knob.Commit();
            Assert.That(knob.IsEditing, Is.False);
            Assert.That(knob.Value, Is.EqualTo(1000f), "A typed value is held to the range.");
            Assert.That(changes, Is.EqualTo(new[] { 500f, 1000f }));
        });
    }

    [Test]
    public async Task TypedNonsenseCancelsAndUnclampedLetsThePastTheRange()
    {
        var changes = new List<float>();
        await Client.WaitPost(() =>
        {
            var knob = Make(changes);

            Click(knob);
            changes.Clear();
            knob.Edit.Text = "not a number";
            knob.Commit();
            Assert.That(knob.IsEditing, Is.False);
            Assert.That(knob.Value, Is.EqualTo(500f));
            Assert.That(changes, Is.Empty);

            knob.Unclamped = true;
            Click(knob);
            changes.Clear();
            knob.Edit.Text = "5000";
            knob.Commit();
            Assert.That(knob.Value, Is.EqualTo(5000f));
            Assert.That(changes, Is.EqualTo(new[] { 5000f }));
        });
    }

    /// <summary>
    ///     What pressing Escape raises on the focused box: it stops editing and reports nothing.
    /// </summary>
    [Test]
    public async Task EscapeInTheBoxCancels()
    {
        var changes = new List<float>();
        await Client.WaitPost(() =>
        {
            var knob = Make(changes);
            Click(knob);
            changes.Clear();
            knob.Edit.Text = "800";

            var escape = new GUIBoundKeyEventArgs(EngineKeyFunctions.TextReleaseFocus, BoundKeyState.Down, default, false, default, default);
            var field = typeof(Control).GetField(nameof(Control.OnKeyBindDown), BindingFlags.Instance | BindingFlags.NonPublic);
            ((Action<GUIBoundKeyEventArgs>)field.GetValue(knob.Edit)).Invoke(escape);

            Assert.That(escape.Handled, Is.True);
            Assert.That(knob.IsEditing, Is.False);
            Assert.That(knob.Value, Is.EqualTo(500f));
            Assert.That(changes, Is.Empty);
        });
    }

    /// <summary>
    ///     A disabled knob neither drags nor types.
    /// </summary>
    [Test]
    public async Task DisabledKnobIgnoresThePress()
    {
        var changes = new List<float>();
        await Client.WaitPost(() =>
        {
            var knob = Make(changes);
            knob.Disabled = true;

            knob.BeginPress(300f);
            Assert.That(knob.IsPressed, Is.False);
            knob.Release();
            knob.Advance(1f);
            Assert.That(knob.IsEditing, Is.False);
            Assert.That(changes, Is.Empty);
        });
    }

    /// <summary>
    ///     The box above the knob appears once the value has moved, and says it with its unit.
    /// </summary>
    [Test]
    public async Task PopupShowsTheLiveValueWithItsUnitWhileDragging()
    {
        var changes = new List<float>();
        await Client.WaitPost(() =>
        {
            var knob = Make(changes);

            knob.BeginPress(300f);
            knob.Advance(KsTguiKnob.HoldSeconds);
            Assert.That(knob.PopupVisible, Is.False, "Held, but nothing has moved.");

            knob.DragTo(290f);
            Assert.That(knob.PopupVisible);
            Assert.That(knob.PopupText, Is.EqualTo("550 kPa"));

            knob.Release();
            Assert.That(knob.PopupVisible, Is.False);

            knob.Format = value => "x" + value;
            Assert.That(knob.PopupText, Is.EqualTo("x550 kPa"));
        });
    }

    /// <summary>
    ///     The ring takes <c>color</c>, else the range the shown value is in, else the theme's; the cursor shows the value and
    ///         the ring the fill value.
    /// </summary>
    [Test]
    public async Task RingColourAndFillFollowColorRangesAndFillValue()
    {
        await Client.WaitPost(() =>
        {
            var knob = new KsTguiKnob { MinValue = 0f, MaxValue = 100f };
            knob.SetValue(25f);

            Assert.That(knob.RingColorName, Is.Null);
            Assert.That(knob.RingColor.A, Is.EqualTo(0.75f).Within(0.001f), "The theme's ring is at .75.");

            knob.Color = KsTguiColor.Yellow;
            Assert.That(knob.RingColor, Is.EqualTo(KsTguiColors.Shade(KsTguiColor.Yellow, 0f)));

            // Default is not one of the names tgui has a ring colour for.
            knob.Color = KsTguiColor.Default;
            Assert.That(knob.RingColorName, Is.Null);

            knob.Color = null;
            knob.AddRange(KsTguiColor.Red, 0f, 50f);
            knob.AddRange(KsTguiColor.Green, 50f, 100f);
            Assert.That(knob.RingColorName, Is.EqualTo(KsTguiColor.Red));

            knob.FillValue = 75f;
            Assert.That(knob.RingColorName, Is.EqualTo(KsTguiColor.Green), "The ring's range is the fill value's.");
            Assert.That(knob.FillArc.Sweep, Is.EqualTo(0.75f * 270f).Within(0.001f));
            Assert.That(knob.CursorDegrees, Is.EqualTo(-67.5f).Within(0.001f), "The cursor still shows the value.");

            knob.Bipolar = true;
            Assert.That(knob.FillArc, Is.EqualTo((270f, 67.5f)));
        });
    }

    /// <summary>
    ///     What is drawn: stays inside the control, has soft edges, ends the fill where the value is, and a fuller ring is more
    ///         triangles.
    /// </summary>
    [Test]
    public async Task DrawnGeometryEndsTheFillAtTheValueWithSoftEdges()
    {
        await Client.WaitPost(() =>
        {
            var knob = new KsTguiKnob { KnobSize = 1.25f, MinValue = 0f, MaxValue = 100f };
            const float scale = 2f;
            var side = 31.2f * 1.25f * scale;
            var pixelSize = new Vector2(side, side);
            var centre = pixelSize / 2f;
            var ringRadius = 1.13f * 12f * 1.25f * scale;

            knob.SetValue(0f);
            var emptyCount = knob.BuildVertices(scale, pixelSize, XColor.White).Count;
            Assert.That(emptyCount, Is.GreaterThan(0));

            knob.SetValue(100f);
            var fullCount = knob.BuildVertices(scale, pixelSize, XColor.White).Count;
            Assert.That(fullCount, Is.GreaterThan(emptyCount), "A fuller ring is more triangles.");

            // Half way the fill ends at 12 o'clock, the cap of its round end centred on the ring there.
            knob.SetValue(50f);
            var vertices = knob.BuildVertices(scale, pixelSize, XColor.White).ToArray();
            Assert.That(vertices.Length % 3, Is.EqualTo(0));

            var ring = knob.RingColor;
            var top = centre + new Vector2(0f, -ringRadius);
            Assert.That(vertices.Any(v => Vector2.Distance(v.Position, top) < 0.05f && Math.Abs(v.Color.A - ring.A) < 0.01f),
                "The fill's end should be at the top of the ring.");

            foreach (var vertex in vertices)
            {
                Assert.That(vertex.Position.X, Is.InRange(-12f * scale, side + 12f * scale), "Only the glow reaches past the control.");
                Assert.That(vertex.Color.A, Is.InRange(0f, 1f));
            }

            // The edges are soft: some vertices are clear, some solid.
            Assert.That(vertices.Any(v => v.Color.A <= 0.001f));
            Assert.That(vertices.Any(v => v.Color.A >= 0.99f));
        });
    }

    /// <summary>
    ///     The control is 2.6em square, and the cursor box turns the way the value says.
    /// </summary>
    [Test]
    public async Task SizeIsTwoPointSixEmsAndScalesWithTheKnobSize()
    {
        await Client.WaitPost(() =>
        {
            var knob = new KsTguiKnob { KnobSize = 1.25f };
            knob.Measure(Vector2.PositiveInfinity);
            Assert.That(knob.DesiredSize, Is.EqualTo(new Vector2(39f, 39f)));
        });
    }

    /// <summary>
    ///     The readout under the knob follows the value and the format, counts to a new value, and is live while dragging.
    /// </summary>
    [Test]
    public async Task ReadoutFollowsTheValueAndTheFormat()
    {
        var changes = new List<float>();
        await Client.WaitPost(() =>
        {
            var knob = Make(changes);
            Assert.That(knob.Readout.Visible, Is.False, "Off by default, as tgui's knob has none.");

            knob.ShowValue = true;
            Assert.That(knob.Readout.Visible);
            Assert.That(knob.Readout.Text, Is.EqualTo("500 kPa"));

            // It counts to a value it is given...
            knob.SetValue(700f);
            Assert.That(knob.Readout.Value, Is.EqualTo(700f));
            Assert.That(knob.Readout.Animating);

            knob.Format = value => "x" + value;
            Assert.That(knob.Readout.Text, Does.StartWith("x"));
            Assert.That(knob.Readout.Text, Does.EndWith(" kPa"));

            // ...but a drag shows the live value at once.
            knob.Format = null;
            knob.BeginPress(300f);
            knob.Advance(KsTguiKnob.HoldSeconds);
            knob.DragTo(290f);
            Assert.That(knob.Readout.Text, Is.EqualTo("750 kPa"));
            knob.DragTo(280f);
            Assert.That(knob.Readout.Text, Is.EqualTo("800 kPa"));
            knob.Release();
            Assert.That(knob.Readout.Text, Is.EqualTo("800 kPa"));
        });
    }

    /// <summary>
    ///     The knob grows by the readout's height, and the typing box opens in the readout's place, not over the knob.
    /// </summary>
    [Test]
    public async Task ClickingTheReadoutOpensTheEditorInItsPlaceAndCommits()
    {
        var changes = new List<float>();
        await Client.WaitPost(() =>
        {
            var knob = Make(changes);
            knob.Measure(Vector2.PositiveInfinity);
            var plainHeight = knob.DesiredSize.Y;

            knob.ShowValue = true;
            knob.Measure(Vector2.PositiveInfinity);
            Assert.That(knob.DesiredSize.Y, Is.GreaterThan(plainHeight + 8f), "The readout adds a row.");
            Assert.That(knob.DesiredSize.X, Is.GreaterThanOrEqualTo(knob.KnobSide));
            knob.Arrange(UIBox2.FromDimensions(Vector2.Zero, knob.DesiredSize));

            Assert.That(knob.IsOverReadout(knob.KnobSide - 1f), Is.False);
            Assert.That(knob.IsOverReadout(knob.KnobSide + 5f));

            knob.ClickReadout();
            Assert.That(knob.IsEditing);
            knob.Arrange(UIBox2.FromDimensions(Vector2.Zero, knob.DesiredSize));
            Assert.That(knob.Edit.Text, Is.EqualTo("500"));
            Assert.That(knob.Edit.SelectionLength, Is.EqualTo(3));
            Assert.That(knob.Edit.Position.Y, Is.GreaterThanOrEqualTo(knob.KnobSide), "Below the dial, not over it.");
            Assert.That(knob.Edit.Position.Y, Is.EqualTo(knob.Readout.Position.Y).Within(0.01f));
            Assert.That(knob.Edit.Size.X, Is.GreaterThanOrEqualTo(knob.Readout.Size.X - 0.01f));
            Assert.That(changes, Is.Empty, "Clicking the readout reports nothing until it is committed.");

            knob.Edit.Text = "250";
            knob.Commit();
            Assert.That(knob.Value, Is.EqualTo(250f));
            Assert.That(knob.Readout.Value, Is.EqualTo(250f));
            Assert.That(changes, Is.EqualTo(new[] { 250f }));
        });
    }

    /// <summary>
    ///     A click on the dial still opens the box, but with a readout it opens in the readout's place.
    /// </summary>
    [Test]
    public async Task ClickOnTheDialOpensTheEditorAtTheReadoutWhenShown()
    {
        var changes = new List<float>();
        await Client.WaitPost(() =>
        {
            var knob = Make(changes);
            knob.ShowValue = true;
            knob.Measure(Vector2.PositiveInfinity);
            knob.Arrange(UIBox2.FromDimensions(Vector2.Zero, knob.DesiredSize));

            Click(knob);
            Assert.That(knob.IsEditing);
            knob.Arrange(UIBox2.FromDimensions(Vector2.Zero, knob.DesiredSize));
            Assert.That(knob.Edit.Position.Y, Is.GreaterThanOrEqualTo(knob.KnobSide));
        });
    }

    /// <summary>
    ///     The readout already shows the live value, so the drag popup stays away.
    /// </summary>
    [Test]
    public async Task DragPopupIsHiddenWhileTheReadoutIsShown()
    {
        var changes = new List<float>();
        await Client.WaitPost(() =>
        {
            var knob = Make(changes);
            knob.ShowValue = true;

            knob.BeginPress(300f);
            knob.Advance(KsTguiKnob.HoldSeconds);
            knob.DragTo(290f);
            Assert.That(knob.PopupVisible, Is.False);

            knob.ShowValue = false;
            Assert.That(knob.PopupVisible);
        });
    }

    /// <summary>
    ///     A long number fits in the typing box, with or without a readout, and the box grows with what is typed.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TheTypingBoxFitsALongNumber(bool showValue)
    {
        var changes = new List<float>();
        await Client.WaitPost(() =>
        {
            var knob = Make(changes, value: 1013.25f);
            knob.MaxValue = 10000f;
            knob.ShowValue = showValue;
            knob.Measure(Vector2.PositiveInfinity);
            knob.Arrange(UIBox2.FromDimensions(Vector2.Zero, knob.DesiredSize));

            Click(knob);
            Assert.That(knob.IsEditing);
            knob.Arrange(UIBox2.FromDimensions(Vector2.Zero, knob.DesiredSize));
            Assert.That(knob.Edit.Text, Is.EqualTo(1013.25f.ToString(System.Globalization.CultureInfo.CurrentCulture)));
            var (room, needed) = knob.EditTextFit;
            Assert.That(room, Is.GreaterThanOrEqualTo(needed), $"\"{knob.Edit.Text}\" fits.");

            // Typed, as a player would: a plain Text = ... raises no change, and typing does.
            knob.Edit.Text = "";
            knob.Edit.InsertAtCursor("12345.678");
            knob.Arrange(UIBox2.FromDimensions(Vector2.Zero, knob.DesiredSize));
            (room, needed) = knob.EditTextFit;
            Assert.That(room, Is.GreaterThanOrEqualTo(needed), "A longer number typed in still fits.");

            // It grows to the left: its right edge stays where the knob's is.
            Assert.That(knob.Edit.Position.X + knob.Edit.Size.X, Is.EqualTo(knob.DesiredSize.X).Within(0.01f));
        });
    }

    private static void Click(KsTguiKnob knob)
    {
        knob.BeginPress(0f);
        knob.Release();
        knob.Advance(KsTguiKnob.HoldSeconds);
    }

    /// <summary>
    ///     The canister's regulator: the knob's release is the pressure the window sends, and its ring is yellow while the valve is open.
    /// </summary>
    [Test]
    public async Task CanisterWindowSendsTheReleasedPressureAndTurnsYellowWithTheValve()
    {
        var sent = new List<float>();
        await Client.WaitPost(() =>
        {
            var window = new KsTguiGasCanisterWindow();
            window.ReleasePressureSet += sent.Add;
            window.SetReleasePressureRange(10f, 1000f);
            window.SetReleasePressure(100f);

            var knob = window.ReleaseKnob;
            Assert.That(knob.Value, Is.EqualTo(100f));
            Assert.That(knob.RingColorName, Is.Null, "The valve is closed.");

            knob.BeginPress(300f);
            knob.Advance(KsTguiKnob.HoldSeconds);
            knob.DragTo(290f);
            Assert.That(sent, Is.Empty, "Nothing is sent while dragging.");
            knob.Release();
            Assert.That(sent, Is.EqualTo(new[] { 150f }));

            window.SetReleaseValve(true);
            Assert.That(knob.RingColorName, Is.EqualTo(KsTguiColor.Yellow));
            window.SetReleaseValve(false);
            Assert.That(knob.RingColorName, Is.Null);

            // The window shows the pressure the server sends when the player is not dragging.
            window.SetReleasePressure(300f);
            Assert.That(knob.Value, Is.EqualTo(300f));
            window.Dispose();
        });
    }
}
