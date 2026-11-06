using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui.Controls;
using Content.Client._KS14.Tgui.Windows.Wires;
using Content.Client.Wires.UI;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Content.Shared.Wires;
using Robust.Shared.Maths;
using Robust.Shared.GameObjects;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     The wires BUI opens the tgui window with the theme on and the stock one with it off, and the tgui window's buttons
///         raise the same actions.
/// </summary>
[TestOf(typeof(KsTguiWiresWindow))]
public sealed class KsTguiWiresWindowTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [TestCase(true)]
    [TestCase(false)]
    public async Task BuiPicksWindowByTheme(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);

        WiresBoundUserInterface bui = null;
        await Client.WaitPost(() =>
        {
            var entityManager = Client.EntMan;
            var owner = entityManager.SpawnEntity(null, new Robust.Shared.Map.MapCoordinates(0f, 0f, Robust.Shared.Map.MapId.Nullspace));
            bui = new WiresBoundUserInterface(owner, WiresUiKey.Key);
            typeof(BoundUserInterface).GetMethod("Open", Any)!.Invoke(bui, null);

            var state = new WiresBoundUserInterfaceState(
                [new ClientWire(4, false, WireColor.Red, WireLetter.α), new ClientWire(7, true, WireColor.Blue, WireLetter.β)],
                [new StatusEntry("a", new StatusLightData(Color.Red, StatusLightState.BlinkingFast, "Light"))],
                "Airlock", "ABC", 1);
            typeof(BoundUserInterface).GetMethod("UpdateState", Any)!.Invoke(bui, [state]);
        });
        await Pair.RunTicksSync(3);

        await Client.WaitAssertion(() =>
        {
            var tguiWindow = (KsTguiWiresWindow)typeof(WiresBoundUserInterface).GetField("_ksTguiMenu", Any)!.GetValue(bui);
            var stockWindow = (WiresMenu)typeof(WiresBoundUserInterface).GetField("_menu", Any)!.GetValue(bui);
            Assert.That(tguiWindow != null, Is.EqualTo(tgui));
            Assert.That(stockWindow != null, Is.EqualTo(!tgui));

            if (!tgui)
            {
                bui.Dispose();
                return;
            }

            var actions = new List<(int, WiresAction)>();
            tguiWindow.OnAction += (id, action) => actions.Add((id, action));

            var buttons = new List<KsTguiButton>();
            Collect(tguiWindow, buttons);
            Assert.That(buttons, Has.Count.EqualTo(4));

            var press = typeof(BaseButton).GetField("OnPressed", Any)!;
            foreach (var button in buttons)
                ((Delegate)press.GetValue(button))!.DynamicInvoke([null]);

            Assert.That(actions, Is.EqualTo(new List<(int, WiresAction)>
            {
                (4, WiresAction.Cut), (4, WiresAction.Pulse), (7, WiresAction.Mend), (7, WiresAction.Pulse),
            }));

            bui.Dispose();
        });
    }

    private static void Collect(Control control, List<KsTguiButton> buttons)
    {
        foreach (var child in control.Children)
        {
            if (child is KsTguiButton button)
                buttons.Add(button);
            Collect(child, buttons);
        }
    }
}
