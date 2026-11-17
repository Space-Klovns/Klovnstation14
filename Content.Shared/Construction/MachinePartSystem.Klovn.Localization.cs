using Content.Shared.Localizations;

namespace Content.Shared.Construction;

public sealed partial class MachinePartSystem
{
    [Dependency] private ContentLocalizationManager _contentLocalizationManager = default!;
}
