using Robust.Shared.Timing;

namespace Content.Shared._KS14.DodgingEffect;

public sealed class DodgingEffectSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _gameTiming.CurTime;

        var eqe = EntityQueryEnumerator<DodgingEffectComponent>();
        while (eqe.MoveNext(out var uid, out var component))
        {
            if (curTime < component.TimeUntilFinished)
            {
                if (curTime < component.TimeUntilNextEffect)
                    continue;

                component.TimeUntilNextEffect = curTime + component.Interval;
                component.Data.Add(Transform(uid).Coordinates);
                continue;
            }

            RemComp(uid, component);
        }
    }

    // Did you know the VV attribute can be added to methods so they can be used with vvinvoke?
    [ViewVariables]
    public void AddEffect(EntityUid uid, TimeSpan interval, TimeSpan duration)
    {
        var component = EnsureComp<DodgingEffectComponent>(uid);
        component.Interval = interval;
        component.StartTime = _gameTiming.CurTime;
        component.TimeUntilNextEffect = TimeSpan.MinValue;
        component.TimeUntilFinished = _gameTiming.CurTime + duration;

        DirtyFields(uid, component, null, nameof(component.Interval), nameof(component.StartTime), nameof(component.TimeUntilNextEffect), nameof(component.TimeUntilFinished));
    }
}
