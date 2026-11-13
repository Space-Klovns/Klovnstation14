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
    ///     The first beep comes as soon as the wearer goes down.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnStateChanged(Entity<KsDistressBeaconComponent> entity, ref KsDistressBeaconStateChangedEvent args)
    {
        entity.Comp.NextBeep = _gameTiming.CurTime;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _gameTiming.CurTime;
        var enumerator = EntityQueryEnumerator<KsDistressBeaconComponent>();
        while (enumerator.MoveNext(out var uid, out var beaconComponent))
        {
            var settings = beaconComponent.SettingsFor(beaconComponent.State);
            if (settings == null || curTime < beaconComponent.NextBeep)
                continue;

            _audioSystem.PlayPvs(beaconComponent.BeepSound, uid);
            beaconComponent.NextBeep = curTime + settings.BeepInterval;
        }
    }
}
