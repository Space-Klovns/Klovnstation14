using Content.Shared._KS14.Fluids.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;

namespace Content.Shared.Fluids;

public abstract partial class SharedPuddleSystem
{
    [Dependency] private EntityQuery<AtmosEvaporatingPuddleComponent> _atmosEvaporatingPuddleQuery = default!;

    protected FixedPoint2 GetModifiedEvaporationRate(Entity<PuddleComponent> puddle, FixedPoint2 evaporationRate)
    {
        if (!_atmosEvaporatingPuddleQuery.TryGetComponent(puddle, out var atmosEvaporatingPuddleComponent))
            return evaporationRate;

        return evaporationRate + atmosEvaporatingPuddleComponent.EvaporationAmount;
    }
}
