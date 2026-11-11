using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using Content.Client.Guidebook.Richtext;
using Content.Client.Message;
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
[UsedImplicitly]
public sealed partial class KsGuidePhaseCalculatorEmbed : BoxContainer, IDocumentTag
{
    [Dependency] private ILogManager _logManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IEntitySystemManager _entitySystemManager = default!;

    private readonly OptionButton _reagentButton = new() { HorizontalExpand = true };
    private readonly LineEdit _pressureEdit = new() { Text = "101.325", HorizontalExpand = true };
    private readonly LineEdit _temperatureEdit = new() { Text = "293.15", HorizontalExpand = true };
    private readonly RichTextLabel _result = new() { HorizontalExpand = true, Margin = new Thickness(0, 6, 0, 0) };
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

    public string ResultText => _result.GetMessage() ?? string.Empty;

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
            UpdateResult();
        };
        _pressureEdit.OnTextChanged += _ => UpdateResult();
        _temperatureEdit.OnTextChanged += _ => UpdateResult();

        AddChild(MakeRow("ks-guidebook-phase-calculator-reagent", _reagentButton));
        AddChild(MakeRow("ks-guidebook-phase-calculator-pressure", _pressureEdit));
        AddChild(MakeRow("ks-guidebook-phase-calculator-temperature", _temperatureEdit));
        AddChild(_result);

        UpdateResult();
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
        UpdateResult();
    }

    private void UpdateResult()
    {
        var reagent = _reagents[Math.Clamp(_reagentButton.SelectedId, 0, _reagents.Count - 1)];
        var propertiesSystem = _entitySystemManager.GetEntitySystem<KsFluidmosPropertiesSystem>();
        if (!propertiesSystem.TryGetProperties(reagent.ID, out var properties))
        {
            _result.SetMarkup(string.Empty);
            return;
        }

        var lines = new List<string>();
        var pressure = ParseNumber(_pressureEdit.Text);
        var temperature = ParseNumber(_temperatureEdit.Text);

        if (!properties.Volatile)
        {
            lines.Add(Loc.GetString("ks-guidebook-phase-calculator-never-boils"));
        }
        else
        {
            if (pressure is > 0f)
            {
                var boilingPoint = KsPhaseMath.BoilingPointAt(properties.VaporizationEntropy, properties.BoilingPoint, pressure.Value);
                lines.Add(float.IsFinite(boilingPoint)
                    ? Loc.GetString("ks-guidebook-phase-calculator-boils", ("kelvin", boilingPoint), ("celsius", boilingPoint - Atmospherics.T0C))
                    : Loc.GetString("ks-guidebook-phase-calculator-never-boils"));
            }
            else
            {
                lines.Add(Loc.GetString("ks-guidebook-phase-calculator-bad-pressure"));
            }

            if (temperature is > 0f)
            {
                var vaporPressure = KsPhaseMath.SaturationPressure(properties.VaporizationEntropy, properties.BoilingPoint, temperature.Value);
                lines.Add(Loc.GetString("ks-guidebook-phase-calculator-vapor-pressure", ("kpa", vaporPressure)));
            }
            else
            {
                lines.Add(Loc.GetString("ks-guidebook-phase-calculator-bad-temperature"));
            }
        }

        lines.Add(properties.Freezable
            ? Loc.GetString("ks-guidebook-phase-calculator-melts", ("kelvin", properties.MeltingPoint), ("celsius", properties.MeltingPoint - Atmospherics.T0C))
            : Loc.GetString("ks-guidebook-phase-calculator-never-freezes"));

        if (properties.Linked && float.IsFinite(properties.CriticalTemperature))
        {
            var criticalPressure = _entitySystemManager.GetEntitySystem<KsGasThermoSystem>().CriticalPressures[properties.GasIndex];
            lines.Add(Loc.GetString("ks-guidebook-phase-calculator-critical",
                ("kelvin", properties.CriticalTemperature),
                ("kpa", criticalPressure)));
        }

        // Markup, so the numbers stand out from the words around them.
        _result.SetMarkup(string.Join("\n", lines));
    }

    /// <summary>
    ///     A number as typed, with either a point or a comma for the decimals, or null.
    /// </summary>
    private static float? ParseNumber(string text)
    {
        return float.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && float.IsFinite(value)
            ? value
            : null;
    }

    private static BoxContainer MakeRow(string labelKey, Control input)
    {
        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 2) };
        row.AddChild(new Label { Text = Loc.GetString(labelKey), MinWidth = 140 });
        row.AddChild(input);
        return row;
    }
}
