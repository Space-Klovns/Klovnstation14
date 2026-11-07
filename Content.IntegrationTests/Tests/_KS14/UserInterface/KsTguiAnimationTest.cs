using System.Collections.Generic;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui.Controls;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Robust.Client.UserInterface.Controls;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     What moves in tgui: the round gauge's needle and alert, and the animated number wherever it stands in for a plain
///         one. Times and curves are tgui-core's own, out of RoundGauge.scss and vars-components.scss.
/// </summary>
[TestOf(typeof(KsTguiRoundGauge))]
public sealed class KsTguiAnimationTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static KsTguiRoundGauge MakeGauge(float value)
    {
        var gauge = new KsTguiRoundGauge { MinValue = 0f, MaxValue = 100f, Value = value };
        gauge.Advance(0.016f);
        return gauge;
    }

    [Test]
    public async Task CubicBezierMatchesCss()
    {
        await Client.WaitPost(() =>
        {
            Assert.That(KsTguiRoundGauge.CubicBezier(0f, 0f, 1f, 1f, 0.3f), Is.EqualTo(0.3f).Within(0.001f), "the identity curve");
            Assert.That(KsTguiRoundGauge.NeedleEase(0.5f), Is.EqualTo(0.5f).Within(0.001f), "ease-in-out is symmetric");
            Assert.That(KsTguiRoundGauge.NeedleEase(0.25f), Is.EqualTo(0.1292f).Within(0.002f), "ease-in-out starts slowly");
            Assert.That(KsTguiRoundGauge.CubicBezier(0.25f, 0.1f, 0.25f, 1f, 0.5f), Is.EqualTo(0.8024f).Within(0.002f), "ease at its middle");
            Assert.That(KsTguiRoundGauge.CubicBezier(0.34f, 1.56f, 0.64f, 1f, 0.5f), Is.GreaterThan(1f), "the alert's curve overshoots");
            Assert.That(KsTguiRoundGauge.NeedleEase(0f), Is.EqualTo(0f));
            Assert.That(KsTguiRoundGauge.NeedleEase(1f), Is.EqualTo(1f));
        });
    }

    [Test]
    public async Task NeedleTakesAFifthOfASecondToAValue()
    {
        await Client.WaitPost(() =>
        {
            var gauge = MakeGauge(0f);
            Assert.That(gauge.NeedleShown, Is.EqualTo(-90f));

            gauge.Value = 100f;
            Assert.That(gauge.NeedleShown, Is.EqualTo(-90f), "A new value only starts the needle moving.");
            Assert.That(gauge.NeedleMoving, Is.True);

            gauge.Advance(0.05f);
            Assert.That(gauge.NeedleShown, Is.EqualTo(-90f + 180f * KsTguiRoundGauge.NeedleEase(0.25f)).Within(0.01f), "A quarter of the way through, on the curve.");

            gauge.Advance(0.05f);
            Assert.That(gauge.NeedleShown, Is.EqualTo(0f).Within(0.2f), "Half way through it is half way there.");

            gauge.Advance(0.11f);
            Assert.That(gauge.NeedleShown, Is.EqualTo(90f));
            Assert.That(gauge.NeedleMoving, Is.False);
        });
    }

    /// <summary>
    ///     tgui draws its first value in place. A window sets the value and the scale together, in either order, and the
    ///         needle shouldn't swing across the dial as the window opens.
    /// </summary>
    [Test]
    public async Task FirstValueIsDrawnInPlace()
    {
        await Client.WaitPost(() =>
        {
            var gauge = new KsTguiRoundGauge { MinValue = 0f, Value = 50f };
            gauge.MaxValue = 100f;
            gauge.Advance(0.016f);

            Assert.That(gauge.NeedleShown, Is.EqualTo(0f));
            Assert.That(gauge.NeedleMoving, Is.False);
        });
    }

    [Test]
    public async Task ANewValueMidwayTurnsTheNeedleFromWhereItIs()
    {
        await Client.WaitPost(() =>
        {
            var gauge = MakeGauge(0f);
            gauge.Value = 100f;
            gauge.Advance(0.1f);
            var midway = gauge.NeedleShown;

            gauge.Value = 0f;
            Assert.That(gauge.NeedleShown, Is.EqualTo(midway), "It carries on from where it got to, without a jump.");

            gauge.Advance(0.1f);
            Assert.That(gauge.NeedleShown, Is.EqualTo((midway - 90f) / 2f).Within(0.2f));

            gauge.Advance(0.11f);
            Assert.That(gauge.NeedleShown, Is.EqualTo(-90f));
        });
    }

    [Test]
    public async Task ShakeAndPulseFollowTheirKeyframes()
    {
        await Client.WaitPost(() =>
        {
            Assert.That(KsTguiRoundGauge.ShakeDegrees(0f), Is.EqualTo(2.5f).Within(0.001f), "0% is +2.5 degrees");
            Assert.That(KsTguiRoundGauge.ShakeDegrees(0.05f), Is.EqualTo(-2.5f).Within(0.001f), "50% is -2.5 degrees");
            Assert.That(KsTguiRoundGauge.ShakeDegrees(0.0999f), Is.EqualTo(0f).Within(0.05f), "100% has no keyframe, so it is the needle's own line");
            Assert.That(KsTguiRoundGauge.ShakeDegrees(0.1001f), Is.EqualTo(2.5f).Within(0.05f), "and the next cycle jumps back to the start");

            Assert.That(KsTguiRoundGauge.AlertOpacity(0f), Is.EqualTo(0.1f).Within(0.001f));
            Assert.That(KsTguiRoundGauge.AlertOpacity(0.5f), Is.EqualTo(1f).Within(0.001f));
            Assert.That(KsTguiRoundGauge.AlertOpacity(0.999f), Is.EqualTo(0.1f).Within(0.02f));

            for (var seconds = 0f; seconds < 2f; seconds += 0.01f)
            {
                Assert.That(KsTguiRoundGauge.AlertOpacity(seconds), Is.InRange(0f, 1f), "The curve overshoots, and an opacity can't.");
            }
        });
    }

    [Test]
    public async Task OnlyAnActiveAlertShakesAndPulses()
    {
        await Client.WaitPost(() =>
        {
            var gauge = MakeGauge(10f);
            gauge.AlertAfter = 70f;
            gauge.AddRange(KsTguiColor.Good, 0f, 70f);
            gauge.AddRange(KsTguiColor.Bad, 70f, 100f);

            gauge.Advance(0.3f);
            Assert.That(gauge.AlertActive, Is.False);
            Assert.That(gauge.NeedleDrawn, Is.EqualTo(gauge.NeedleShown), "Out of alert the needle holds still.");

            gauge.Value = 90f;
            gauge.Advance(0.25f);
            Assert.That(gauge.AlertActive, Is.True);
            Assert.That(gauge.AlertOpacityNow, Is.EqualTo(0.1f).Within(0.001f), "The first frame in alert starts the animation from its start.");
            Assert.That(gauge.NeedleDrawn - gauge.NeedleShown, Is.EqualTo(2.5f).Within(0.001f));

            gauge.Advance(0.05f);
            Assert.That(gauge.NeedleDrawn - gauge.NeedleShown, Is.EqualTo(-2.5f).Within(0.001f));

            gauge.Advance(0.45f);
            Assert.That(gauge.AlertOpacityNow, Is.EqualTo(1f).Within(0.001f));

            gauge.Value = 10f;
            gauge.Advance(0.25f);
            Assert.That(gauge.AlertActive, Is.False);
            Assert.That(gauge.NeedleDrawn, Is.EqualTo(gauge.NeedleShown), "Leaving the alert stops the shake.");

            gauge.Value = 90f;
            gauge.Advance(0.25f);
            Assert.That(gauge.AlertOpacityNow, Is.EqualTo(0.1f).Within(0.001f), "A new alert starts over.");
        });
    }

    [Test]
    public async Task NoRangesMeansTheWholeDialIsOneRange()
    {
        await Client.WaitPost(() =>
        {
            var gauge = MakeGauge(90f);
            gauge.AlertAfter = 70f;

            Assert.That(gauge.MatchingRange(), Is.EqualTo(KsTguiColor.Primary));
            Assert.That(gauge.AlertActive, Is.True);
        });
    }

    [Test]
    public async Task InstantNumberSkipsTheEase()
    {
        await Client.WaitPost(() =>
        {
            var number = new KsTguiAnimatedNumber { Value = 10f };
            number.Value = 20f;
            Assert.That(number.Animating, Is.True);
            Assert.That(number.Displayed, Is.EqualTo(10f));

            number.Instant = true;
            Assert.That(number.Animating, Is.False, "Turning it on finishes an ease already under way.");
            Assert.That(number.Displayed, Is.EqualTo(20f));

            number.Value = 30f;
            Assert.That(number.Animating, Is.False);
            Assert.That(number.Text, Is.EqualTo("30"));
        });
    }

    [Test]
    public async Task TextInPlaceOfANumberStartsOver()
    {
        await Client.WaitPost(() =>
        {
            var number = new KsTguiAnimatedNumber { Initial = 0f, Value = 40f };
            Assert.That(number.Animating, Is.True);

            number.ShowText("N/A");
            Assert.That(number.Text, Is.EqualTo("N/A"));
            Assert.That(number.Animating, Is.False, "Nothing should write over the text on a later frame.");

            number.Value = 40f;
            Assert.That(number.Animating, Is.True, "The same number coming back is a new one, from its initial value.");
            Assert.That(number.Displayed, Is.EqualTo(0f));
        });
    }

    [Test]
    public async Task PoolKeepsANumberAcrossARebuild()
    {
        await Client.WaitPost(() =>
        {
            var created = new List<KsTguiAnimatedNumber>();
            var pool = new KsTguiAnimatedNumberPool<string>(() =>
            {
                var number = new KsTguiAnimatedNumber();
                created.Add(number);
                return number;
            });

            var firstRow = new BoxContainer();
            var water = pool.Get("water");
            var ethanol = pool.Get("ethanol");
            firstRow.AddChild(water);
            pool.Sweep();

            var secondRow = new BoxContainer();
            Assert.That(pool.Get("water"), Is.SameAs(water));
            Assert.That(water.Parent, Is.Null, "It should come out of the row it was in, so it can go in the new one.");
            secondRow.AddChild(water);
            pool.Sweep();

            Assert.That(pool.Get("ethanol"), Is.Not.SameAs(ethanol), "A row that went away and came back is a new row.");
            Assert.That(created, Has.Count.EqualTo(3));
        });
    }

    [Test]
    public async Task ProgressBarCanShowAnEasingNumber()
    {
        await Client.WaitPost(() =>
        {
            var bar = new KsTguiProgressBar { Value = 0.5f };
            Assert.That(bar.ShownText, Is.EqualTo("50%"));

            bar.ShowNumber(40f, shown => $"{shown}/100", initial: 0f);
            Assert.That(bar.ShownText, Is.EqualTo("0/100"), "It starts from its initial value.");

            bar.Value = 0.7f;
            Assert.That(bar.ShownText, Is.EqualTo("0/100"), "The fill moving is no reason to go back to a percentage.");

            bar.Text = "empty";
            Assert.That(bar.ShownText, Is.EqualTo("empty"));
        });
    }
}
