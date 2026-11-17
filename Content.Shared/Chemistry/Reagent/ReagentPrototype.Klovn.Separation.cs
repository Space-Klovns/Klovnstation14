using Content.Shared._KS14.Chemistry.Separation;
using Robust.Shared.Prototypes;

namespace Content.Shared.Chemistry.Reagent;

public sealed partial class ReagentPrototype
{
    /// <summary>
    ///     A group of reagents no machine can pick apart: selecting this one in a ChemMaster, a plumbing filter, a plumbing
    ///         reactor and the like moves every member of its group present, in proportion. Only physical separation
    ///         (Fluidmos distillation, phase separation, freezing) splits them. Null for an ordinary reagent.
    /// </summary>
    /// <seealso cref="KsReagentSeparationSystem"/>
    [DataField("ksInseparableGroup")]
    public ProtoId<KsReagentSeparationGroupPrototype>? KsInseparableGroup;
}
