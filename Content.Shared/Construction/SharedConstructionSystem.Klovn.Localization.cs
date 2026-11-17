using Content.Shared.Localizations;

namespace Content.Shared.Construction;

public abstract partial class SharedConstructionSystem
{
    [Dependency] private ContentLocalizationManager _contentLocalizationManager = default!;
}
