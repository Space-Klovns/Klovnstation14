using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui.Controls;
using Content.Client._KS14.Tgui.Windows.Battery;
using Content.Client._KS14.Tgui.Windows.Disposal;
using Content.Client.Disposal.Mailing;
using Content.Client.Disposal.Unit;
using Content.Client.Power.Battery;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Content.Shared.Disposal;
using Content.Shared.Disposal.Components;
using Content.Shared.Power;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     The disposal unit, mailing unit and battery BUIs open the tgui window with the theme on and the stock one with it
///         off, and every control of the tgui windows raises its action.
/// </summary>
[TestOf(typeof(KsTguiDisposalWindow))]
public sealed class KsTguiDisposalBatteryWindowTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [TestCase(true)]
    [TestCase(false)]
    public async Task DisposalBuiPicksWindowByTheme(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);

        DisposalUnitBoundUserInterface bui = null;
        await Client.WaitPost(() =>
        {
            var owner = Client.EntMan.SpawnEntity(null, new MapCoordinates(0f, 0f, MapId.Nullspace));
            bui = new DisposalUnitBoundUserInterface(owner, DisposalUnitComponent.DisposalUnitUiKey.Key);
            typeof(BoundUserInterface).GetMethod("Open", Any)!.Invoke(bui, null);
        });
        await Pair.RunTicksSync(3);

        await Client.WaitAssertion(() =>
        {
            var tguiWindow = typeof(DisposalUnitBoundUserInterface).GetField("_tguiWindow", Any)!.GetValue(bui);
            var stockWindow = typeof(DisposalUnitBoundUserInterface).GetField("_disposalUnitWindow", Any)!.GetValue(bui);
            Assert.That(tguiWindow is KsTguiDisposalWindow, Is.EqualTo(tgui));
            Assert.That(stockWindow is DisposalUnitWindow, Is.EqualTo(!tgui));

            if (tgui)
            {
                var window = (KsTguiDisposalWindow)tguiWindow;
                var fired = new HashSet<string>();
                window.OnEject += () => fired.Add("eject");
                window.OnEngage += () => fired.Add("engage");
                window.OnPower += () => fired.Add("power");

                var buttons = new List<KsTguiButton>();
                Collect(window, buttons);
                Assert.That(buttons, Has.Count.EqualTo(3));
                foreach (var button in buttons)
                    Press(button);

                Assert.That(fired, Is.EquivalentTo(new[] { "eject", "engage", "power" }));
            }

            bui.Dispose();
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task MailingBuiPicksWindowByThemeAndKeepsDestinations(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);

        MailingUnitBoundUserInterface bui = null;
        await Client.WaitPost(() =>
        {
            var owner = Client.EntMan.SpawnEntity(null, new MapCoordinates(0f, 0f, MapId.Nullspace));
            bui = new MailingUnitBoundUserInterface(owner, MailingUnitUiKey.Key);
            typeof(BoundUserInterface).GetMethod("Open", Any)!.Invoke(bui, null);
        });
        await Pair.RunTicksSync(3);

        await Client.WaitAssertion(() =>
        {
            var tguiWindow = typeof(MailingUnitBoundUserInterface).GetField("_tguiWindow", Any)!.GetValue(bui);
            Assert.That(tguiWindow is KsTguiMailingWindow, Is.EqualTo(tgui));
            Assert.That(bui.MailingUnitWindow is MailingUnitWindow, Is.EqualTo(!tgui));

            if (tgui)
            {
                var window = (KsTguiMailingWindow)tguiWindow;
                var fired = new List<string>();
                window.OnTargetSelected += target => fired.Add(target);
                window.SetTargets("tag", "b", new List<string> { "a", "b", "c" });

                var buttons = new List<KsTguiButton>();
                Collect(window, buttons);
                // Eject, flush and power, and a button per destination.
                Assert.That(buttons, Has.Count.EqualTo(6));
                foreach (var button in buttons)
                    Press(button);

                Assert.That(fired, Is.EquivalentTo(new[] { "a", "b", "c" }));
            }

            bui.Dispose();
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task BatteryBuiPicksWindowByTheme(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);

        BatteryBoundUserInterface bui = null;
        await Client.WaitPost(() =>
        {
            var owner = Client.EntMan.SpawnEntity(null, new MapCoordinates(0f, 0f, MapId.Nullspace));
            bui = new BatteryBoundUserInterface(owner, BatteryUiKey.Key);
            typeof(BoundUserInterface).GetMethod("Open", Any)!.Invoke(bui, null);
            typeof(BoundUserInterface).GetMethod("UpdateState", Any)!.Invoke(bui, [MakeState()]);
        });
        await Pair.RunTicksSync(3);

        await Client.WaitAssertion(() =>
        {
            var window = typeof(BatteryBoundUserInterface).GetField("_menu", Any)!.GetValue(bui);
            Assert.That(window is KsTguiBatteryWindow, Is.EqualTo(tgui));
            Assert.That(window is BatteryMenu, Is.EqualTo(!tgui));

            if (tgui)
            {
                var batteryWindow = (KsTguiBatteryWindow)window;
                var fired = new HashSet<string>();
                var chargeRates = new List<float>();
                var dischargeRates = new List<float>();
                batteryWindow.OnInBreaker += on => fired.Add($"in{on}");
                batteryWindow.OnOutBreaker += on => fired.Add($"out{on}");
                batteryWindow.OnChargeRate += rate => chargeRates.Add(rate);
                batteryWindow.OnDischargeRate += rate => dischargeRates.Add(rate);

                var buttons = new List<KsTguiButton>();
                Collect(batteryWindow, buttons);
                // A breaker and four rate buttons per side.
                Assert.That(buttons, Has.Count.EqualTo(10));
                foreach (var button in buttons)
                    Press(button);

                // MakeState: input on, output off.
                Assert.That(fired, Is.EquivalentTo(new[] { "inFalse", "outTrue" }));
                Assert.That(chargeRates, Has.Count.EqualTo(4));
                Assert.That(dischargeRates, Has.Count.EqualTo(4));
                Assert.That(chargeRates, Does.Contain(1000f).And.Contain(50000f));
                Assert.That(dischargeRates, Does.Contain(2000f).And.Contain(80000f));

                // Typing a target commits it, in watts.
                var inputs = new List<KsTguiNumberInput>();
                Collect(batteryWindow, inputs);
                Assert.That(inputs, Has.Count.EqualTo(2));
                chargeRates.Clear();
                inputs[0].Edit.Text = "20";
                inputs[0].Commit();
                Assert.That(chargeRates, Is.EqualTo(new[] { 20000f }));
            }

            bui.Dispose();
        });
    }

    private static BatteryBuiState MakeState()
    {
        return new BatteryBuiState
        {
            CanCharge = true,
            CanDischarge = false,
            SupplyingNetworkHasPower = true,
            LoadingNetworkHasPower = true,
            CurrentReceiving = 5000f,
            CurrentSupply = 1000f,
            MaxChargeRate = 10000f,
            MinMaxChargeRate = 1000f,
            MaxMaxChargeRate = 50000f,
            Efficiency = 1f,
            MaxSupply = 20000f,
            MinMaxSupply = 2000f,
            MaxMaxSupply = 80000f,
            Charge = 500000f,
            Capacity = 1000000f,
        };
    }

    private static void Press(KsTguiButton button)
    {
        var press = typeof(BaseButton).GetField("OnPressed", Any)!;
        ((System.Delegate)press.GetValue(button))?.DynamicInvoke([null]);
    }

    private static void Collect<T>(Control control, List<T> found) where T : Control
    {
        foreach (var child in control.Children)
        {
            if (child is T match)
                found.Add(match);
            Collect(child, found);
        }
    }
}
