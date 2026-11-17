using System.Globalization;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.Shared.Localizations;

public sealed partial class ContentLocalizationManager
{
    private bool _prototypeMetadataWarmed;

    public void InitializeCultures()
    {
        WarmPrototypeMetadata();
        foreach (var culture in GetAvailableCultures())
            LoadAdditionalCulture(culture);
        _loc.SetFallbackCulture(CultureInfo.GetCultureInfo(DefaultCultureName));
    }

    private void WarmPrototypeMetadata()
    {
        if (_prototypeMetadataWarmed)
            return;
        foreach (var prototype in _prototypeLocalePrototypeManager.EnumeratePrototypes<EntityPrototype>())
            _loc.GetEntityData(prototype.ID);
        _prototypeMetadataWarmed = true;
    }

    /// <summary>
    /// Formats and validates one synchronous client request in its installed culture.
    /// </summary>
    public IDisposable BeginCultureScope(string requestedCulture)
    {
        var culture = ResolveCulture(requestedCulture);
        WarmPrototypeMetadata(); // Normally already done at startup; also handles tool prototype reloads.
        LoadAdditionalCulture(culture);
        _loc.SetFallbackCulture(CultureInfo.GetCultureInfo(DefaultCultureName));
        return new CultureScope(_loc, culture);
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly ILocalizationManager _localization;
        private readonly CultureInfo _previousCulture;

        public CultureScope(ILocalizationManager localization, CultureInfo culture)
        {
            _localization = localization;
            _previousCulture = localization.DefaultCulture ?? CultureInfo.GetCultureInfo(DefaultCultureName);
            localization.SetCulture(culture);
        }

        public void Dispose()
        {
            _localization.SetCulture(_previousCulture);
        }
    }
}
