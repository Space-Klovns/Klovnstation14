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

public static class KsNukeExplosionCinematicTimings
{
    /// <summary>
    ///     Length of the 3-2-1 timer intro.
    /// </summary>
    public static readonly TimeSpan IntroDuration = TimeSpan.FromSeconds(3.5);

    /// <summary>
    ///     The intact station is always shown for at least this long.
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
