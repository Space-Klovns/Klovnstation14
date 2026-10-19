using Content.Shared.Localizations;
using Content.Shared.RCD;
using Robust.Shared.Prototypes;

namespace Content.Shared.RCD.Systems;

public sealed partial class RCDSystem
{
    [Dependency] private ContentLocalizationManager _contentLocalizationManager = default!;

    private string GetLocalizedConstructionName(RCDPrototype prototype)
    {
        if (prototype.Prototype != null && ProtoMan.TryIndex<EntityPrototype>(prototype.Prototype, out var entityPrototype))
            return _contentLocalizationManager.GetLocalizedPrototypeName(entityPrototype);

        return Loc.HasString(prototype.SetName) ? Loc.GetString(prototype.SetName) : prototype.SetName;
    }
}
