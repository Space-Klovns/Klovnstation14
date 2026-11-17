#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui;
using Content.Client.UserInterface.Controls;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     A window resizes from its edges even when its contents stop clicks right up to them.
/// </summary>
[TestOf(typeof(KsWindowEdgeGrip))]
public sealed class KsWindowEdgeGripTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private const float WindowSize = 300f;

    /// <summary>
    ///     A press in each of the window's resize zones reaches the window, which works out the drag from it, and a press
    ///         in the middle still goes to the contents.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task PressesInResizeZonesReachTheWindow(bool tgui)
    {
        await OverrideCVar(Side.Client, Content.Shared._KS14.CCVar.KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2); // The theme restyles on the frame after the cvar changes.

        var uiManager = Client.ResolveDependency<IUserInterfaceManager>();
        FancyWindow? window = null;
        PanelContainer? contents = null;

        await Client.WaitPost(() =>
        {
            window = new FancyWindow { Title = "Edge", SetSize = new Vector2(WindowSize, WindowSize) };
            contents = new PanelContainer
            {
                MouseFilter = Control.MouseFilterMode.Stop,
                HorizontalExpand = true,
                VerticalExpand = true,
            };
            window.ContentsContainer.AddChild(contents);
            window.OpenCentered();
        });

        await Pair.RunTicksSync(5);

        await Client.WaitAssertion(() =>
        {
            var zones = new List<(string Name, Vector2 Point)>
            {
                ("bottom", new Vector2(WindowSize / 2f, WindowSize - 2f)),
                ("right", new Vector2(WindowSize - 1f, WindowSize / 2f)),
                ("corner", new Vector2(WindowSize - 3f, WindowSize - 3f)),
            };

            // Baron resizes from every side, tgui from the south and east only.
            if (!tgui)
            {
                zones.Add(("left", new Vector2(1f, WindowSize / 2f)));
                zones.Add(("bottom left", new Vector2(2f, WindowSize - 2f)));
            }

            Control? HitAt(Vector2 point)
            {
                return uiManager.MouseGetControl(new ScreenCoordinates(window!.GlobalPixelPosition + point * window.UIScale, WindowId.Main));
            }

            using (Assert.EnterMultipleScope())
            {
                foreach (var (name, point) in zones)
                {
                    // The press goes to what is hit, then up through every Pass parent until a Stop: the grip passes it
                    //      to the window, which works out the drag from where the press landed.
                    var hit = HitAt(point);
                    Assert.That(hit, Is.InstanceOf<KsWindowEdgeGrip>(), $"A press in the {name} resize zone lands on {hit}, not the edge grip.");
                    Assert.That(hit?.MouseFilter, Is.EqualTo(Control.MouseFilterMode.Pass), $"The {name} grip stops the press.");
                    Assert.That(hit?.Parent, Is.SameAs(window), $"The {name} grip doesn't pass to the window.");
                }

                Assert.That(HitAt(new Vector2(WindowSize / 2f, WindowSize / 2f)), Is.SameAs(contents),
                    "The edge grip took a press in the middle of the window.");
            }

            window!.Close();
        });
    }

    /// <summary>
    ///     Nothing of a window takes the mouse from outside it. The tgui handle on a DefaultWindow classified its zones by
    ///         distance from the edges alone, so the mouse anywhere diagonally out from the bottom right corner, or anywhere
    ///         left of a resizable window, was the window's: the actions window, ahelp and the spawn menu showed a resize
    ///         cursor from across the screen.
    /// </summary>
    [TestCase(true, true)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(false, false)]
    public async Task NothingOutsideAWindowIsItsToHit(bool tgui, bool fancy)
    {
        await OverrideCVar(Side.Client, Content.Shared._KS14.CCVar.KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);

        var uiManager = Client.ResolveDependency<IUserInterfaceManager>();
        Robust.Client.UserInterface.CustomControls.BaseWindow? window = null;

        await Client.WaitPost(() =>
        {
            window = fancy
                ? new FancyWindow { Title = "Far", SetSize = new Vector2(WindowSize, WindowSize) }
                : new Robust.Client.UserInterface.CustomControls.DefaultWindow { Title = "Far", SetSize = new Vector2(WindowSize, WindowSize) };
            window.Resizable = true;
            window.OpenCentered();
        });

        await Pair.RunTicksSync(5);

        await Client.WaitAssertion(() =>
        {
            var size = window!.Size;
            var away = 120f;
            var points = new (string Name, Vector2 Point)[]
            {
                ("past the bottom right corner", size + new Vector2(away, away)),
                ("past the bottom left corner", new Vector2(-away, size.Y + away)),
                ("past the top right corner", new Vector2(size.X + away, -away)),
                ("past the top left corner", new Vector2(-away, -away)),
                ("left of it", new Vector2(-away, size.Y / 2f)),
                ("below it", new Vector2(size.X / 2f, size.Y + away)),
            };

            using (Assert.EnterMultipleScope())
            {
                foreach (var (name, point) in points)
                {
                    var hit = uiManager.MouseGetControl(new ScreenCoordinates(window.GlobalPixelPosition + point * window.UIScale, WindowId.Main));
                    for (var control = hit; control != null; control = control.Parent)
                    {
                        if (control != window)
                            continue;

                        Assert.Fail($"The mouse {name} lands on {hit} in the window.");
                        break;
                    }
                }
            }

            window.Close();
        });
    }
}
