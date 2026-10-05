using System.Globalization;

namespace Content.Shared.Localizations;

public sealed partial class ContentLocalizationManager
{
    /// <summary>
    ///     Load an additional client culture with the formatting functions used by content.
    /// </summary>
    public void LoadAdditionalCulture(CultureInfo culture)
    {
        if (_loc.HasCulture(culture))
            return;

        _loc.LoadCulture(culture);
        _loc.AddFunction(culture, "PRESSURE", FormatPressure);
        _loc.AddFunction(culture, "POWERWATTS", FormatPowerWatts);
        _loc.AddFunction(culture, "POWERJOULES", FormatPowerJoules);
        _loc.AddFunction(culture, "ENERGYWATTHOURS", FormatEnergyWattHours);
        _loc.AddFunction(culture, "UNITS", FormatUnits);
        _loc.AddFunction(culture, "TOSTRING", args => FormatToString(culture, args));
        _loc.AddFunction(culture, "LOC", FormatLoc);
        _loc.AddFunction(culture, "NATURALFIXED", args => FormatCultureNumber(culture, args, percent: false));
        _loc.AddFunction(culture, "NATURALPERCENT", args => FormatCultureNumber(culture, args, percent: true));
        _loc.AddFunction(culture, "PLAYTIME", FormatPlaytime);
        // Russian translations provide inflected words/select expressions themselves.
        // Applying English plural suffixes to these would produce malformed Russian words.
        _loc.AddFunction(culture, "MAKEPLURAL", args => args.Args[0]);
        _loc.AddFunction(culture, "MANY", args => args.Args[0]);
    }

    private static ILocValue FormatCultureNumber(CultureInfo culture, LocArgs args, bool percent)
    {
        var number = ((LocValueNumber)args.Args[0]).Value * (percent ? 100 : 1);
        var maxDecimals = (int)Math.Floor(((LocValueNumber)args.Args[1]).Value);
        var formatter = (NumberFormatInfo)culture.NumberFormat.Clone();
        formatter.NumberDecimalDigits = maxDecimals;
        var value = string.Format(formatter, "{0:N}", number);
        if (maxDecimals > 0)
            value = value.TrimEnd('0').TrimEnd(char.Parse(formatter.NumberDecimalSeparator));

        return new LocValueString(value + (percent ? "%" : ""));
    }
}
