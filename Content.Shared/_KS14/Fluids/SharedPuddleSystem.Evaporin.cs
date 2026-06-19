using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;

namespace Content.Shared.Fluids;

public abstract partial class SharedPuddleSystem
{
    protected virtual void ModifyEvaporationRate(Entity<PuddleComponent> puddle, ref FixedPoint2 evaporateRate) { }
}
