using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui.Controls;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     The ImageButton, the grid that shares the width out among its cards, the round gauge, the animated number, the
///         dimmer and the modal. Nothing headless draws, so these pin layout, state and the numbers behind the drawing.
/// </summary>
[TestOf(typeof(KsTguiCardGrid))]
public sealed class KsTguiCardControlsTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    /// <summary>
    ///     Wherever the width falls, the cards fill it: the first touches the left edge, the last of every full row the right,
    ///         the gaps are the same everywhere, and all the cards are as wide as each other.
    /// </summary>
    [TestCase(50)]
    [TestCase(200)]
    [TestCase(301)]
    [TestCase(450)]
    [TestCase(599)]
    [TestCase(777)]
    [TestCase(1003)]
    public async Task CardGridFillsItsWidth(int width)
    {
        const float gap = 6f;
        KsTguiCardGrid grid = null;
        var cards = new List<KsTguiImageButton>();

        await Client.WaitPost(() =>
        {
            grid = new KsTguiCardGrid { Gap = gap, VerticalGap = gap, HorizontalExpand = true };
            for (var i = 0; i < 40; i++)
            {
                var card = new KsTguiImageButton { Text = "Card " + i, TopRightText = "x" + i };
                cards.Add(card);
                grid.AddChild(card);
            }

            grid.Measure(new Vector2(width, 4000f));
            grid.Arrange(new UIBox2(0f, 0f, width, grid.DesiredSize.Y));
        });

        await Client.WaitAssertion(() =>
        {
            var minimum = cards[0].CardWidth;
            var columns = Math.Max(1, (int)MathF.Floor((width + gap) / (minimum + gap)));
            var rows = cards.GroupBy(card => (int)MathF.Round(card.Position.Y)).ToList();
            Assert.That(rows.First().Count(), Is.EqualTo(columns), "as many columns as fit the narrowest card");

            foreach (var row in rows.Where(row => row.Count() == columns))
            {
                var ordered = row.OrderBy(card => card.Position.X).ToList();
                Assert.That(ordered.First().Position.X, Is.EqualTo(0f).Within(1f), "flush with the left edge");
                Assert.That(ordered.Last().Position.X + ordered.Last().Size.X, Is.EqualTo(width).Within(1f), "flush with the right edge");

                for (var i = 1; i < ordered.Count; i++)
                {
                    var between = ordered[i].Position.X - (ordered[i - 1].Position.X + ordered[i - 1].Size.X);
                    Assert.That(between, Is.EqualTo(gap).Within(1f), "the same gap between every pair");
                }

                Assert.That(ordered.Select(card => card.Size.X).Max() - ordered.Select(card => card.Size.X).Min(), Is.LessThanOrEqualTo(1f), "equal cards");
            }

            Assert.That(cards.All(card => card.Size.X >= Math.Min(minimum, width)), Is.True, "no card is narrower than its minimum");
        });
    }

    [Test]
    public async Task ImageButtonSizesAsTgui()
    {
        KsTguiImageButton card = null;
        KsTguiImageButton empty = null;
        KsTguiImageButton row = null;

        await Client.WaitPost(() =>
        {
            card = new KsTguiImageButton { Text = "Name" };
            empty = new KsTguiImageButton();
            row = new KsTguiImageButton { Fluid = true, ImageSize = 32f, Text = "Name" };
            card.Measure(new Vector2(1000f, 1000f));
            empty.Measure(new Vector2(1000f, 1000f));
            row.Measure(new Vector2(300f, 1000f));
        });

        await Client.WaitAssertion(() =>
        {
            Assert.That(card.DesiredSize.X, Is.EqualTo(72f), "the picture, 3px padding either side and a 1px border");
            Assert.That(card.DesiredSize.Y, Is.GreaterThan(64f + 7f), "the bar sits under the picture");
            Assert.That(empty.DesiredSize, Is.EqualTo(new Vector2(72f, 72f)), "no bar: picture, padding and an all-round border");
            Assert.That(row.DesiredSize.Y, Is.GreaterThanOrEqualTo(32f + 2f));
            Assert.That(row.HorizontalExpand, Is.True, "a row fills the width");
        });
    }

    [Test]
    public async Task GaugeAngleClampsAndRangesMatch()
    {
        await Client.WaitAssertion(() =>
        {
            Assert.That(KsTguiRoundGauge.NeedleDegrees(0f, 0f, 100f), Is.EqualTo(-90f));
            Assert.That(KsTguiRoundGauge.NeedleDegrees(50f, 0f, 100f), Is.EqualTo(0f));
            Assert.That(KsTguiRoundGauge.NeedleDegrees(100f, 0f, 100f), Is.EqualTo(90f));
            Assert.That(KsTguiRoundGauge.NeedleDegrees(-30f, 0f, 100f), Is.EqualTo(-90f), "under the minimum stops at the left");
            Assert.That(KsTguiRoundGauge.NeedleDegrees(1e9f, 0f, 100f), Is.EqualTo(90f), "over the maximum stops at the right");
            Assert.That(KsTguiRoundGauge.NeedleDegrees(float.NaN, 0f, 100f), Is.EqualTo(-90f), "not a number reads as empty");
            Assert.That(KsTguiRoundGauge.NeedleDegrees(float.PositiveInfinity, 0f, 100f), Is.EqualTo(90f));
            Assert.That(KsTguiRoundGauge.NeedleDegrees(5f, 10f, 10f), Is.EqualTo(-90f), "a dial with no span reads as empty");
            Assert.That(KsTguiRoundGauge.NeedleDegrees(25f, 20f, 30f), Is.EqualTo(0f), "a minimum above zero is honoured");

            var (start, end) = KsTguiRoundGauge.RangeFractions(70f, 150f, 0f, 100f);
            Assert.That(start, Is.EqualTo(0.7f).Within(0.0001f));
            Assert.That(end, Is.EqualTo(1f), "a range past the dial is cut off at its end");

            var gauge = new KsTguiRoundGauge { MinValue = 0f, MaxValue = 100f, Value = 90f, AlertAfter = 70f };
            gauge.AddRange(KsTguiColor.Good, 0f, 70f);
            gauge.AddRange(KsTguiColor.Average, 70f, 85f);
            gauge.AddRange(KsTguiColor.Bad, 85f, 100f);
            Assert.That(gauge.MatchingRange(), Is.EqualTo(KsTguiColor.Bad));
            Assert.That(gauge.InAlert, Is.True);

            gauge.Value = 20f;
            Assert.That(gauge.MatchingRange(), Is.EqualTo(KsTguiColor.Good));
            Assert.That(gauge.InAlert, Is.False);
        });
    }

    [Test]
    public async Task AnimatedNumberEasesAndThenStops()
    {
        await Client.WaitAssertion(() =>
        {
            // A step is by time: two half-steps come to what one whole step does.
            var whole = KsTguiAnimatedNumber.Step(0f, 100f, 1f / 30f, out _);
            var half = KsTguiAnimatedNumber.Step(KsTguiAnimatedNumber.Step(0f, 100f, 1f / 60f, out _), 100f, 1f / 60f, out _);
            Assert.That(half, Is.EqualTo(whole).Within(0.01f), "the same easing at any frame rate");

            var value = 0f;
            var settled = false;
            var steps = 0;
            while (!settled && steps++ < 1000)
                value = KsTguiAnimatedNumber.Step(value, 100f, 1f / 60f, out settled);

            Assert.That(settled, Is.True, "it arrives");
            Assert.That(value, Is.EqualTo(100f), "and ends exactly on the target");
            Assert.That(steps, Is.LessThan(200));

            var number = new KsTguiAnimatedNumber { Initial = 0f };
            number.Value = 10f;
            Assert.That(number.Animating, Is.True);
            Assert.That(number.Displayed, Is.EqualTo(0f), "the first value starts from Initial");

            number.Value = 10f;
            Assert.That(number.Animating, Is.True, "the same target changes nothing");

            var plain = new KsTguiAnimatedNumber { Value = 4.5f };
            Assert.That(plain.Animating, Is.False, "without Initial the first value is shown at once");
            Assert.That(plain.Text, Is.EqualTo(4.5f.ToString("F1")), "with the decimals it was given");
            Assert.That(KsTguiAnimatedNumber.DecimalsOf(12f), Is.EqualTo(0));
            Assert.That(KsTguiAnimatedNumber.DecimalsOf(0.125f), Is.EqualTo(3));
        });
    }

    [Test]
    public async Task DimmerCoversAndBlocksAndModalClosesOnEscape()
    {
        Control host = null;
        Button underneath = null;
        KsTguiDimmer dimmer = null;
        KsTguiModal modal = null;
        var closed = 0;

        await Client.WaitPost(() =>
        {
            underneath = new Button { Text = "Under", MinSize = new Vector2(200f, 100f) };
            host = KsTguiDimmer.Over(underneath, out dimmer);
            dimmer.Content = new Label { Text = "Locked" };
            dimmer.Visible = true;

            modal = new KsTguiModal { Body = new Label { Text = "Form" } };
            modal.Closed += () => closed++;
            host.AddChild(modal);

            host.Measure(new Vector2(400f, 300f));
            host.Arrange(new UIBox2(0f, 0f, 400f, 300f));
        });

        await Client.WaitAssertion(() =>
        {
            Assert.That(dimmer.Size, Is.EqualTo(host.Size), "the dimmer covers everything under it");
            Assert.That(dimmer.MouseFilter, Is.EqualTo(Control.MouseFilterMode.Stop), "and takes the clicks");
            Assert.That(modal.IsOpen, Is.False, "a modal starts closed");
        });

        await Client.WaitPost(() => modal.Open());
        await Client.WaitAssertion(() =>
        {
            Assert.That(modal.IsOpen, Is.True);
            Assert.That(modal.HandleKeyFunction(EngineKeyFunctions.Use), Is.False, "other keys do not close it");
            Assert.That(modal.IsOpen, Is.True);
            Assert.That(modal.HandleKeyFunction(EngineKeyFunctions.TextReleaseFocus), Is.True);
            Assert.That(modal.IsOpen, Is.False, "Escape closes it");
            Assert.That(closed, Is.EqualTo(1));
            Assert.That(modal.HandleKeyFunction(EngineKeyFunctions.TextReleaseFocus), Is.False, "and a closed one ignores it");
        });
    }

    /// <summary>
    ///     The engine throws drawing an atlas of an atlas, which headless never does, so only the nesting can be checked.
    /// </summary>
    [Test]
    public async Task CroppingAFrameDoesNotNestAtlases()
    {
        await Client.WaitAssertion(() =>
        {
            var source = Texture.White;
            var frame = new AtlasTexture(source, UIBox2.FromDimensions(1f, 1f, 8f, 8f));
            var crop = (AtlasTexture)KsTguiImageButton.CropTexture(frame, UIBox2.FromDimensions(1f, 2f, 3f, 4f));

            Assert.That(crop.SourceTexture, Is.SameAs(source));
            Assert.That(crop.SubRegion, Is.EqualTo(UIBox2.FromDimensions(2f, 3f, 3f, 4f)));
        });
    }
}
