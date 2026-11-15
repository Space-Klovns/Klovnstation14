using Content.Shared._KS14.DistressBeacon;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._KS14.DistressBeacon;

/// <summary>
///     Beeps for every beacon whose wearer is down, from the beacon, for everyone in range to hear.
/// </summary>
public sealed partial class KsDistressBeaconBeepSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;

    /// <summary>
    ///     The first beep comes as soon as the wearer goes down; a beacon that goes off stops being looked at.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnStateChanged(Entity<KsDistressBeaconComponent> entity, ref KsDistressBeaconStateChangedEvent args)
    {
        if (entity.Comp.SettingsFor(args.State) == null)
        {
            RemCompDeferred<KsDistressBeaconBeepingComponent>(entity);
            return;
        }

        EnsureComp<KsDistressBeaconBeepingComponent>(entity).NextBeep = _gameTiming.CurTime;
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<KsDistressBeaconComponent> entity, ref ComponentShutdown args)
    {
        RemCompDeferred<KsDistressBeaconBeepingComponent>(entity);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _gameTiming.CurTime;
        var enumerator = EntityQueryEnumerator<KsDistressBeaconBeepingComponent, KsDistressBeaconComponent>();
        while (enumerator.MoveNext(out var uid, out var beepingComponent, out var beaconComponent))
        {
            if (curTime < beepingComponent.NextBeep ||
                beepingComponent.LifeStage > ComponentLifeStage.Running)
                continue;

            var settings = beaconComponent.SettingsFor(beaconComponent.State);
            if (settings == null)
                continue;

            _audioSystem.PlayPvs(beaconComponent.BeepSound, uid);
            beepingComponent.NextBeep = curTime + settings.BeepInterval;
        }
    }
}
