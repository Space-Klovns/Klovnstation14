using System.Globalization;
using Robust.Shared.Localization;

namespace Content.Server._KS14.Localization;

/// <summary>
/// Formats one synchronous examine response in its recipient's language, then restores server culture.
/// </summary>
public sealed class KsExamineLocaleScope : IDisposable
{
    private readonly ILocalizationManager _localizationManager;
    private readonly CultureInfo _previousCulture;

    public KsExamineLocaleScope(ILocalizationManager localizationManager, CultureInfo culture)
    {
        _localizationManager = localizationManager;
        _previousCulture = localizationManager.DefaultCulture ?? CultureInfo.GetCultureInfo("en-US");
        localizationManager.SetCulture(culture);
    }

    public void Dispose()
    {
        _localizationManager.SetCulture(_previousCulture);
    }
}
