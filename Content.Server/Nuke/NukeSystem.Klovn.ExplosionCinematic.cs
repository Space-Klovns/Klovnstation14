using System.Text.Json;
using Content.Server.GameTicking.Rules.Components;
using Content.Shared._KS14.Nuke;
using Content.Shared.Audio;
using Robust.Shared.ContentPack;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.Nuke;

public sealed partial class NukeSystem
{
    [Dependency] private IResourceManager _resource = default!;

    private readonly Dictionary<string, TimeSpan> _cinematicStateLengths = new();

    /// <summary>
    ///     Detonates the nuke like <see cref="ActivateBomb"/>, optionally forcing which cinematic ending plays.
    /// </summary>
    public void KsActivateBomb(Entity<NukeComponent?, TransformComponent?> nuke, KsNukeExplosionEnding? ending = null)
    {
        if (!Resolve(nuke, ref nuke.Comp1, ref nuke.Comp2))
            return;

        if (nuke.Comp1.Exploded)
            return;

        BeginCinematicDetonation((nuke, nuke.Comp1, nuke.Comp2), ending);
    }

    /// <summary>
    ///     Starts the station explosion cinematic on every in-game client and detonates the nuke
    ///     once the cinematic reaches the explosion.
    /// </summary>
    /// <param name="ending">The ending to show, or null to pick one from the nuke's situation.</param>
    private void BeginCinematicDetonation(Entity<NukeComponent, TransformComponent> nuke, KsNukeExplosionEnding? ending = null)
    {
        var (uid, component, transform) = nuke;
        component.Exploded = true;
        _sound.StopStationEventMusic(uid, StationEventMusicType.Nuke);

        // If the alarm never played (e.g. an admin detonated it), clients pick it up where it would normally be at zero.
        var (alarmSound, alarmOffset) = component.PlayedAlertSoundInfo is { } info
            ? (info.Sound, _timing.CurTime - info.StartTime)
            : (_audio.ResolveSound(component.AlertSound), TimeSpan.FromSeconds(component.AlertSoundTime));

        var resolvedEnding = ending ?? (IsNukeOpsShuttleOnStationMap(transform)
            ? KsNukeExplosionEnding.TotalA
            : KsNukeExplosionEnding.NukeWin);

        var introLength = GetCinematicStateLength(KsNukeExplosionCinematicStates.Get(resolvedEnding).Intro);
        var alarmRemaining = _audio.GetAudioLength(alarmSound) - alarmOffset;
        var explosionDelay = TimeSpan.FromTicks(Math.Max(
            (introLength + KsNukeExplosionCinematicTimings.MinimumIntactDuration).Ticks,
            (alarmRemaining + KsNukeExplosionCinematicTimings.PostAlarmPause).Ticks));

        // Lobby players have no attached entity and don't get the cinematic.
        RaiseNetworkEvent(new KsNukeExplosionCinematicEvent(alarmSound, alarmOffset, explosionDelay, resolvedEnding),
            Filter.Empty().AddWhereAttachedEntity(_ => true));

        Timer.Spawn(explosionDelay, () => CompleteDetonation(uid));
    }

    /// <summary>
    ///     Total length of a cinematic RSI state's animation, read from its frame delays
    ///     so the server times the explosion exactly like the client plays it.
    /// </summary>
    private TimeSpan GetCinematicStateLength(string state)
    {
        if (_cinematicStateLengths.TryGetValue(state, out var cached))
            return cached;

        var length = TimeSpan.Zero;
        var metaPath = new ResPath(KsNukeExplosionCinematicStates.RsiPath) / "meta.json";
        if (_resource.TryContentFileRead(metaPath, out var stream))
        {
            using (stream)
            using (var meta = JsonDocument.Parse(stream))
            {
                foreach (var stateMeta in meta.RootElement.GetProperty("states").EnumerateArray())
                {
                    if (stateMeta.GetProperty("name").GetString() != state ||
                        !stateMeta.TryGetProperty("delays", out var delays))
                    {
                        continue;
                    }

                    // Every direction has the same timing, so the first one is enough.
                    foreach (var delay in delays[0].EnumerateArray())
                    {
                        length += TimeSpan.FromSeconds(delay.GetSingle());
                    }

                    break;
                }
            }
        }
        else
        {
            Log.Error($"Couldn't read {metaPath}, the nuke explosion cinematic will be out of sync.");
        }

        _cinematicStateLengths[state] = length;
        return length;
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

        _explosions.QueueExplosion(nuke,
            nuke.Comp1.ExplosionType,
            nuke.Comp1.TotalIntensity,
            nuke.Comp1.IntensitySlope,
            nuke.Comp1.MaxIntensity);

        RaiseLocalEvent(new NukeExplodedEvent
        {
            OwningStation = nuke.Comp2.GridUid,
            EndRound = nuke.Comp1.EndRound,
        });

        if (nuke.Comp1.EndRound)
        {
            Timer.Spawn(10000, () =>
            {
                // Only end the round if it is actually running!
                if (_roundEndSystem.ExpectedCountdownEnd == null)
                    _roundEndSystem.EndRound();
            });
        }

        Del(nuke);
    }
}
