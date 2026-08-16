using System.Numerics;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Sensors;

/// <summary>
///     The sensor picture a console's nav state carries: contacts, coverage fans
///         and emitter/jammer status. Bundled into one fork-owned object so the
///         upstream <c>NavInterfaceState</c> stays a single-field diff.
///     A null instance means "no sensor picture" and reads back exactly like the
///         old all-null/false defaults.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsSensorNavState
{
    /// <summary>
    ///     Everything the console's grid knows via the sensor framework.
    ///         Null means the grid has no contact pool (no sensor coverage);
    ///         radar-type UIs render full fog of war off this list.
    /// </summary>
    public List<KsSensorContactState>? Contacts;

    /// <summary>
    ///     The coverage fans of the console grid's own sensors, each cut short by
    ///         walls and other grids. Framing is per region (grid-local apex,
    ///         world-oriented fan boundaries; see
    ///         <see cref="KsSensorRegionState.WorldOffsets"/>). Null when nothing is
    ///         watching (scrubbed with <see cref="Contacts"/>).
    /// </summary>
    public List<KsSensorRegionState>? Regions;

    /// <summary>
    ///     Whether any radar on the console's grid is currently jammed. Drives the
    ///         "JAMMED" indicator: your radar is dark until you kill the jammer or push
    ///         inside its burn-through range.
    /// </summary>
    public bool Jammed;

    /// <summary>Whether the console's grid mounts any radar at all: gates the radar on/off toggle's visibility.</summary>
    public bool HasRadar;

    /// <summary>Whether any radar on the console's grid is switched on (emitting): drives the toggle's ON/OFF label.</summary>
    public bool RadarActive;

    /// <summary>Whether the console's grid mounts any jammer at all: gates the jammer on/off toggle's visibility.</summary>
    public bool HasJammer;

    /// <summary>Whether any jammer on the console's grid is switched on: drives the jammer toggle's ON/OFF label.</summary>
    public bool JammerActive;

    /// <summary>
    ///     Gates the ESM tab's precision side (roster memory, selection, signal
    ///         analysis, FOCUS), which degrades to a "NO ELINT ARRAY" chip without
    ///         an array.
    /// </summary>
    public bool HasElint;

    /// <summary>
    ///     Whether the grid's ELINT is deaf right now because an own active emitter
    ///         (radar or jammer) is transmitting. Emission truth, not toggle intent:
    ///         an unpowered radar left switched on shows <see cref="RadarActive"/>
    ///         without deafening anything. Only meaningful alongside
    ///         <see cref="HasElint"/>; drives the ESM tab's deaf chip.
    /// </summary>
    public bool ElintDeaf;

    /// <summary>
    ///     Gates the ESM tab's warning side (threat channels, posture, the tab-alert
    ///         flash), which degrades to a "NO RWR RECEIVER" chip without a receiver.
    /// </summary>
    public bool HasRwr;

    /// <summary>
    ///     The grid's emission log, oldest first. Derived entirely from the grid's own
    ///         pool transitions, never from the global emission registry, which would
    ///         leak emitters the grid has not actually heard. Null when the log is empty.
    /// </summary>
    public List<KsEmissionLogEntry>? EmissionLog;

    /// <summary>
    ///     Life-sign blips from the grid's own IRSTs, strictly sweep-fresh: a track
    ///         going ghost drops its crew dots the same push. Null when none.
    /// </summary>
    public List<KsLifeSignState>? LifeSigns;
}

/// <summary>
///     One life-sign blip. <see cref="Grid"/> set: a centre-of-mass-relative offset
///         in that contact's local frame (rides the dead-reckoned hull); null: a
///         world position. Never carries identity.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct KsLifeSignState(NetEntity? Grid, Vector2 Position);

[Serializable, NetSerializable]
public enum KsEmissionLogKind : byte
{
    /// <summary>An emitter-class source started hearing this contact.</summary>
    EmitterNew = 0,

    /// <summary>The last live emitter-class track of this contact went stale.</summary>
    EmitterSilent = 1,

    /// <summary>A radar on the own grid became jammed.</summary>
    JamStart = 2,

    /// <summary>The own grid's radars recovered from jamming.</summary>
    JamEnd = 3,
}

/// <summary>
///     One emission-log line: what happened, when (server time, so the client can
///         age it like contact timestamps), and the designation/name the affected
///         contact was known by (null for own-grid jam events).
/// </summary>
[Serializable, NetSerializable]
public readonly record struct KsEmissionLogEntry(
    TimeSpan Time,
    KsEmissionLogKind Kind,
    string? Designation,
    string? Name);
