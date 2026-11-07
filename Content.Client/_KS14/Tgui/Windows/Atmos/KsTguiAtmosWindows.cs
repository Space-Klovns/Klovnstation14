using Content.Client._KS14.Tgui.Controls;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Prototypes;
using Content.Shared._KS14.Fluidmos;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._KS14.Tgui.Windows.Atmos;

/// <summary>
///     What a gas device's bound user interface needs from its window, whichever of the stock and the tgui window it opened.
/// </summary>
public interface IKsAtmosWindow : IDisposable
{
    string? Title { get; set; }
}

/// <summary>
///     A window with the line saying where a compressing device's heat goes.
/// </summary>
public interface IKsHeatWindow : IKsAtmosWindow
{
    /// <summary>
    ///     The device whose heat the footnote shows.
    /// </summary>
    EntityUid HeatDevice { set; }
}

public interface IKsGasPressurePumpWindow : IKsHeatWindow
{
    event Action<bool>? ToggleStatusButtonPressed;
    event Action<float>? PumpOutputPressureChanged;

    float MaxPressure { get; set; }

    void SetOutputPressure(float pressure);
    void SetPumpStatus(bool enabled);
}

public interface IKsGasVolumePumpWindow : IKsHeatWindow
{
    event Action<bool>? ToggleStatusButtonPressed;
    event Action<string>? PumpTransferRateChanged;

    void SetTransferRate(float rate);
    void SetPumpStatus(bool enabled);
}

public interface IKsGasFilterWindow : IKsHeatWindow
{
    event Action<bool>? ToggleStatusButtonPressed;
    event Action<string>? FilterTransferRateChanged;
    event Action<HashSet<Gas>>? FilterGasesChanged;

    void SetTransferRate(float rate);
    void SetFilterStatus(bool enabled);
    void SetFilteredGases(HashSet<Gas> gases);
    void PopulateGasList(IEnumerable<GasPrototype> gases);
}

public interface IKsGasMixerWindow : IKsHeatWindow
{
    event Action<bool>? ToggleStatusButtonPressed;
    event Action<string>? MixerOutputPressureChanged;
    event Action<string>? MixerNodePercentageChanged;

    bool NodeOneLastEdited { get; }

    void SetOutputPressure(float pressure);
    void SetNodePercentages(float nodeOne);
    void SetMixerStatus(bool enabled);
}

public interface IKsGasCanisterWindow : IKsAtmosWindow
{
    event Action? TankEjectButtonPressed;
    event Action? TankInsertButtonPressed;
    event Action<float>? ReleasePressureSet;
    event Action? ReleaseValveCloseButtonPressed;
    event Action? ReleaseValveOpenButtonPressed;
    event Action? ShieldToggleButtonPressed;

    void SetCanisterLabel(string label);
    void SetCanisterPressure(float pressure);
    void SetPortStatus(bool status);
    void SetTankLabel(string? label);
    void SetTankPressure(float pressure);
    void SetReleasePressureRange(float min, float max);

    /// <summary>
    ///     The pressure the canister starts to fail at.
    /// </summary>
    void SetCanisterLimit(float pressureLimit);

    /// <summary>
    ///     The pressures the holding tank starts to vent at and to fail at.
    /// </summary>
    void SetTankLimits(float leakPressure, float fragmentPressure);

    void SetReleasePressure(float pressure);
    void SetReleaseValve(bool valve);
    void SetShieldSettingsVisible(bool visible);
    void UpdateShieldState(bool enabled, float dischargeRate, float lastCharge, float maxCharge);
    void SetKsFluid(KsFluidEntry[]? entries);
}

public interface IKsGasThermomachineWindow : IKsAtmosWindow
{
    event Action? ToggleRequested;
    event Action<float>? TemperatureRequested;

    EntityUid Entity { set; }

    void SetActive(bool active);
    void SetTemperature(float temperature);

    /// <summary>
    ///     The range the machine can be set within, for the window's presets.
    /// </summary>
    void SetTemperatureRange(float min, float max);

    /// <summary>
    ///     The gas the machine is working on.
    /// </summary>
    void SetGasStatus(float temperature, float pressure);
}

/// <summary>
///     Picks between a gas device's stock window and its tgui one.
/// </summary>
public static class KsTguiAtmosWindows
{
    public static TInterface Create<TStock, TTgui, TInterface>(BoundUserInterface boundUserInterface)
        where TStock : BaseWindow, TInterface, new()
        where TTgui : BaseWindow, TInterface, new()
    {
        return KsTguiTheme.ReadEnabled()
            ? boundUserInterface.CreateWindow<TTgui>()
            : boundUserInterface.CreateWindow<TStock>();
    }
}

/// <summary>
///     tgui's on/off button, shared by the gas devices' windows: a power icon and "On" or "Off", green while on.
/// </summary>
public sealed class KsTguiPowerToggle
{
    public KsTguiButton Button { get; }

    public bool On { get; private set; }

    /// <summary>
    ///     Raised with the state the player asked for.
    /// </summary>
    public event Action<bool>? Toggled;

    public KsTguiPowerToggle()
    {
        Button = new KsTguiButton();
        Button.OnPressed += _ => Toggled?.Invoke(!On);
        SetOn(false);
    }

    public void SetOn(bool on)
    {
        On = on;
        Button.Selected = on;
        Button.Icon = on ? KsTguiIcons.Power : KsTguiIcons.Times;
        Button.Text = Loc.GetString(on ? "ks-tgui-atmos-on" : "ks-tgui-atmos-off");
    }
}
