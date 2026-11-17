using Content.Shared.Localizations;
using Content.Shared.RCD;
using Robust.Shared.Prototypes;

namespace Content.Client.RCD;

public sealed partial class RCDMenuBoundUserInterface
{
    [Dependency] private ContentLocalizationManager _contentLocalizationManager = default!;
    [Dependency] private ILocalizationManager _rcdLocalizationManager = default!;

    private string GetLocalizedConstructionName(RCDPrototype prototype)
    {
        if (prototype.Prototype != null && _prototypeManager.TryIndex<EntityPrototype>(prototype.Prototype, out var entityPrototype))
            return _contentLocalizationManager.GetLocalizedPrototypeName(entityPrototype);

        return _rcdLocalizationManager.HasString(prototype.SetName) ? Loc.GetString(prototype.SetName) : prototype.SetName;
    }
}
