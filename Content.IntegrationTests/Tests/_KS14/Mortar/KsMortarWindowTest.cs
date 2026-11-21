#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Content.Client._KS14.Mortar;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Mortar;
using NUnit.Framework;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Mortar;

/// <summary>
///     <see cref="KsMortarWindow"/> and its <see cref="KsMortarAimControl"/>, headless: a click on the plot aims where it is,
///         kept to the band; input that will not do stops Fire at once and says why; the plot and the fields are one aim;
///         and a refresh from the server leaves what the player is typing alone.
/// </summary>
[TestFixture]
[TestOf(typeof(KsMortarWindow))]
[TestOf(typeof(KsMortarAimControl))]
public sealed class KsMortarWindowTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private const float MinRange = 30f;
    private const float MaxRange = 50f;

    private static KsMortarBuiState State(int rounds = 5)
    {
        return new KsMortarBuiState(rounds, MinRange, MaxRange, spread: 1.5f, readyAt: TimeSpan.Zero, reloadTime: TimeSpan.FromSeconds(6),
            impacts: new List<Vector2> { new(0f, 40f), new(10f, 35f) }, sightRadius: 0f, sightEyes: new Dictionary<NetEntity, NetEntity>());
    }

    private async Task<KsMortarWindow> OpenWindow()
    {
        KsMortarWindow window = default!;
        await Client.WaitPost(() =>
        {
            window = new KsMortarWindow();
            window.UpdateState(State());
            window.OpenCentered();
        });
        await Pair.RunTicksSync(5);
        return window;
    }

    private async Task Click(Control control, Vector2 relativePosition)
    {
        foreach (var state in new[] { BoundKeyState.Down, BoundKeyState.Up })
        {
            var pixel = relativePosition * control.UIScale;
            var args = new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, state,
                new ScreenCoordinates(control.GlobalPixelPosition + pixel, default), true, relativePosition, pixel);
            await Client.DoGuiEvent(control, args);
        }
    }

    /// <summary>
    ///     Where a point is on the plot, worked out from what the plot is, not from its own code: north up, the outer ring
    ///         <see cref="MaxRange"/> tiles out, <see cref="KsMortarAimControl.RingMargin"/> in from its nearer edge; the
    ///         inner ring, <see cref="MinRange"/>, at <see cref="KsMortarAimControl.InnerRadiusFraction"/> of that, the band
    ///         spread evenly between them and the inside evenly inside it.
    /// </summary>
    private static Vector2 PlotPoint(KsMortarAimControl plot, double bearingDegrees, float distance)
    {
        var outer = MathF.Min(plot.Size.X, plot.Size.Y) / 2f - KsMortarAimControl.RingMargin;
        var inner = outer * KsMortarAimControl.InnerRadiusFraction;
        var radius = distance <= MinRange
            ? inner * distance / MinRange
            : inner + (outer - inner) * (distance - MinRange) / (MaxRange - MinRange);
        var theta = bearingDegrees * Math.PI / 180d;
        var direction = new Vector2((float) Math.Sin(theta), (float) -Math.Cos(theta));
        return plot.Size / 2f + direction * radius;
    }

    [Test]
    public async Task AClickOnThePlotAimsThere()
    {
        var window = await OpenWindow();

        await Client.WaitAssertion(() => Assert.That(window.AimPlot.Size.X, Is.GreaterThan(KsMortarAimControl.RingMargin * 4f),
            "precondition: the plot is laid out"));

        await Click(window.AimPlot, PlotPoint(window.AimPlot, 135, 40f));
        await Client.WaitAssertion(() =>
        {
            Assert.That(window.BearingEdit.Text, Is.EqualTo("135"), "a click south-east of the mortar should aim south-east");
            Assert.That(window.DistanceEdit.Text, Is.EqualTo("40"), "a click halfway across the band should aim halfway through the range");
            Assert.That(window.InputValid, Is.True);
        });

        // Inside the band's inner ring: kept to its least range, the bearing as clicked.
        await Click(window.AimPlot, PlotPoint(window.AimPlot, 270, MinRange / 4f));
        await Client.WaitAssertion(() =>
        {
            Assert.That(window.BearingEdit.Text, Is.EqualTo("270"));
            Assert.That(window.DistanceEdit.Text, Is.EqualTo(MinRange.ToString()), "a click short of the band should aim at its least range");
        });

        // Past the outer ring, in the plot's corner.
        await Click(window.AimPlot, new Vector2(1f, 1f));
        await Client.WaitAssertion(() =>
        {
            Assert.That(window.BearingEdit.Text, Is.EqualTo("315"));
            Assert.That(window.DistanceEdit.Text, Is.EqualTo(MaxRange.ToString()), "a click past the band should aim at its furthest");
            window.Close();
        });
    }

    [Test]
    public async Task InputThatWillNotDoStopsFireAtOnce()
    {
        var window = await OpenWindow();

        await Client.WaitAssertion(() =>
        {
            Assert.That(window.FireButton.Disabled, Is.False, "precondition: loaded, with rounds, aimed within range");

            window.DistanceEdit.SetText((MaxRange + 10f).ToString(), invokeEvent: true);
            Assert.That(window.FireButton.Disabled, Is.True, "a distance past the range should stop Fire as it is typed");
            Assert.That(window.DistanceInvalid, Is.True, "the distance field should be marked");
            Assert.That(window.DistanceEdit.ModulateSelfOverride, Is.Not.Null, "the distance field should turn red");
            Assert.That(window.ReasonLabel.Text!.Trim(), Is.Not.Empty, "it should say why Fire is off");

            window.DistanceEdit.SetText("40", invokeEvent: true);
            Assert.That(window.FireButton.Disabled, Is.False, "back in range, Fire should be on again");
            Assert.That(window.ReasonLabel.Text!.Trim(), Is.Empty);

            window.BearingEdit.SetText("north", invokeEvent: true);
            Assert.That(window.FireButton.Disabled, Is.True, "a bearing that is no number should stop Fire");
            Assert.That(window.BearingInvalid, Is.True);
            Assert.That(window.ReasonLabel.Text!.Trim(), Is.Not.Empty);

            window.BearingEdit.SetText("400", invokeEvent: true);
            Assert.That(window.FireButton.Disabled, Is.True, "a bearing past 360 should stop Fire");

            window.BearingEdit.SetText("90", invokeEvent: true);
            Assert.That(window.FireButton.Disabled, Is.False);

            window.UpdateState(State(rounds: 0));
            Assert.That(window.FireButton.Disabled, Is.True, "with no rounds Fire should be off");
            window.Close();
        });
    }

    [Test]
    public async Task ThePlotAndTheFieldsAreOneAim()
    {
        var window = await OpenWindow();

        await Client.WaitAssertion(() =>
        {
            window.BearingEdit.SetText("90", invokeEvent: true);
            window.DistanceEdit.SetText("45", invokeEvent: true);
            Assert.That(window.AimPlot.Bearing.Degrees, Is.EqualTo(90d).Within(1e-6), "typing a bearing should move the plot's aim");
            Assert.That(window.AimPlot.Distance, Is.EqualTo(45f).Within(1e-6), "typing a distance should move the plot's aim");
            Assert.That(window.BearingReadout.Text, Does.StartWith("090°"), "the readout should give the bearing in three figures");
        });

        await Click(window.AimPlot, PlotPoint(window.AimPlot, 0, 35f));
        await Client.WaitAssertion(() =>
        {
            Assert.That(window.BearingEdit.Text, Is.EqualTo("0"), "the plot should write its aim into the fields");
            Assert.That(window.DistanceEdit.Text, Is.EqualTo("35"));
            Assert.That(window.AimPlot.Bearing.Degrees, Is.EqualTo(0d).Within(1e-6));
            Assert.That(window.AimPlot.Distance, Is.EqualTo(35f).Within(1e-6));
        });

        await Click(window.BearingDownButton, Vector2.One);
        await Click(window.DistanceUpButton, Vector2.One);
        await Client.WaitAssertion(() =>
        {
            Assert.That(window.BearingEdit.Text, Is.EqualTo("359"), "a nudge down from north should go round to 359");
            Assert.That(window.DistanceEdit.Text, Is.EqualTo("36"), "a nudge up should add a tile");
            Assert.That(window.AimPlot.Bearing.Degrees, Is.EqualTo(359d).Within(1e-6), "a nudge should move the plot's aim too");
            Assert.That(window.AimPlot.Distance, Is.EqualTo(36f).Within(1e-6));

            Assert.That(KsMortarWindow.BearingNudgeDegrees(300f, KsMortarWindow.NudgeStep) * Math.PI / 180d * 300d,
                Is.EqualTo(KsMortarWindow.NudgeStep).Within(0.05d), "at long range a bearing nudge should move the aim about a tile");

            // Halfway through typing a number: a refresh must not take it away.
            window.DistanceEdit.SetText("4", invokeEvent: true);
            window.UpdateState(State(rounds: 4));
            Assert.That(window.DistanceEdit.Text, Is.EqualTo("4"), "a refresh from the server must leave what is typed");
            Assert.That(window.BearingEdit.Text, Is.EqualTo("359"));
            Assert.That(window.FireButton.Disabled, Is.True, "precondition: halfway through typing, out of range");
            window.Close();
        });
    }

    /// <summary>
    ///     The cross of nudge buttons moves the point aimed at along the map, a step at a time, the bearing and distance
    ///         following, kept to the band.
    /// </summary>
    [Test]
    public async Task TheCrossMovesThePointAimedAt()
    {
        var window = await OpenWindow();
        var step = KsMortarWindow.NudgeStep;

        await Client.WaitPost(() =>
        {
            window.BearingEdit.SetText("0", invokeEvent: true);
            window.DistanceEdit.SetText("40", invokeEvent: true);
        });

        await Click(window.NudgeEastButton, Vector2.One);
        await Client.WaitAssertion(() =>
        {
            var expected = Math.Atan2(step, 40d) * 180d / Math.PI;
            Assert.That(double.Parse(window.BearingEdit.Text, System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(expected).Within(0.01d),
                "a step east from due north should turn the bearing east by as much");
            Assert.That(float.Parse(window.DistanceEdit.Text, System.Globalization.CultureInfo.InvariantCulture),
                Is.EqualTo(MathF.Sqrt(40f * 40f + step * step)).Within(0.05f), "and lengthen the distance by as little");
            Assert.That(window.AimPlot.Bearing.Degrees, Is.EqualTo(expected).Within(0.01d), "the plot should follow the cross");
        });

        await Click(window.NudgeWestButton, Vector2.One);
        await Click(window.NudgeSouthButton, Vector2.One);
        await Client.WaitAssertion(() =>
        {
            Assert.That(window.BearingEdit.Text, Is.EqualTo("0"), "west again should be back on north");
            Assert.That(float.Parse(window.DistanceEdit.Text, System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(40f - step).Within(0.05f),
                "a step south from due north should shorten the distance by a step");
            window.DistanceEdit.SetText(MinRange.ToString(), invokeEvent: true);
        });

        await Click(window.NudgeSouthButton, Vector2.One);
        await Client.WaitAssertion(() =>
        {
            Assert.That(window.DistanceEdit.Text, Is.EqualTo(MinRange.ToString()), "a step inside the least range should be kept to it");
            Assert.That(window.FireButton.Disabled, Is.False);
            window.Close();
        });
    }
}
