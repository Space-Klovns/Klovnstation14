using Content.Server._KS14.Cinematics;
using Content.Server.Damage.Systems;
using Content.Server.GameTicking.Rules.Components;
using Content.Shared._KS14.Cinematics;
using Content.Shared.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Timing;

namespace Content.Server.Nuke;

public sealed partial class NukeSystem
{
    /// <summary>
    ///     Silence between the alarm finishing and the station exploding.
    /// </summary>
    private static readonly TimeSpan KsCinematicPostAlarmPause = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     Plays the rest of the alarm to anyone who never heard it start.
    /// </summary>
    private static readonly AudioParams KsContinuedAlarmParams = AudioParams.Default.WithVolume(-2f);

    [Dependency] private KsCinematicSystem _cinematicSystem = default!;
    [Dependency] private GodmodeSystem _godmodeSystem = default!;

    /// <summary>
    ///     Starts the nuke's explosion cinematic for everyone in game, and detonates the nuke once the cinematic
    ///         reaches the explosion, which is after the alarm has finished.
    /// </summary>
    private void BeginCinematicDetonation(Entity<NukeComponent, TransformComponent> nuke)
    {
        var (uid, component, transform) = nuke;
        component.Exploded = true;
        _sound.StopStationEventMusic(uid, StationEventMusicType.Nuke);

        // Everyone is already watching the station blow up, so nothing may destroy the nuke before it does.
        //      Whatever deletes it regardless is caught by OnShutdown.
        _godmodeSystem.EnableGodmode(uid);

        // If the alarm never played (e.g. an admin detonated it), clients pick it up where it would normally be at zero.
        var (alarmSound, alarmOffset) = component.PlayedAlertSoundInfo is { } info
            ? (info.Sound, _timing.CurTime - info.StartTime)
            : (_audio.ResolveSound(component.AlertSound), TimeSpan.FromSeconds(component.AlertSoundTime));

        var cinematic = IsNukeOpsShuttleOnStationMap(transform)
            ? component.KsShuttleDestroyedCinematic
            : component.KsCinematic;

        var alarmRemaining = _audio.GetAudioLength(alarmSound) - alarmOffset;
        var explosionDelay = _cinematicSystem.Play(cinematic,
            stretchUntil: alarmRemaining + KsCinematicPostAlarmPause,
            continuedSound: new KsCinematicContinuedSound(alarmSound, alarmOffset, KsContinuedAlarmParams));

        Timer.Spawn(explosionDelay ?? TimeSpan.Zero, () => CompleteDetonation(uid));
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

    private void CompleteDetonation(Entity<NukeComponent?, TransformComponent?> nuke)
    {
        if (TerminatingOrDeleted(nuke) || !Resolve(nuke, ref nuke.Comp1, ref nuke.Comp2, false))
            return;

        Detonate((nuke, nuke.Comp1, nuke.Comp2));
        Del(nuke);
    }

    /// <summary>
    ///     A nuke deleted during its cinematic, which Godmode can't prevent (an admin, a singularity), explodes where
    ///         it was rather than leaving everyone with a cinematic of an explosion that never happened.
    /// </summary>
    /// <remarks>
    ///     On terminating rather than on <see cref="ComponentShutdown"/>, because deletion detaches the entity to
    ///         nullspace before shutting its components down, and with it goes where it was.
    /// </remarks>
    [SubscribeLocalEvent]
    private void OnTerminating(Entity<NukeComponent> entity, ref EntityTerminatingEvent args)
    {
        DetonateIfLeftMidCinematic(entity);
    }

    /// <summary>
    ///     Removing just the nuke's component is leaving the world as a nuke, too. On deletion
    ///         <see cref="OnTerminating"/> has already detonated it.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnShutdown(Entity<NukeComponent> entity, ref ComponentShutdown args)
    {
        DetonateIfLeftMidCinematic(entity);
    }

    private void DetonateIfLeftMidCinematic(Entity<NukeComponent> entity)
    {
        if (!entity.Comp.Exploded || entity.Comp.KsDetonated)
            return;

        var transformComponent = Transform(entity);

        // The whole map going (a round restart) takes the station with it; there is nothing left to blow up.
        if (transformComponent.MapUid is not { } mapUid || TerminatingOrDeleted(mapUid))
            return;

        Detonate((entity.Owner, entity.Comp, transformComponent));
    }

    private void Detonate(Entity<NukeComponent, TransformComponent> nuke)
    {
        var (uid, component, transformComponent) = nuke;
        component.KsDetonated = true;

        // Map coordinates rather than the entity, which may already be partway through deletion here.
        _explosions.QueueExplosion(_transform.GetMapCoordinates(transformComponent),
            component.ExplosionType,
            component.TotalIntensity,
            component.IntensitySlope,
            component.MaxIntensity,
            uid);

        RaiseLocalEvent(new NukeExplodedEvent
        {
            OwningStation = transformComponent.GridUid,
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
    }
}
