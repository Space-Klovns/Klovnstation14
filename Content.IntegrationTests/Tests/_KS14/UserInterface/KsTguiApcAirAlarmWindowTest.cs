using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui.Controls;
using Content.Client._KS14.Tgui.Windows.AirAlarm;
using Content.Client._KS14.Tgui.Windows.Apc;
using Content.Client.Atmos.Monitor.UI;
using Content.Client.Power.APC;
using Content.Client.Power.APC.UI;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Content.Shared.APC;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Monitor;
using Content.Shared.Atmos.Monitor.Components;
using Content.Shared.Atmos.Piping.Unary.Components;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     The APC and air alarm BUIs open the tgui window with the theme on and the stock one with it off, and every button of
///         the tgui windows raises its action.
/// </summary>
[TestOf(typeof(KsTguiApcWindow))]
public sealed class KsTguiApcAirAlarmWindowTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [TestCase(true)]
    [TestCase(false)]
    public async Task ApcBuiPicksWindowByTheme(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);

        ApcBoundUserInterface bui = null;
        await Client.WaitPost(() =>
        {
            var owner = Client.EntMan.SpawnEntity(null, new Robust.Shared.Map.MapCoordinates(0f, 0f, Robust.Shared.Map.MapId.Nullspace));
            bui = new ApcBoundUserInterface(owner, ApcUiKey.Key);
            typeof(BoundUserInterface).GetMethod("Open", Any)!.Invoke(bui, null);
            typeof(BoundUserInterface).GetMethod("UpdateState", Any)!.Invoke(bui,
                [new ApcBoundInterfaceState(true, 1500, ApcExternalPowerState.Low, 0.5f)]);
        });
        await Pair.RunTicksSync(3);

        await Client.WaitAssertion(() =>
        {
            var window = typeof(ApcBoundUserInterface).GetField("_menu", Any)!.GetValue(bui);
            Assert.That(window is KsTguiApcWindow, Is.EqualTo(tgui));
            Assert.That(window is ApcMenu, Is.EqualTo(!tgui));

            if (tgui)
            {
                var apcWindow = (KsTguiApcWindow)window;
                apcWindow.SetAccessEnabled(true);

                var breakerPresses = 0;
                apcWindow.OnBreaker += () => breakerPresses++;

                var buttons = new List<KsTguiButton>();
                Collect(apcWindow, buttons);
                Assert.That(buttons, Has.Count.EqualTo(1));
                Press(buttons[0]);
                Assert.That(breakerPresses, Is.EqualTo(1));
            }

            bui.Dispose();
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task AirAlarmBuiPicksWindowByTheme(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);

        AirAlarmBoundUserInterface bui = null;
        await Client.WaitPost(() =>
        {
            var owner = Client.EntMan.SpawnEntity(null, new Robust.Shared.Map.MapCoordinates(0f, 0f, Robust.Shared.Map.MapId.Nullspace));
            bui = new AirAlarmBoundUserInterface(owner, SharedAirAlarmInterfaceKey.Key);
            typeof(BoundUserInterface).GetMethod("Open", Any)!.Invoke(bui, null);
            typeof(BoundUserInterface).GetMethod("UpdateState", Any)!.Invoke(bui, [MakeState()]);
        });
        await Pair.RunTicksSync(3);

        await Client.WaitAssertion(() =>
        {
            var window = typeof(AirAlarmBoundUserInterface).GetField("_window", Any)!.GetValue(bui);
            Assert.That(window is KsTguiAirAlarmWindow, Is.EqualTo(tgui));
            Assert.That(window is AirAlarmWindow, Is.EqualTo(!tgui));

            if (!tgui)
            {
                bui.Dispose();
                return;
            }

            var airWindow = (KsTguiAirAlarmWindow)window;
            airWindow.SetAccessEnabled(true);
            var fired = new HashSet<string>();
            airWindow.AtmosDeviceDataChanged += (_, _) => fired.Add("changed");
            airWindow.AtmosDeviceDataCopied += _ => fired.Add("copied");
            airWindow.AtmosAlarmThresholdChanged += (_, _, _, _) => fired.Add("threshold");
            airWindow.AirAlarmModeChanged += _ => fired.Add("mode");
            airWindow.AutoModeChanged += _ => fired.Add("auto");
            airWindow.ResyncAllRequested += () => fired.Add("resync");

            // Collected up front: resyncing throws the device sections out, but their buttons still answer.
            var buttons = new List<KsTguiButton>();
            Collect(airWindow, buttons);
            foreach (var button in buttons)
                Press(button);

            Assert.That(fired, Is.EquivalentTo(new[] { "changed", "copied", "threshold", "mode", "auto", "resync" }));

            bui.Dispose();
        });
    }

    [Test]
    public async Task AirAlarmWithoutAccessDisablesEveryEditableControl()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Pair.RunTicksSync(2);

        AirAlarmBoundUserInterface bui = null;
        await Client.WaitPost(() =>
        {
            var owner = Client.EntMan.SpawnEntity(null, new Robust.Shared.Map.MapCoordinates(0f, 0f, Robust.Shared.Map.MapId.Nullspace));
            bui = new AirAlarmBoundUserInterface(owner, SharedAirAlarmInterfaceKey.Key);
            typeof(BoundUserInterface).GetMethod("Open", Any)!.Invoke(bui, null);
            typeof(BoundUserInterface).GetMethod("UpdateState", Any)!.Invoke(bui, [MakeState()]);
        });
        await Pair.RunTicksSync(3);

        await Client.WaitAssertion(() =>
        {
            var airWindow = (KsTguiAirAlarmWindow)typeof(AirAlarmBoundUserInterface).GetField("_window", Any)!.GetValue(bui);

            // The test player has no entity, so no access.
            var buttons = new List<KsTguiButton>();
            Collect(airWindow, buttons);
            var inputs = new List<KsTguiNumberInput>();
            CollectInputs(airWindow, inputs);
            Assert.That(inputs, Is.Not.Empty);

            // Only the navigation to the other screens stays usable: Vents, Scrubbers, Modes, Thresholds.
            Assert.That(buttons.Count(button => !button.Disabled), Is.EqualTo(4), "Without access only navigation should work.");
            Assert.That(inputs.All(input => input.Disabled), Is.True, "Without access no number can be edited.");

            airWindow.SetAccessEnabled(true);
            Assert.That(buttons.Count(button => !button.Disabled), Is.GreaterThan(20));
            Assert.That(inputs.Count(input => !input.Disabled), Is.GreaterThan(4));

            airWindow.SetAccessEnabled(false);
            Assert.That(buttons.Count(button => !button.Disabled), Is.EqualTo(4));
            Assert.That(inputs.All(input => input.Disabled), Is.True);

            bui.Dispose();
        });
    }

    private static void CollectInputs(Control control, List<KsTguiNumberInput> inputs)
    {
        foreach (var child in control.Children)
        {
            if (child is KsTguiNumberInput input)
                inputs.Add(input);
            CollectInputs(child, inputs);
        }
    }

    private static AirAlarmUIState MakeState()
    {
        var threshold = new AtmosAlarmThreshold
        {
            UpperBound = new AlarmThresholdSetting { Value = 300f },
            LowerBound = new AlarmThresholdSetting { Value = 1f },
        };

        var sensor = new AtmosSensorData(
            101f,
            293f,
            10f,
            AtmosAlarmType.Normal,
            new Dictionary<Gas, float> { [Gas.Oxygen] = 2f, [Gas.Nitrogen] = 8f },
            new AtmosAlarmThreshold(threshold),
            new AtmosAlarmThreshold(threshold),
            new Dictionary<Gas, AtmosAlarmThreshold>
            {
                [Gas.Oxygen] = new(threshold),
                [Gas.Nitrogen] = new(threshold),
            });

        var devices = new List<(string, IAtmosDeviceData)>
        {
            ("vent", new GasVentPumpData { Enabled = true, PressureChecks = VentPressureBound.Both }),
            ("scrubber", new GasVentScrubberData { Enabled = true }),
            ("sensor", sensor),
        };

        return new AirAlarmUIState("addr", 3, 101f, 293f, devices, AirAlarmMode.Filtering, AtmosAlarmType.Normal, false, false);
    }

    private static void Press(KsTguiButton button)
    {
        var press = typeof(BaseButton).GetField("OnPressed", Any)!;
        ((System.Delegate)press.GetValue(button))?.DynamicInvoke([null]);
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
