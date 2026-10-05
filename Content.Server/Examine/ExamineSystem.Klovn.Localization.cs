using System.Globalization;
using Content.Server._KS14.Localization;
using Content.Shared.Localizations;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server.Examine;

public sealed partial class ExamineSystem
{
    [Dependency] private ILocalizationManager _localizationManager = default!;
    [Dependency] private ContentLocalizationManager _contentLocalizationManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    private IDisposable BeginExamineLocale(string requestedLocale)
    {
        var culture = CultureInfo.GetCultureInfo(requestedLocale == "ru-RU" ? "ru-RU" : "en-US");
        // Prototype metadata caches belong to the server's ordinary culture. Populate
        // them before temporarily formatting the response in another language.
        if (!_localizationManager.HasCulture(culture))
        {
            foreach (var prototype in _prototypeManager.EnumeratePrototypes<EntityPrototype>())
                _localizationManager.GetEntityData(prototype.ID);
        }
        _contentLocalizationManager.LoadAdditionalCulture(culture);
        _localizationManager.SetFallbackCulture(CultureInfo.GetCultureInfo("en-US"));
        return new KsExamineLocaleScope(_localizationManager, culture);
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

        foreach (var parentPrototype in _prototypeManager.EnumerateParents<EntityPrototype>(prototype.ID, includeSelf: true))
        {
            if (parentPrototype == null)
                continue;
            var localizationId = parentPrototype.CustomLocalizationID ?? $"ent-{parentPrototype.ID}";
            if (_localizationManager.TryGetString($"{localizationId}.desc", out var description))
                return description;
            if (parentPrototype.SetDesc != null)
                return parentPrototype.SetDesc;
        }

        return "";
    }
}
