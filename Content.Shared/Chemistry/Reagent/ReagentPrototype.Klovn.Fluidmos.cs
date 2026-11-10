using Content.Shared._KS14.Fluidmos;

namespace Content.Shared.Chemistry.Reagent;

public sealed partial class ReagentPrototype
{
    /// <summary>
    ///     How this reagent behaves inside atmos mixtures (pipes, canisters, tanks). Null takes every default; the boiling
    ///         and melting points come from <see cref="BoilingPoint"/> and <see cref="MeltingPoint"/>.
    /// </summary>
    [DataField]
    public KsFluidmosReagentData? Fluidmos;
}
