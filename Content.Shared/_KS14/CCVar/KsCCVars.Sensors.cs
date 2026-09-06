using Content.Shared.Administration;
using Content.Shared.CCVar.CVarAccess;
using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    ///     Global sensor tick cadence, in seconds. Every sensor sweep, datalink
    ///         broadcast and console push runs on this beat, so lowering it costs server
    ///         time. Changing it also rescales the live-track window
    ///         (<see cref="SensorsLiveWindowTicks"/>), which is measured in ticks.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<float> SensorsUpdateInterval =
        CVarDef.Create("klovn.sensors.update_interval", 0.5f, CVar.SERVERONLY);

    /// <summary>
    ///     Seconds a stale detection source is remembered on a contact before it is
    ///         pruned. The freshest source is always kept for attribution, so this only
    ///         trims the older duplicate sources on a contact seen by several sensors.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<float> SensorsSourceRetention =
        CVarDef.Create("klovn.sensors.source_retention", 60f, CVar.SERVERONLY);

    /// <summary>
    ///     How many ticks a contact stays a solid live track after its last sighting
    ///         before it decays to a memory ghost. Widened per datalink hop (relayed
    ///         knowledge arrives a tick later each hop), so this is the base window for a
    ///         zero-hop, own-sensor track. Multiplied by <see cref="SensorsUpdateInterval"/>
    ///         to get the wall-clock window.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<int> SensorsLiveWindowTicks =
        CVarDef.Create("klovn.sensors.live_window_ticks", 2, CVar.SERVERONLY);

    /// <summary>
    ///     Bearing drift rate, in degrees per second, above which a bearing track's
    ///         stability readout flips from STABLE to DRIFTING. A crossing target at
    ///         100m moving 10 m/s tangentially drifts ~5.7 deg/s; a station (or a
    ///         constant-bearing intercept) drifts 0.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<float> SensorsDriftThreshold =
        CVarDef.Create("klovn.sensors.drift_threshold", 1.0f, CVar.SERVERONLY);

    /// <summary>
    ///     How many entries a grid's emission log keeps (oldest dropped first).
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<int> SensorsEmissionLogEntries =
        CVarDef.Create("klovn.sensors.emission_log_entries", 32, CVar.SERVERONLY);

    /// <summary>
    ///     Client accessibility switch: disables every nonessential instrument-UI
    ///         animation (shimmer, flicker, pings) at once. Essential state cues
    ///         (the JAMMED alarm pulse) remain.
    /// </summary>
    public static readonly CVarDef<bool> HudReducedMotion =
        CVarDef.Create("klovn.hud.reduced_motion", false, CVar.CLIENTONLY | CVar.ARCHIVE);
}
