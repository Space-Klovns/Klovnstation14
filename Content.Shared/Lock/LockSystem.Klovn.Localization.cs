using Content.Shared.Localizations;

namespace Content.Shared.Lock;

public sealed partial class LockSystem
{
    [Dependency] private ContentLocalizationManager _contentLocalizationManager = default!;
}
