using Content.Shared.Localizations;

namespace Content.Shared.Verbs;

public sealed partial class RequestServerVerbsEvent
{
    public string ClientLocale = ContentLocalizationManager.DefaultCultureName;
}

public sealed partial class ExecuteVerbEvent
{
    public string ClientLocale = ContentLocalizationManager.DefaultCultureName;
}
