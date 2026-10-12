using Content.Shared.Localizations;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.PopupLocalization;

/// <summary>A prototype name resolved in the recipient's culture, including inherited names.</summary>
[Serializable, NetSerializable]
public sealed record KsPopupPrototypeName(string Id)
{
    public string Format()
    {
        var prototypes = IoCManager.Resolve<IPrototypeManager>();
        return prototypes.TryIndex<EntityPrototype>(Id, out var prototype)
            ? IoCManager.Resolve<ContentLocalizationManager>().GetLocalizedPrototypeName(prototype)
            : Id;
    }
}
