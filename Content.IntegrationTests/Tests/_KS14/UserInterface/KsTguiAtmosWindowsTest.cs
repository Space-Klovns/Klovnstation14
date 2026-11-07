#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui.Controls;
using Content.Client._KS14.Tgui.Windows.Atmos;
using Content.Client.Atmos.UI;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Piping.Binary.Components;
using Content.Shared.Atmos.Piping.Trinary.Components;
using Content.Shared.Atmos.Piping.Unary.Components;
using Content.Shared.Atmos.Prototypes;
using Content.Shared.UserInterface;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     The gas devices' bound user interfaces open the tgui window under the tgui theme and the stock one otherwise, and
///         each tgui window raises the actions the stock window does.
/// </summary>
[TestOf(typeof(KsTguiAtmosWindows))]
public sealed class KsTguiAtmosWindowsTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId PressurePump = "GasPressurePump";
    private static readonly EntProtoId VolumePump = "GasVolumePump";
    private static readonly EntProtoId Filter = "GasFilter";
    private static readonly EntProtoId Mixer = "GasMixer";
    private static readonly EntProtoId Canister = "AirCanister";
    private static readonly EntProtoId Thermomachine = "GasThermoMachineFreezer";
    private static readonly EntProtoId Human = "MobHuman";

    private static IEnumerable<TestCaseData> Devices()
    {
        foreach (var tgui in new[] { true, false })
        {
            yield return new TestCaseData(tgui, PressurePump, GasPressurePumpUiKey.Key, typeof(KsTguiGasPressurePumpWindow), typeof(GasPressurePumpWindow));
            yield return new TestCaseData(tgui, VolumePump, GasVolumePumpUiKey.Key, typeof(KsTguiGasVolumePumpWindow), typeof(GasVolumePumpWindow));
            yield return new TestCaseData(tgui, Filter, GasFilterUiKey.Key, typeof(KsTguiGasFilterWindow), typeof(GasFilterWindow));
            yield return new TestCaseData(tgui, Mixer, GasMixerUiKey.Key, typeof(KsTguiGasMixerWindow), typeof(GasMixerWindow));
            yield return new TestCaseData(tgui, Canister, GasCanisterUiKey.Key, typeof(KsTguiGasCanisterWindow), typeof(GasCanisterWindow));
            yield return new TestCaseData(tgui, Thermomachine, ThermomachineUiKey.Key, typeof(KsTguiGasThermomachineWindow), typeof(GasThermomachineWindow));
        }
    }

    [TestCaseSource(nameof(Devices))]
    public async Task TheBuiOpensTheThemesWindow(bool tgui, EntProtoId device, Enum uiKey, System.Type tguiWindow, System.Type stockWindow)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);

        var testMap = await Pair.CreateTestMap();
        await Server.WaitPost(() =>
        {
            var deviceUid = SEntMan.SpawnEntity(device, new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 0.5f)));
            var user = SEntMan.SpawnEntity(Human, new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 1.5f)));
            Server.PlayerMan.SetAttachedEntity(ServerSession!, user);
            SEntMan.System<SharedUserInterfaceSystem>().OpenUi(deviceUid, uiKey, user);
        });
        await Pair.RunTicksSync(10);

        await Client.WaitAssertion(() =>
        {
            var windows = Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.ToList();
            Assert.That(windows.Any(window => window.GetType() == tguiWindow), Is.EqualTo(tgui));
            Assert.That(windows.Any(window => window.GetType() == stockWindow), Is.EqualTo(!tgui));
        });
    }

    /// <summary>
    ///     Fires a button's press as a click would, which a headless test has no mouse for.
    /// </summary>
    private static void Press(BaseButton button)
    {
        var field = typeof(BaseButton).GetField("OnPressed", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((Action<BaseButton.ButtonEventArgs>?)field.GetValue(button))?.Invoke(null!);
    }

    private static void Enter(KsTguiNumberInput input, float value)
    {
        input.Edit.Text = value.ToString(CultureInfo.CurrentCulture);
        input.Commit();
    }

    private static string Text(float value) => value.ToString(CultureInfo.CurrentCulture);

    [Test]
    public async Task PressurePumpActionsFire()
    {
        var fired = new List<string>();
        await Client.WaitPost(() =>
        {
            var window = new KsTguiGasPressurePumpWindow();
            window.ToggleStatusButtonPressed += on => fired.Add("power:" + on);
            window.PumpOutputPressureChanged += value => fired.Add("pressure:" + value);
            window.MaxPressure = 1000f;
            window.SetOutputPressure(100f);

            Press(window.PowerButton);
            Enter(window.PressureInput, 250f);
            Press(window.MaxButtonControl);
            Assert.That(fired, Is.EqualTo(new[] { "power:True", "pressure:250", "pressure:1000" }));

            // A value above the limit is clamped to it, and one the server sends is shown.
            window.SetOutputPressure(300f);
            Assert.That(window.PressureInput.Value, Is.EqualTo(300f));
            window.Dispose();
        });
    }

    [Test]
    public async Task VolumePumpActionsFire()
    {
        var fired = new List<string>();
        await Client.WaitPost(() =>
        {
            var window = new KsTguiGasVolumePumpWindow();
            window.ToggleStatusButtonPressed += on => fired.Add("power:" + on);
            window.PumpTransferRateChanged += value => fired.Add("rate:" + value);

            Press(window.PowerButton);
            Enter(window.RateInput, 20f);
            Press(window.MaxButtonControl);
            Assert.That(fired, Is.EqualTo(new[] { "power:True", "rate:" + Text(20f), "rate:" + Text(Atmospherics.MaxTransferRate) }));
            window.Dispose();
        });
    }

    [Test]
    public async Task FilterActionsFire()
    {
        var fired = new List<string>();
        await Client.WaitPost(() =>
        {
            var gases = CProtoMan.EnumeratePrototypes<GasPrototype>().Take(3).ToList();
            var window = new KsTguiGasFilterWindow();
            window.PopulateGasList(gases);
            window.ToggleStatusButtonPressed += on => fired.Add("power:" + on);
            window.FilterTransferRateChanged += value => fired.Add("rate:" + value);
            window.FilterGasesChanged += selected => fired.Add("gases:" + selected.Count);

            Assert.That(window.GasButtons, Has.Count.EqualTo(gases.Count));
            Press(window.PowerButton);
            Enter(window.RateInput, 30f);
            Press(window.MaxButtonControl);
            Press(window.GasButtons.Values.First());
            Press(window.SelectAllButton);
            Press(window.DeselectAllButton);

            Assert.That(fired, Is.EqualTo(new[]
            {
                "power:True", "rate:" + Text(30f), "rate:" + Text(Atmospherics.MaxTransferRate),
                "gases:1", "gases:" + gases.Count, "gases:0",
            }));
            window.Dispose();
        });
    }

    /// <summary>
    ///     The filter with every gas in it: nothing is wider or taller than the window gives it.
    /// </summary>
    [Test]
    public async Task GasFilterWithEveryGasFits()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Pair.RunTicksSync(2);

        KsTguiGasFilterWindow window = null!;
        await Client.WaitPost(() =>
        {
            window = new KsTguiGasFilterWindow();
            window.PopulateGasList(CProtoMan.EnumeratePrototypes<GasPrototype>());
            window.OpenCentered();
        });
        await Pair.RunTicksSync(3);

        await Client.WaitAssertion(() =>
        {
            static IEnumerable<KsTguiButton> Buttons(Control root)
            {
                foreach (var child in root.Children)
                {
                    if (child is KsTguiButton button)
                        yield return button;

                    foreach (var inner in Buttons(child))
                        yield return inner;
                }
            }

            // Where they landed, not what they measure: measured inside a width, a control can't report wider than it, whatever it wants.
            var gasButtons = Buttons(window.ContentsContainer).Where(button => button.Text != null && button.Parent is WrapContainer).ToList();
            Assert.That(gasButtons, Has.Count.GreaterThan(1), "The filter should list the gases.");

            var windowRight = window.GlobalPosition.X + window.Size.X;
            foreach (var button in gasButtons)
            {
                Assert.That(button.GlobalPosition.X + button.Size.X, Is.LessThanOrEqualTo(windowRight + 0.5f), $"{button.Text} runs past the window");
            }

            window.Close();
        });
    }

    [Test]
    public async Task MixerActionsFire()
    {
        var fired = new List<string>();
        await Client.WaitPost(() =>
        {
            var window = new KsTguiGasMixerWindow();
            window.ToggleStatusButtonPressed += on => fired.Add("power:" + on);
            window.MixerOutputPressureChanged += value => fired.Add("pressure:" + value);
            window.MixerNodePercentageChanged += value => fired.Add((window.NodeOneLastEdited ? "one:" : "two:") + value);
            window.SetNodePercentages(0.5f);

            Press(window.PowerButton);
            Enter(window.PressureInput, 400f);
            Press(window.MaxButtonControl);
            Enter(window.NodeOneInput, 30f);
            Enter(window.NodeTwoInput, 40f);

            Assert.That(fired, Is.EqualTo(new[]
            {
                "power:True", "pressure:" + Text(400f), "pressure:" + Text(Atmospherics.MaxOutputPressure),
                "one:" + Text(30f), "two:" + Text(40f),
            }));
            window.Dispose();
        });
    }

    [Test]
    public async Task CanisterActionsFire()
    {
        var fired = new List<string>();
        await Client.WaitPost(() =>
        {
            var window = new KsTguiGasCanisterWindow();
            window.TankEjectButtonPressed += () => fired.Add("eject");
            window.ReleasePressureSet += value => fired.Add("release:" + value);
            window.ReleaseValveOpenButtonPressed += () => fired.Add("open");
            window.ReleaseValveCloseButtonPressed += () => fired.Add("close");
            window.ShieldToggleButtonPressed += () => fired.Add("shield");

            window.SetReleasePressureRange(10f, 500f);
            window.SetReleasePressure(100f);
            window.SetTankLabel("tank");
            window.SetShieldSettingsVisible(true);
            window.UpdateShieldState(enabled: false, dischargeRate: 1f, lastCharge: 10f, maxCharge: 100f);

            Press(window.ValveButton);
            window.SetReleaseValve(true);
            Press(window.ValveButton);
            Press(window.EjectButton);
            Press(window.ShieldButton);
            Enter(window.ReleaseInput, 200f);
            Press(window.ReleaseMaxButton);
            Press(window.ReleaseDefaultButton);

            Assert.That(fired, Is.EqualTo(new[]
            {
                "open", "close", "eject", "shield", "release:200", "release:500",
                "release:" + Math.Clamp(Atmospherics.OneAtmosphere, 10f, 500f),
            }));
            window.Dispose();
        });
    }

    [Test]
    public async Task ThermomachineActionsFire()
    {
        var fired = new List<string>();
        await Client.WaitPost(() =>
        {
            var window = new KsTguiGasThermomachineWindow();
            window.ToggleRequested += () => fired.Add("toggle");
            window.TemperatureRequested += value => fired.Add("temperature:" + value);
            window.SetTemperatureRange(100f, 400f);
            window.SetTemperature(300f);

            Press(window.PowerButton);
            Enter(window.TemperatureInput, 250f);
            Press(window.MinimumButton);
            Press(window.MaximumButton);

            Assert.That(fired, Is.EqualTo(new[] { "toggle", "temperature:250", "temperature:100", "temperature:400" }));
            window.Dispose();
        });
    }
}
