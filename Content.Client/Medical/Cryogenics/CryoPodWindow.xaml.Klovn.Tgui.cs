using Content.Client._KS14.Tgui;
using Content.Shared.Temperature;

namespace Content.Client.Medical.Cryogenics;

public sealed partial class CryoPodWindow
{
    /// <summary>
    ///     The stock line, with the number easing under tgui, as tg's cell temperature does. Null is no gas to read.
    /// </summary>
    public void KsShowTemperature(float? kelvin)
    {
        Temperature.Instant = !KsTguiTheme.ReadEnabled();

        if (kelvin is not { } temperature)
        {
            Temperature.ShowText(Loc.GetString("generic-not-available-shorthand"));
            return;
        }

        Temperature.Format = KsFormatTemperature;
        Temperature.Value = temperature;
    }

    private static string KsFormatTemperature(float kelvin)
    {
        var celsius = TemperatureHelpers.KelvinToCelsius(kelvin);
        return Loc.GetString("gas-analyzer-window-temperature-val-text",
            ("tempK", $"{kelvin:0.0}"),
            ("tempC", $"{celsius:0.0}"));
    }
}
