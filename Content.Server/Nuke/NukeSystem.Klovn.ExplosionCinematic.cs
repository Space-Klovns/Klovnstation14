// KS14: added in this fork
using Content.Server.GameTicking.Rules.Components;
using Content.Shared._KS14.Nuke;
using Content.Shared.Audio;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server.Nuke;

public sealed partial class NukeSystem
{
    /// <summary>
    ///     Starts the station explosion cinematic on every in-game client and detonates the nuke
    ///     once the cinematic reaches the explosion.
    /// </summary>
    private void BeginCinematicDetonation(EntityUid uid, NukeComponent component, TransformComponent transform)
    {
        component.Exploded = true;
        _sound.StopStationEventMusic(uid, StationEventMusicType.Nuke);

        // If the alarm never played (e.g. an admin detonated it), clients pick it up where it would normally be at zero.
        var (alarmSound, alarmOffset) = component.PlayedAlertSoundInfo is { } info
            ? (info.Sound, _timing.CurTime - info.StartTime)
            : (_audio.ResolveSound(component.AlertSound), TimeSpan.FromSeconds(component.AlertSoundTime));

        var alarmRemaining = _audio.GetAudioLength(alarmSound) - alarmOffset;
        var explosionDelay = TimeSpan.FromTicks(Math.Max(
            (KsNukeExplosionCinematicTimings.IntroDuration + KsNukeExplosionCinematicTimings.MinimumIntactDuration).Ticks,
            (alarmRemaining + KsNukeExplosionCinematicTimings.PostAlarmPause).Ticks));

        var ending = IsNukeOpsShuttleOnStationMap(transform)
            ? KsNukeExplosionEnding.TotalA
            : KsNukeExplosionEnding.NukeWin;

        // Lobby players have no attached entity and don't get the cinematic.
        RaiseNetworkEvent(new KsNukeExplosionCinematicEvent(alarmSound, alarmOffset, explosionDelay, ending),
            Filter.Empty().AddWhereAttachedEntity(_ => true));

        Timer.Spawn(explosionDelay, () => CompleteDetonation(uid));
    }

    private bool IsNukeOpsShuttleOnStationMap(TransformComponent nukeTransform)
    {
        if (_station.GetStationInMap(nukeTransform.MapID) == null)
            return false;

        var query = EntityQueryEnumerator<NukeOpsShuttleComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var shuttleTransform))
        {
            if (shuttleTransform.MapID == nukeTransform.MapID)
                return true;
        }

        return false;
    }

    private void CompleteDetonation(EntityUid uid)
    {
        if (TerminatingOrDeleted(uid) ||
            !TryComp<NukeComponent>(uid, out var component) ||
            !TryComp(uid, out TransformComponent? transform))
        {
            return;
        }

        _explosions.QueueExplosion(uid,
            component.ExplosionType,
            component.TotalIntensity,
            component.IntensitySlope,
            component.MaxIntensity);

        RaiseLocalEvent(new NukeExplodedEvent
        {
            OwningStation = transform.GridUid,
            EndRound = component.EndRound,
        });

        if (component.EndRound)
        {
            Timer.Spawn(10000, () =>
            {
                // Only end the round if it is actually running!
                if (_roundEndSystem.ExpectedCountdownEnd == null)
                    _roundEndSystem.EndRound();
            });
        }

        Del(uid);
    }
}
