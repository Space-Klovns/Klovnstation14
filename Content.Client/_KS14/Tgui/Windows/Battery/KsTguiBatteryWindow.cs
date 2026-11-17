using System.Numerics;
using Content.Client._KS14.Tgui.Controls;
using Content.Client.UserInterface.Controls;
using Content.Shared.Power;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Windows.Battery;

/// <summary>
///     The SMES and substation window laid out after tgui's Smes: Stored Energy, then Input and Output, each with its breaker
///         in the title and a target rate. KS14's battery adds what tg lacks: the rate actually going in and out, the
///         passthrough, whether each side's network has power, and the time to full or empty. Its power lines and cell
///         lamps are not drawn: the bars and the network rows say the same.
/// </summary>
public sealed partial class KsTguiBatteryWindow : FancyWindow, IKsBatteryMenu
{
    // The ETA display keeps the stock menu's cutoffs.
    private const float MaxEtaValueMinutes = 60f;
    private const float NotApplicableEtaHighCutoffMinutes = 1000f;
    private const float NotApplicableEtaLowCutoffMinutes = 0.01f;
    private const float PrecisionRoundFactor = 100_000f;

    /// <summary>
    ///     tg's +/- buttons move the target by 10 kW.
    /// </summary>
    private const float AdjustWatts = 10_000f;

    private const float WattsPerKilowatt = 1000f;

    [Dependency] private IEntityManager _entityManager = default!;

    private readonly KsTguiProgressBar _storedBar;
    private readonly KsTguiLabeledListItem _energyItem;
    private readonly KsTguiLabeledListItem _etaItem;

    private readonly RateControls _input;
    private readonly RateControls _output;
    private readonly KsTguiLabeledListItem _passthroughItem;

    public event Action<bool>? OnInBreaker;
    public event Action<bool>? OnOutBreaker;
    public event Action<float>? OnChargeRate;
    public event Action<float>? OnDischargeRate;

    public KsTguiBatteryWindow()
    {
        IoCManager.InjectDependencies(this);

        MinSize = new Vector2(380f, 0f);
        SetWidth = 380f;

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = KsTguiSection.Gap,
            Margin = new Thickness(6f),
        };

