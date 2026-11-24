using Content.Shared.Damage.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared.Damage.Systems;

public sealed partial class DamageableSystem
{
    /// <returns>If the damage container can take the given damage type. A null container takes every type.</returns>
    public bool KsContainerSupportsType(ProtoId<DamageContainerPrototype>? container, ProtoId<DamageTypePrototype> type)
    {
        return SupportsType(container, type);
    }
}
