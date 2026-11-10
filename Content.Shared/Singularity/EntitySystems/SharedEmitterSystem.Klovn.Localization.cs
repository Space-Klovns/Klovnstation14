using Content.Shared.Localizations;

namespace Content.Shared.Singularity.EntitySystems;

public abstract partial class SharedEmitterSystem
{
    [Dependency] private ContentLocalizationManager _contentLocalizationManager = default!;
}
