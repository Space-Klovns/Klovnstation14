using Content.Shared._KS14.SupplyPod;
using Robust.Server.Audio;
using Robust.Shared.Timing;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Server._KS14.SupplyPod;

/// <summary>
///     Kept you waiting, huh?
/// </summary>
public sealed class SupplyPodSystem : SharedSupplyPodSystem
{
    [Dependency] private readonly AudioSystem _audioSystem = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SupplyPodComponent, MapInitEvent>(OnMapInit);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _gameTiming.CurTime;
        var eqe = EntityQueryEnumerator<ActiveSupplyPodComponent, SupplyPodComponent>();

        while (eqe.MoveNext(out var uid, out var activeSupplyPodComponent, out var supplyPodComponent))
        {
            // Pre-impact
            if (curTime >= activeSupplyPodComponent.FallSoundTime)
            {
                _audioSystem.PlayPvs(
                    supplyPodComponent.FallSound,
                    activeSupplyPodComponent.DestinationCoordinates
                );

                activeSupplyPodComponent.FallSoundTime = TimeSpan.MaxValue;
                Dirty(uid, activeSupplyPodComponent);
            }

            if (curTime < activeSupplyPodComponent.LaunchFinishTime)
                continue;

            // Impact

            activeSupplyPodComponent.LaunchFinishTime = TimeSpan.MaxValue;
            _audioSystem.PlayPvs(
                supplyPodComponent.ImpactSound,
                activeSupplyPodComponent.DestinationCoordinates
            );

            RemComp(uid, activeSupplyPodComponent);
        }
    }

    private void OnMapInit(Entity<SupplyPodComponent> entity, ref MapInitEvent args)
    {
        var curTime = _gameTiming.CurTime;

        var activeComponent = EnsureComp<ActiveSupplyPodComponent>(entity.Owner);
        activeComponent.LaunchFinishTime = curTime + entity.Comp.FallDuration;
        activeComponent.FallSoundTime = curTime + entity.Comp.FallSoundDelay;
        activeComponent.DestinationCoordinates = Transform(entity).Coordinates;
        Dirty(entity.Owner, activeComponent);
    }
}