        var storedSection = new KsTguiSection { Title = Loc.GetString("ks-tgui-battery-stored-energy") };
        var storedBody = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = KsTguiSection.Gap,
        };
        _storedBar = new KsTguiProgressBar { HorizontalExpand = true };
        _storedBar.AddRange(KsTguiColor.Good, 0.5f, float.PositiveInfinity);
        _storedBar.AddRange(KsTguiColor.Average, 0.15f, 0.5f);
        _storedBar.AddRange(KsTguiColor.Bad, float.NegativeInfinity, 0.15f);
        var storedList = new KsTguiLabeledList();
        _energyItem = storedList.AddItem(Loc.GetString("battery-menu-energy").TrimEnd(':'));
        _etaItem = storedList.AddItem(string.Empty);
        storedBody.AddChild(_storedBar);
        storedBody.AddChild(storedList);
        storedSection.AddChild(storedBody);
        root.AddChild(storedSection);

        _input = new RateControls(Loc.GetString("ks-tgui-battery-input"), "ks-tgui-battery-target-input",
            "ks-tgui-battery-receiving", "ks-tgui-battery-into-cells", "ks-tgui-battery-charge-mode");
        _input.Breaker.OnPressed += _ => OnInBreaker?.Invoke(!_input.BreakerOn);
        _input.Rate.OnCommitted += kilowatts => OnChargeRate?.Invoke(kilowatts * WattsPerKilowatt);
        WireAdjust(_input, rate => OnChargeRate?.Invoke(rate));
        root.AddChild(_input.Section);

        _output = new RateControls(Loc.GetString("ks-tgui-battery-output"), "ks-tgui-battery-target-output",
            "ks-tgui-battery-supplying", "ks-tgui-battery-from-cells", "ks-tgui-battery-output-mode");
        _output.Breaker.OnPressed += _ => OnOutBreaker?.Invoke(!_output.BreakerOn);
        _output.Rate.OnCommitted += kilowatts => OnDischargeRate?.Invoke(kilowatts * WattsPerKilowatt);
        WireAdjust(_output, rate => OnDischargeRate?.Invoke(rate));
        _passthroughItem = _output.List.AddItem(Loc.GetString("battery-menu-passthrough"));
        root.AddChild(_output.Section);

        ContentsContainer.AddChild(root);
    }

    public void SetEntity(EntityUid entity)
    {
        this.SetInfoFromEntity(_entityManager, entity);
    }

    public void Update(BatteryBuiState state)
    {
        var inValue = state.CurrentReceiving;
        var outValue = state.CurrentSupply;

        var storageDelta = inValue - outValue;
        // Mask rounding errors in power code.
        if (Math.Abs(storageDelta) < state.Capacity / PrecisionRoundFactor)
            storageDelta = 0f;

        var fraction = state.Capacity > 0f ? state.Charge / state.Capacity : 0f;
        if (!float.IsFinite(fraction))
            fraction = 0f;
        fraction = Math.Clamp(fraction, 0f, 1f);

        _storedBar.Value = fraction;
        _storedBar.Text = Loc.GetString("battery-menu-stored-percent-value", ("value", fraction));
        _energyItem.Text = Loc.GetString("battery-menu-stored-energy-value", ("value", state.Charge));

        UpdateEta(state, storageDelta);

        // Input.
        _input.SetBreaker(state.CanCharge);
        _input.Mode.Text = Loc.GetString(fraction >= 1f
            ? "ks-tgui-battery-fully-charged"
            : storageDelta > 0f ? "ks-tgui-battery-charging" : "ks-tgui-battery-not-charging");
        _input.Mode.TextColor = fraction >= 1f ? KsTguiPalettes.Good : storageDelta > 0f ? KsTguiPalettes.Average : KsTguiPalettes.Bad;
        _input.SetRange(state.MinMaxChargeRate, state.MaxMaxChargeRate, state.MaxChargeRate);
        _input.Flow.Text = FormatPower(inValue);
        _input.Storage.Text = FormatPower(Math.Max(0f, storageDelta));
        _input.SetNetwork(state.SupplyingNetworkHasPower);

        // Output.
        _output.SetBreaker(state.CanDischarge);
        var sending = outValue > 0f;
        _output.Mode.Text = Loc.GetString(sending
            ? "ks-tgui-battery-sending"
            : state.Charge > 0f ? "ks-tgui-battery-not-sending" : "ks-tgui-battery-no-charge");
        _output.Mode.TextColor = sending ? KsTguiPalettes.Good : state.Charge > 0f ? KsTguiPalettes.Average : KsTguiPalettes.Bad;
        _output.SetRange(state.MinMaxSupply, state.MaxMaxSupply, state.MaxSupply);
        _output.Flow.Text = FormatPower(outValue);
        _output.Storage.Text = FormatPower(Math.Max(0f, -storageDelta));
        _output.SetNetwork(state.LoadingNetworkHasPower);
        _passthroughItem.Text = FormatPower(Math.Min(inValue, outValue));
    }

    private void UpdateEta(BatteryBuiState state, float storageDelta)
    {
        var storageEtaDiff = storageDelta > 0f ? (state.Capacity - state.Charge) * (1f / state.Efficiency) : -state.Charge;
        var etaMinutes = storageEtaDiff / storageDelta / 60.0;

        _etaItem.Label = Loc.GetString(storageDelta > 0f ? "battery-menu-eta-full" : "battery-menu-eta-empty").TrimEnd(':');

        if (!double.IsFinite(etaMinutes)
            || Math.Abs(etaMinutes) > NotApplicableEtaHighCutoffMinutes
            || Math.Abs(etaMinutes) < NotApplicableEtaLowCutoffMinutes)
        {
            _etaItem.Text = Loc.GetString("battery-menu-eta-value-na");
            return;
        }

        _etaItem.Text = Loc.GetString(
            etaMinutes > MaxEtaValueMinutes ? "battery-menu-eta-value-max" : "battery-menu-eta-value",
            ("minutes", Math.Min(Math.Ceiling(etaMinutes), MaxEtaValueMinutes)));
    }

    private static string FormatPower(float value)
    {
        return Loc.GetString("battery-menu-power-value", ("value", value));
    }

    /// <summary>
    ///     tg's four buttons around the slider: to the minimum, down by 10 kW, up by 10 kW, to the maximum.
    /// </summary>
    private static void WireAdjust(RateControls controls, Action<float> send)
    {
        controls.ToMin.OnPressed += _ => send(controls.Rate.MinValue * WattsPerKilowatt);
        controls.Down.OnPressed += _ => send(Math.Max(controls.Rate.MinValue * WattsPerKilowatt,
            controls.Rate.Value * WattsPerKilowatt - AdjustWatts));
        controls.Up.OnPressed += _ => send(Math.Min(controls.Rate.MaxValue * WattsPerKilowatt,
            controls.Rate.Value * WattsPerKilowatt + AdjustWatts));
        controls.ToMax.OnPressed += _ => send(controls.Rate.MaxValue * WattsPerKilowatt);
    }

    /// <summary>
    ///     The controls of one side: its breaker, mode, target rate with tg's four buttons, and the flows.
    /// </summary>
    private sealed class RateControls
    {
        public readonly KsTguiSection Section;
        public readonly KsTguiLabeledList List = new();
        public readonly KsTguiButton Breaker = new();
        public readonly KsTguiLabeledListItem Mode;
        public readonly KsTguiNumberInput Rate;
        public readonly KsTguiButton ToMin = new() { Icon = KsTguiIcons.FastBackward };
        public readonly KsTguiButton Down = new() { Icon = KsTguiIcons.Backward };
        public readonly KsTguiButton Up = new() { Icon = KsTguiIcons.Forward };
        public readonly KsTguiButton ToMax = new() { Icon = KsTguiIcons.FastForward };
        public readonly KsTguiLabeledListItem Flow;
        public readonly KsTguiLabeledListItem Storage;
        public readonly KsTguiLabeledListItem Network;

        public bool BreakerOn { get; private set; }

        public RateControls(string title, string targetKey, string flowKey, string storageKey, string modeKey)
        {
            Section = new KsTguiSection { Title = title, Buttons = Breaker };

            Mode = List.AddItem(Loc.GetString(modeKey));

            var targetItem = List.AddItem(Loc.GetString(targetKey));
            Rate = new KsTguiNumberInput
            {
                Unit = Loc.GetString("ks-tgui-battery-unit-kw"),
                WidthPixels = 75f,
                TickWhileDragging = true,
            };
            var rateRow = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Horizontal,
                SeparationOverride = 3,
                HorizontalExpand = true,
            };
            rateRow.AddChild(ToMin);
            rateRow.AddChild(Down);
            rateRow.AddChild(Rate);
            rateRow.AddChild(Up);
            rateRow.AddChild(ToMax);
            targetItem.XamlChildren.Add(rateRow);

            Flow = List.AddItem(Loc.GetString(flowKey));
            Storage = List.AddItem(Loc.GetString(storageKey));
            Network = List.AddItem(Loc.GetString("ks-tgui-battery-network"));

            Section.AddChild(List);
        }

        public void SetBreaker(bool on)
        {
            BreakerOn = on;
            Breaker.Icon = on ? KsTguiIcons.SyncAlt : KsTguiIcons.Times;
            Breaker.Text = Loc.GetString(on ? "ks-tgui-apc-on" : "ks-tgui-apc-off");
            Breaker.Selected = on;
        }

        public void SetRange(float minWatts, float maxWatts, float currentWatts)
        {
            Rate.MinValue = minWatts / WattsPerKilowatt;
            Rate.MaxValue = maxWatts / WattsPerKilowatt;
            Rate.Step = MathF.Max(0.1f, (maxWatts - minWatts) / WattsPerKilowatt / 100f);
            Rate.StepPixelSize = 4f;
            Rate.SetValue(currentWatts / WattsPerKilowatt);

            ToMin.Disabled = currentWatts <= minWatts;
            Down.Disabled = currentWatts <= minWatts;
            Up.Disabled = currentWatts >= maxWatts;
            ToMax.Disabled = currentWatts >= maxWatts;
        }

        public void SetNetwork(bool hasPower)
        {
            Network.Text = Loc.GetString(hasPower ? "ks-tgui-battery-network-powered" : "ks-tgui-battery-network-unpowered");
            Network.TextColor = hasPower ? KsTguiPalettes.Good : KsTguiPalettes.Bad;
        }
    }
}
