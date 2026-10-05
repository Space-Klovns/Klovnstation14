using System.Globalization;
using Content.Shared._KS14.CCVar;
using Content.Shared.Localizations;
using Robust.Shared.Configuration;
using Robust.Shared.Localization;

namespace Content.Client._KS14.Localization;

/// <summary>
///     Applies the archived client locale while keeping English available for untranslated messages.
/// </summary>
public sealed partial class KsClientLocalizationManager
{
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private ILocalizationManager _localizationManager = default!;
    [Dependency] private ContentLocalizationManager _contentLocalizationManager = default!;

    public void Initialize()
    {
        _configurationManager.OnValueChanged(KsCCVars.ClientLocale, ApplyLocale, invokeImmediately: true);
    }

    private void ApplyLocale(string locale)
    {
        var culture = CultureInfo.GetCultureInfo(locale == "ru-RU" ? "ru-RU" : "en-US");
        var englishCulture = CultureInfo.GetCultureInfo("en-US");
        _contentLocalizationManager.LoadAdditionalCulture(culture);
        _localizationManager.SetFallbackCulture(englishCulture);

        if (_localizationManager.DefaultCulture?.Name == culture.Name)
            return;

        _localizationManager.SetCulture(culture);
        // SetCulture alone does not invalidate the engine's prototype-name cache.
        _localizationManager.ReloadLocalizations();
    }
}
