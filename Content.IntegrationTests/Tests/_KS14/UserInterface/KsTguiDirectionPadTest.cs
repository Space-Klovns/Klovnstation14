#nullable enable
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui.Controls;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using NUnit.Framework;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     <see cref="KsTguiDirectionPad"/>: each of its buttons, pressed, says its own direction; a disabled one says nothing;
///         the middle one is there only when given something to show; and its eight arrows are one size under both themes.
/// </summary>
[TestFixture]
[TestOf(typeof(KsTguiDirectionPad))]
public sealed class KsTguiDirectionPadTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly Vector2i[] Directions =
    {
        new(-1, 1), new(0, 1), new(1, 1),
        new(-1, 0), new(1, 0),
        new(-1, -1), new(0, -1), new(1, -1),
    };

    private async Task<(DefaultWindow Window, KsTguiDirectionPad Pad)> Open()
    {
        DefaultWindow window = default!;
        KsTguiDirectionPad pad = default!;
        await Client.WaitPost(() =>
        {
            pad = new KsTguiDirectionPad();
            window = new DefaultWindow();
            window.Contents.AddChild(pad);
            window.OpenCentered();
        });
        await Pair.RunTicksSync(5);
        return (window, pad);
    }

    private async Task Press(Control control)
    {
        foreach (var state in new[] { BoundKeyState.Down, BoundKeyState.Up })
        {
            var args = new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, state,
                new ScreenCoordinates(control.GlobalPixelPosition + Vector2.One, default), true, Vector2.One, Vector2.One);
            await Client.DoGuiEvent(control, args);
        }
    }

    [Test]
    public async Task EachButtonSaysItsDirection()
    {
        var (window, pad) = await Open();
        var pressed = new List<Vector2i>();
        await Client.WaitPost(() => pad.OnDirectionPressed += pressed.Add);

        foreach (var direction in Directions)
        {
            pressed.Clear();
            await Press(pad.GetButton(direction)!);
            Assert.That(pressed, Is.EqualTo(new[] { direction }), $"the button at {direction} should say {direction}");
        }

        pressed.Clear();
        await Client.WaitPost(() => pad.SetDirectionDisabled(new Vector2i(0, 1), true));
        await Press(pad.GetButton(new Vector2i(0, 1))!);
        Assert.That(pressed, Is.Empty, "a disabled direction should say nothing");

        await Client.WaitAssertion(() =>
        {
            Assert.That(pad.GetButton(Vector2i.Zero)!.Visible, Is.False, "no middle button unless it has something to show");
            pad.CentreText = "R";
            Assert.That(pad.GetButton(Vector2i.Zero)!.Visible, Is.True);
        });
        await Pair.RunTicksSync(2);
        await Press(pad.GetButton(Vector2i.Zero)!);
        Assert.That(pressed, Is.EqualTo(new[] { Vector2i.Zero }), "the middle button should say (0, 0)");

        await Client.WaitPost(() => window.Close());
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task TheArrowsAreOneSize(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        var (window, pad) = await Open();

        await Client.WaitAssertion(() =>
        {
            var size = pad.GetButton(Directions[0])!.Size;
            Assert.That(size.X, Is.GreaterThan(0f));
            foreach (var direction in Directions)
            {
                var button = pad.GetButton(direction)!;
                Assert.That(Vector2.Distance(button.Size, size), Is.LessThan(0.01f), $"the arrow at {direction} should be the size of the others: {button.Size} against {size}");
                Assert.That(button.Icon, Is.Not.Null.And.Not.Empty, $"the button at {direction} should be an arrow");
            }

            window.Close();
        });
    }
}
