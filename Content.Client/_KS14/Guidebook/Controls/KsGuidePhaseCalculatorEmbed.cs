using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using Content.Client.Guidebook.Richtext;
using Content.Client.Message;
using Content.Client._KS14.Tgui.Controls;
using Content.Shared._KS14.Atmos;
using Content.Shared._KS14.Fluidmos;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Reagent;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.Guidebook.Controls;

/// <summary>
///     A calculator on a guidebook page for where a reagent boils at a pressure, and its vapor pressure at a temperature,
///         from the same curve pipes use: <c>&lt;KsGuidePhaseCalculatorEmbed/&gt;</c>, or with
///         <c>Reagents="Water,Ammonia"</c> to list only those. By default it lists every reagent with Fluidmos data.
/// </summary>
/// <remarks>
///     Each input has its own answer right under it, in a section of their own: the pressure gives the boiling point, the
///         temperature the vapor pressure, and neither changes the other's. The melting and critical points belong to the
///         substance alone, so they sit outside it, under the substance's dropdown.
/// </remarks>
[UsedImplicitly]
public sealed partial class KsGuidePhaseCalculatorEmbed : BoxContainer, IDocumentTag
{
    [Dependency] private ILogManager _logManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IEntitySystemManager _entitySystemManager = default!;
    [Dependency] private ILocalizationManager _localizationManager = default!;

    private readonly OptionButton _reagentButton = new() { HorizontalExpand = true };
    private readonly LineEdit _pressureEdit = new() { Text = "101.325", HorizontalExpand = true };
    private readonly LineEdit _temperatureEdit = new() { Text = "293.15", HorizontalExpand = true };
    private readonly RichTextLabel _pressureResult = new() { HorizontalExpand = true, Margin = new Thickness(0, 0, 0, 4) };
    private readonly RichTextLabel _temperatureResult = new() { HorizontalExpand = true };
    private readonly RichTextLabel _substanceResult = new() { HorizontalExpand = true, Margin = new Thickness(0, 4, 0, 0) };
    private readonly List<ReagentPrototype> _reagents = new();

