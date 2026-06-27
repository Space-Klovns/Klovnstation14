using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;

namespace Content.Server.Fluids.EntitySystems;

public sealed partial class PuddleSystem
{
    [Dependency] private readonly AtmosphereSystem _atmosphereSystem = default!;

    protected override void ModifyEvaporationRate(Entity<PuddleComponent> puddle, ref FixedPoint2 evaporateRate)
    {
        if (_atmosphereSystem.GetTileMixture(puddle.Owner, true) is { } mixture)
        {
            var moles = mixture.GetMoles(Gas.Evaporin);
            if (moles > Atmospherics.GasMinMoles)
            {
                // Add a flat rate scaled by moles, making it extremely fast
                // e.g. 1 mole = 3.0 evaporation speed
                evaporateRate += FixedPoint2.New(moles * puddle.Comp.EvaporinEvaporationMultiplier);
            }
        }
    }
}
