using Content.Shared._KS14.Fluids.Components;

namespace Content.Server._KS14.Fluids;

public sealed class AtmosEvaporatingPuddleSystem : EntitySystem
{
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
}
