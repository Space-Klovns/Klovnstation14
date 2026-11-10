using Content.Server._KS14.Atmos;
using Content.Server._KS14.Fluidmos;
using Content.Shared._KS14.Fluidmos;
using Content.Shared.Atmos;
using Robust.Shared.Utility;

namespace Content.Server.Atmos.EntitySystems;

public sealed partial class AtmosphereSystem
{
    [Dependency] private KsFluidmosPhaseSystem _ksFluidmosPhaseSystem = default!;
    [Dependency] private KsFluidmosTileSystem _ksFluidmosTileSystem = default!;
    [Dependency] private KsTransferThermoSystem _ksTransferThermoSystem = default!;

    /// <summary>
    ///     The fluid half of <see cref="DivideInto"/>: each receiver gets its volume's share of every phase, solids
    ///         included, since they physically sit in those pipes. The source is not modified.
    /// </summary>
    private static void KsDivideFluidInto(GasMixture source, GasMixture receiver, float fraction)
    {
        if (source.Fluid is not { } fluid || fluid.IsEmpty)
            return;

        if (!receiver.FluidCapable)
        {
            DebugTools.Assert(false, "A pipe net divided its fluid into a mixture that cannot hold it.");
            return;
        }

        receiver.Fluid ??= new KsFluidMixture(fluid.Count);
        receiver.Fluid.Add(fluid, fraction);
    }
}
