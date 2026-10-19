using System.Globalization;
using Content.Shared.Localizations;
using Robust.Shared.Localization;
using Robust.Shared.Utility;

namespace Content.Client.Guidebook;

public sealed partial class DocumentParsingManager
{
    [Dependency] private ILocalizationManager _localizationManager = default!;

    public ResPath GetLocalizedDocumentPath(ResPath originalPath)
    {
        var culture = _localizationManager.DefaultCulture ?? CultureInfo.GetCultureInfo(ContentLocalizationManager.DefaultCultureName);
        var original = originalPath.ToString().TrimStart('/');
        if (!original.StartsWith("ServerInfo/", StringComparison.Ordinal))
            return originalPath;

        while (culture.Name.Length > 0)
        {
            var localizedPath = new ResPath($"/ServerInfo/_KS14/Guidebook/{culture.Name}/{original[11..]}");
            if (_resourceManager.ContentFileExists(localizedPath))
                return localizedPath;
            culture = culture.Parent;
        }
        return originalPath;
    }
}
