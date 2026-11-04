using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Fluidmos;

/// <summary>
///     Identifies one reagent in a <see cref="KsFluidMixture"/>: its prototype and extra data. Holds no runtime indices,
///         so it is safe across pooled servers and in map saves.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct KsFluidReagentKey(ProtoId<ReagentPrototype> Prototype, KsReagentDataSet? Data)
{
    public ReagentId ToReagentId()
    {
        return new ReagentId(Prototype, Data?.ToList());
    }

    public static KsFluidReagentKey From(ReagentId reagentId)
    {
        return new KsFluidReagentKey(reagentId.Prototype, KsReagentDataSet.From(reagentId.Data));
    }
}
