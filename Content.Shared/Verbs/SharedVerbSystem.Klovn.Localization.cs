using Content.Shared.Localizations;

namespace Content.Shared.Verbs;

public abstract partial class SharedVerbSystem
{
    [Dependency] protected ContentLocalizationManager VerbLocalization = default!;
}