    public KsGuidePhaseCalculatorEmbed()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        Margin = new Thickness(0, 4);
    }

    /// <summary>
    ///     The reagent shown, by its index in the list.
    /// </summary>
    public int SelectedIndex => _reagentButton.SelectedId;

    /// <summary>
    ///     The reagents listed, in order, for tests.
    /// </summary>
    public IReadOnlyList<ReagentPrototype> Reagents => _reagents;

    /// <summary>
    ///     What the pressure gives: where the substance boils at it.
    /// </summary>
    public string PressureResultText => _pressureResult.GetMessage() ?? string.Empty;

    /// <summary>
    ///     What the temperature gives: the substance's vapor pressure at it. Empty when the substance never boils.
    /// </summary>
    public string TemperatureResultText => _temperatureResult.Visible ? _temperatureResult.GetMessage() ?? string.Empty : string.Empty;

    /// <summary>
    ///     What belongs to the substance alone: where it melts, and its critical point.
    /// </summary>
    public string SubstanceResultText => _substanceResult.GetMessage() ?? string.Empty;

    /// <summary>
    ///     Every answer shown, one per line.
    /// </summary>
    public string ResultText => string.Join("\n", new[] { PressureResultText, TemperatureResultText, SubstanceResultText }
        .Where(text => text.Length > 0));

    public bool TryParseTag(Dictionary<string, string> args, [NotNullWhen(true)] out Control? control)
    {
        control = null;
        var sawmill = _logManager.GetSawmill("guidebook.phase_calculator");

        if (args.TryGetValue("Reagents", out var reagents))
        {
            foreach (var reagentId in reagents.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (!_prototypeManager.TryIndex<ReagentPrototype>(reagentId, out var reagent))
                {
                    sawmill.Error($"\"{reagentId}\" is not a reagent prototype");
                    return false;
                }

                // Listed twice, it would show twice.
                if (!_reagents.Contains(reagent))
                    _reagents.Add(reagent);
            }
        }
        else
        {
            _reagents.AddRange(_prototypeManager.EnumeratePrototypes<ReagentPrototype>()
                .Where(reagent => reagent.Fluidmos != null && reagent.BoilingPoint != null));
        }

        // Alphabetical by the name the player sees, in their language, however the page listed them.
        _reagents.Sort((first, second) => StringComparer.CurrentCultureIgnoreCase.Compare(first.LocalizedName, second.LocalizedName));

        if (_reagents.Count == 0)
        {
            sawmill.Error("Phase calculator embed has no reagents to list");
            return false;
        }

        for (var i = 0; i < _reagents.Count; i++)
        {
            _reagentButton.AddItem(_reagents[i].LocalizedName, i);
        }

        _reagentButton.OnItemSelected += args =>
        {
            _reagentButton.SelectId(args.Id);
            UpdateResults();
        };
        _pressureEdit.OnTextChanged += _ => UpdatePressureResult();
        _temperatureEdit.OnTextChanged += _ => UpdateTemperatureResult();

        AddChild(MakeRow("ks-guidebook-phase-calculator-reagent", _reagentButton));

        // A tgui section under the tgui theme, a dark panel under Baron: either way, a box that says these inputs and their
        //      answers go together, and that the substance's own points below don't depend on them.
        var inputs = new KsTguiSection { Margin = new Thickness(0, 4, 0, 0) };
        var inputsBox = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
        inputsBox.AddChild(MakeRow("ks-guidebook-phase-calculator-pressure", _pressureEdit));
        inputsBox.AddChild(_pressureResult);
        inputsBox.AddChild(MakeRow("ks-guidebook-phase-calculator-temperature", _temperatureEdit));
        inputsBox.AddChild(_temperatureResult);
        inputs.XamlChildren.Add(inputsBox);
        AddChild(inputs);

        AddChild(_substanceResult);

        UpdateResults();
        control = this;
        return true;
    }

    /// <summary>
    ///     Sets the inputs as a player typing them would, for tests.
    /// </summary>
    public void SetInputs(int reagentIndex, string pressure, string temperature)
    {
        _reagentButton.SelectId(reagentIndex);
        _pressureEdit.Text = pressure;
        _temperatureEdit.Text = temperature;
        UpdateResults();
    }

    /// <summary>
    ///     Types a pressure alone, as a player would, for tests.
    /// </summary>
    public void SetPressure(string pressure)
    {
        _pressureEdit.Text = pressure;
        UpdatePressureResult();
    }

    /// <summary>
    ///     Types a temperature alone, as a player would, for tests.
    /// </summary>
    public void SetTemperature(string temperature)
    {
        _temperatureEdit.Text = temperature;
        UpdateTemperatureResult();
    }

    private bool TryGetProperties([NotNullWhen(true)] out KsFluidReagentProperties? properties)
    {
        var reagent = _reagents[Math.Clamp(_reagentButton.SelectedId, 0, _reagents.Count - 1)];
        return _entitySystemManager.GetEntitySystem<KsFluidmosPropertiesSystem>().TryGetProperties(reagent.ID, out properties);
    }

    /// <summary>
    ///     The substance changed: every answer is new.
    /// </summary>
    private void UpdateResults()
    {
        UpdatePressureResult();
        UpdateTemperatureResult();
        UpdateSubstanceResult();
    }

    private void UpdatePressureResult()
    {
        if (!TryGetProperties(out var properties))
        {
            _pressureResult.SetMarkup(string.Empty);
            return;
        }

        // Nothing makes it boil: one answer for both inputs, in the pressure's place, and the temperature's is hidden.
        if (!properties.Volatile)
        {
            _pressureResult.SetMarkup(Loc.GetString("ks-guidebook-phase-calculator-never-boils"));
            return;
        }

        var pressure = ParseNumber(_pressureEdit.Text);
        if (pressure is not > 0f)
        {
            _pressureResult.SetMarkup(Loc.GetString("ks-guidebook-phase-calculator-bad-pressure"));
            return;
        }

        var boilingPoint = KsPhaseMath.BoilingPointAt(properties.VaporizationEntropy, properties.BoilingPoint, pressure.Value);
        _pressureResult.SetMarkup(float.IsPositiveInfinity(boilingPoint)
            ? Loc.GetString("ks-guidebook-phase-calculator-never-boils-pressure")
            : Loc.GetString("ks-guidebook-phase-calculator-boils", ("kelvin", boilingPoint), ("celsius", boilingPoint - Atmospherics.T0C)));
    }

    private void UpdateTemperatureResult()
    {
        if (!TryGetProperties(out var properties) || !properties.Volatile)
        {
            _temperatureResult.Visible = false;
            _temperatureResult.SetMarkup(string.Empty);
            return;
        }

        _temperatureResult.Visible = true;
        var temperature = ParseNumber(_temperatureEdit.Text);
        if (temperature is not > 0f)
        {
            _temperatureResult.SetMarkup(Loc.GetString("ks-guidebook-phase-calculator-bad-temperature"));
            return;
        }

        // A vapor pressure under the pipes' own floor is one they treat as not evaporating at all.
        var vaporPressure = KsPhaseMath.SaturationPressure(properties.VaporizationEntropy, properties.BoilingPoint, temperature.Value);
        _temperatureResult.SetMarkup(vaporPressure < KsFluidmos.MinActivePressure
            ? Loc.GetString("ks-guidebook-phase-calculator-never-boils-temperature")
            : Loc.GetString("ks-guidebook-phase-calculator-vapor-pressure", ("kpa", vaporPressure)));
    }

    private void UpdateSubstanceResult()
    {
        if (!TryGetProperties(out var properties))
        {
            _substanceResult.SetMarkup(string.Empty);
            return;
        }

        var lines = new List<string>
        {
            properties.Freezable
                ? Loc.GetString("ks-guidebook-phase-calculator-melts", ("kelvin", properties.MeltingPoint), ("celsius", properties.MeltingPoint - Atmospherics.T0C))
                : Loc.GetString("ks-guidebook-phase-calculator-never-freezes"),
        };

        if (properties.Linked && float.IsFinite(properties.CriticalTemperature))
        {
            var criticalPressure = _entitySystemManager.GetEntitySystem<KsGasThermoSystem>().CriticalPressures[properties.GasIndex];
            lines.Add(Loc.GetString("ks-guidebook-phase-calculator-critical",
                ("kelvin", properties.CriticalTemperature),
                ("kpa", criticalPressure)));
        }

        // Markup, so the numbers stand out from the words around them.
        _substanceResult.SetMarkup(string.Join("\n", lines));
    }

    /// <summary>
    ///     A number as typed, or null. A point is always a decimal point. A comma is a decimal point where the player's
    ///         language writes one (Russian "7,4"), and otherwise groups thousands ("7,400"). With both, whichever comes last
    ///         is the decimal point and the other groups thousands. Spaces group thousands too.
    /// </summary>
    public static float? ParseNumber(string text, CultureInfo culture)
    {
        var cleaned = new string(text.Where(character => !char.IsWhiteSpace(character)).ToArray());
        var lastComma = cleaned.LastIndexOf(',');
        var lastPoint = cleaned.LastIndexOf('.');

        if (lastComma >= 0 && lastPoint >= 0)
        {
            cleaned = lastComma > lastPoint
                ? cleaned.Replace(".", string.Empty).Replace(',', '.')
                : cleaned.Replace(",", string.Empty);
        }
        else if (lastComma >= 0)
        {
            var commaIsDecimal = culture.NumberFormat.NumberDecimalSeparator == "," && cleaned.IndexOf(',') == lastComma;
            cleaned = commaIsDecimal ? cleaned.Replace(',', '.') : cleaned.Replace(",", string.Empty);
        }

        return float.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && float.IsFinite(value)
            ? value
            : null;
    }

    private float? ParseNumber(string text)
    {
        return ParseNumber(text, _localizationManager.DefaultCulture ?? CultureInfo.CurrentCulture);
    }

    private static BoxContainer MakeRow(string labelKey, Control input)
    {
        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 2) };
        row.AddChild(new Label { Text = Loc.GetString(labelKey), MinWidth = 140 });
        row.AddChild(input);
        return row;
    }
}
