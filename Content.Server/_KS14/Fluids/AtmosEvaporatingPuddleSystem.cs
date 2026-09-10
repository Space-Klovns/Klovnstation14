using Content.Shared._KS14.Fluids.Components;
using Content.Shared.FixedPoint;

namespace Content.Server._KS14.Fluids;

public sealed partial class AtmosEvaporatingPuddleSystem : EntitySystem
{
    [Dependency] private EntityQuery<AtmosEvaporatingPuddleComponent> _query = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var eqe = EntityQueryEnumerator<AtmosEvaporatingPuddleComponent>();
        while (eqe.MoveNext(out var uid, out var evaporatingPuddleComponent))
        {
            evaporatingPuddleComponent.Lifetime -= TimeSpan.FromSeconds(frameTime);
            if (evaporatingPuddleComponent.Lifetime > TimeSpan.Zero)
                continue;

            RemComp(uid, evaporatingPuddleComponent);
        }
    }

    public FixedPoint2 GetEvaporation(EntityUid uid)
        => _query.CompOrNull(uid)?.EvaporationAmount ?? FixedPoint2.Zero;
}
