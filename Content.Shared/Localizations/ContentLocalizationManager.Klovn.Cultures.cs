using System.Globalization;
using System.Linq;
using Robust.Shared.ContentPack;

namespace Content.Shared.Localizations;

public sealed partial class ContentLocalizationManager
{
    public const string DefaultCultureName = "en-US";

    [Dependency] private IResourceManager _localizationResourceManager = default!;
    private IReadOnlyList<CultureInfo>? _availableCultures;
    private readonly HashSet<string> _configuredCultures = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<CultureInfo> GetAvailableCultures()
    {
        return _availableCultures ?? RefreshAvailableCultures();
    }

    /// <summary>
    /// Refresh after mounting a language resource pack. Only registered content cultures are selectable.
    /// </summary>
    public IReadOnlyList<CultureInfo> RefreshAvailableCultures()
    {
        var cultures = new Dictionary<string, CultureInfo>(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultCultureName] = CultureInfo.GetCultureInfo(DefaultCultureName),
        };
        var paths = _localizationResourceManager.ContentFindFiles("/Locale")
            .Concat(_localizationResourceManager.ContentFindFiles("/Uploaded"));
        foreach (var path in paths)
        {
            // Content languages opt in by translating these settings labels. Engine-only locales
            // also share /Locale, but do not provide translations for the game itself.
            if (!path.ToString().EndsWith("/_KS14/Localization/options.ftl", StringComparison.Ordinal))
                continue;
            var parts = path.ToString().Split('/', StringSplitOptions.RemoveEmptyEntries);
            var localeIndex = Array.IndexOf(parts, "Locale");
            if (localeIndex < 0 || localeIndex + 2 >= parts.Length)
                continue;
            try
            {
                var culture = CultureInfo.GetCultureInfo(parts[localeIndex + 1]);
                if (culture.Name.Length > 0)
                    cultures.TryAdd(culture.Name, culture);
            }
            catch (ArgumentException)
            {
                // A resource directory is not necessarily a supported culture name.
            }
        }
        _availableCultures = cultures.Values.OrderBy(culture => culture.Name != DefaultCultureName)
            .ThenBy(culture => culture.NativeName, StringComparer.Ordinal).ToArray();
        _prototypeNameMessages.Clear();
        return _availableCultures;
    }

    public CultureInfo ResolveCulture(string? requestedCulture)
    {
        return GetAvailableCultures().FirstOrDefault(culture =>
            culture.Name.Equals(requestedCulture, StringComparison.OrdinalIgnoreCase))
            ?? CultureInfo.GetCultureInfo(DefaultCultureName);
    }

    /// <summary>
    ///     Load a culture with the formatting functions used by content on either client or server.
    /// </summary>
    public void LoadAdditionalCulture(CultureInfo culture)
    {
        if (!_loc.HasCulture(culture))
            _loc.LoadCulture(culture);
        if (!_configuredCultures.Add(culture.Name))
            return;

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
        if (culture.TwoLetterISOLanguageName == "en")
        {
            _loc.AddFunction(culture, "MAKEPLURAL", FormatMakePlural);
            _loc.AddFunction(culture, "MANY", FormatMany);
        }
        else
        {
            // Other languages express their own inflections through Fluent select expressions.
            _loc.AddFunction(culture, "MAKEPLURAL", args => args.Args[0]);
            _loc.AddFunction(culture, "MANY", args => args.Args[0]);
        }
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
