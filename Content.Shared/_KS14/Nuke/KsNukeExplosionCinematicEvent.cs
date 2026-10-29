using Robust.Shared.Audio;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Nuke;

[Serializable, NetSerializable]
public enum KsNukeExplosionEnding : byte
{
    /// <summary>
    ///     The station was destroyed.
    /// </summary>
    NukeWin,

    /// <summary>
    ///     The station was destroyed and the nuclear operatives' shuttle went down with it.
    /// </summary>
    TotalA,

    /// <summary>
    ///     The nuke went off but missed the station.
    /// </summary>
    NukeFail,

    /// <summary>
    ///     The station blew itself up.
    /// </summary>
    SelfDestruct,

    /// <summary>
    ///     A rogue AI blew up the station.
    /// </summary>
    Malf,

    /// <summary>
    ///     The station was consumed by a blood cult.
    /// </summary>
    Cult,
}

/// <summary>
///     Sent to every in-game client when a nuke's timer reaches zero.
///     The client plays the whole cinematic locally, timed from when it receives this event.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsNukeExplosionCinematicEvent : EntityEventArgs
{
    /// <summary>
    ///     The nuke alarm that should keep playing through the intro.
    /// </summary>
    public ResolvedSoundSpecifier AlarmSound;

    /// <summary>
    ///     How far into <see cref="AlarmSound"/> playback is when the cinematic starts.
    /// </summary>
    public TimeSpan AlarmOffset;

    /// <summary>
    ///     Time from the start of the cinematic until the station explodes.
    /// </summary>
    public TimeSpan ExplosionDelay;

    public KsNukeExplosionEnding Ending;

    public KsNukeExplosionCinematicEvent(
        ResolvedSoundSpecifier alarmSound,
        TimeSpan alarmOffset,
        TimeSpan explosionDelay,
        KsNukeExplosionEnding ending)
    {
        AlarmSound = alarmSound;
        AlarmOffset = alarmOffset;
        ExplosionDelay = explosionDelay;
        Ending = ending;
    }
}

/// <summary>
///     The RSI states shown for an ending: the intro, what's held until the climax,
///     the climax itself and the closing summary.
///     The intro and climax play at the frame timings in the RSI.
/// </summary>
public readonly record struct KsNukeExplosionCinematicStates(string Intro, string Hold, string Climax, string Summary)
{
    public const string RsiPath = "/Textures/_KS14/Cinematic/station_explosion.rsi";

    public static KsNukeExplosionCinematicStates Get(KsNukeExplosionEnding ending)
    {
        return ending switch
        {
            KsNukeExplosionEnding.TotalA => new("intro_nuke", "station_intact", "station_explode_fade_red", "summary_totala"),
            KsNukeExplosionEnding.NukeFail => new("intro_nuke", "station_intact", "station_intact_fade_red", "summary_nukefail"),
            KsNukeExplosionEnding.SelfDestruct => new("intro_nuke", "station_intact", "station_explode_fade_red", "summary_selfdes"),
            KsNukeExplosionEnding.Malf => new("intro_malf", "intro_malf_still", "station_explode_fade_red", "summary_malf"),
            KsNukeExplosionEnding.Cult => new("intro_cult", "station_intact", "station_corrupted", "summary_cult"),
            _ => new("intro_nuke", "station_intact", "station_explode_fade_red", "summary_nukewin"),
        };
    }
}

public static class KsNukeExplosionCinematicTimings
{
    /// <summary>
    ///     The hold state between the intro and the climax is always shown for at least this long.
    /// </summary>
    public static readonly TimeSpan MinimumIntactDuration = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     Silence between the alarm finishing and the station exploding.
    /// </summary>
    public static readonly TimeSpan PostAlarmPause = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     How long the ending summary stays up before the cinematic fades out.
    /// </summary>
    public static readonly TimeSpan SummaryDuration = TimeSpan.FromSeconds(6);
}
