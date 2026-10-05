using Robust.Shared.Localization;
using Robust.Shared.Utility;

namespace Content.Client.Guidebook;

public sealed partial class DocumentParsingManager
{
    [Dependency] private ILocalizationManager _localizationManager = default!;

    public ResPath GetLocalizedDocumentPath(ResPath originalPath)
    {
        var culture = _localizationManager.DefaultCulture?.Name ?? "en-US";
        var original = originalPath.ToString().TrimStart('/');
        if (culture == "en-US" || !original.StartsWith("ServerInfo/", StringComparison.Ordinal))
            return originalPath;

        var localizedPath = new ResPath($"/ServerInfo/_KS14/Guidebook/{culture}/{original[11..]}");
        return _resourceManager.ContentFileExists(localizedPath) ? localizedPath : originalPath;
    }
}
