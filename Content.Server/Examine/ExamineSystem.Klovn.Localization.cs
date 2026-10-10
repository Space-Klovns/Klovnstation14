using Content.Shared.Localizations;
using Robust.Shared.Localization;
using Robust.Shared.Utility;

namespace Content.Server.Examine;

public sealed partial class ExamineSystem
{
    [Dependency] private ContentLocalizationManager _contentLocalizationManager = default!;

    private IDisposable BeginExamineLocale(string requestedLocale)
    {
        return _contentLocalizationManager.BeginCultureScope(requestedLocale);
    }

    private FormattedMessage LocalizedExamineError(string localizationId)
    {
        var message = new FormattedMessage();
        message.AddText(Loc.GetString(localizationId));
        return message;
    }

    private string? GetLocalizedExamineDescription(EntityUid entityUid)
    {
        var metadataComponent = MetaData(entityUid);
        var prototype = metadataComponent.EntityPrototype;
        if (prototype == null || metadataComponent.EntityDescription != prototype.Description)
            return null; // Preserve descriptions deliberately changed in game.

        return _contentLocalizationManager.GetLocalizedPrototypeDescription(prototype);
    }
}
