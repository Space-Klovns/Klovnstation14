using Content.Shared.Localizations;

namespace Content.Shared.Hands.EntitySystems;

public abstract partial class SharedHandsSystem
{
    [Dependency] private ContentLocalizationManager _contentLocalizationManager = default!;
}
