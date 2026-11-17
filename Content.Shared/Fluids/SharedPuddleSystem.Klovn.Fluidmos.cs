using Content.Shared._KS14.Fluidmos;
using Content.Shared.Chemistry.Reagent;

namespace Content.Shared.Fluids;

public abstract partial class SharedPuddleSystem
{
    [Dependency] private KsFluidmosPropertiesSystem _ksFluidmosPropertiesSystem = default!;

    /// <summary>
    ///     Whether a reagent leaves a puddle through Fluidmos rather than upstream's fixed evaporation rate: a reagent
    ///         linked to a gas evaporates into its tile as that gas (<c>BoilTileEffect</c>'s evaporate mode), conserving
    ///         its mass, so upstream must not also remove it. Unlinked reagents keep upstream's rate, which removes them
    ///         outright: the one place reagent mass is not conserved.
    /// </summary>
    private bool KsEvaporatesThroughFluidmos(ReagentPrototype reagentPrototype)
    {
        return _ksFluidmosPropertiesSystem.TryGetProperties(reagentPrototype.ID, out var properties) && properties.Linked;
    }
}
