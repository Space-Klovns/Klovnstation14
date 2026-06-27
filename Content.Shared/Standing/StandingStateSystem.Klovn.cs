using Content.Shared.Gravity;
using Content.Shared.Stunnable;

namespace Content.Shared.Standing;

/*
    There is an upstream bug where after going weightless and getting forced to stand,
        going back to gravity will have you stay standed *even when* you shouldn't be able to
        (like when you have no legs)

    This fixes that by checking for standing again when you are gravved
*/

public sealed partial class StandingStateSystem : EntitySystem
{
    public void InitialiseKlovn()
    {
        base.Initialize();
        SubscribeLocalEvent<StandingStateComponent, WeightlessnessChangedEvent>(KsOnWeightlessnessChanged);
    }

    private void KsOnWeightlessnessChanged(Entity<StandingStateComponent> entity, ref WeightlessnessChangedEvent args)
    {
        if (HasComp<KnockedDownComponent>(entity))
            return;

        var ev = new StandUpAttemptEvent();
        RaiseLocalEvent(entity, ref ev);
        if (!ev.Cancelled)
            return;

        EnsureComp<KnockedDownComponent>(entity);
    }
}
