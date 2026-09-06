using Content.Client._KS14.Sensors;
using Content.Shared._KS14.Sensors;

namespace Content.Client._KS14.Shuttles.UI;

/// <summary>
///     Which instrument screen the shell shows. Namespace-level rather than nested
///         in the window so the remembered tab can live in
///         <see cref="KsConsoleUiPrefs"/>.
/// </summary>
public enum KsInstrumentTab : byte
{
    Radar,
    Map,
    Esm,
    Dock,
}

/// <summary>
///     One console's remembered instrument-face settings. Everything here is a
///         physical switch position on the face: closing the UI and coming back
///         must find each switch where the crew left it, instead of the crew
///         re-dialling the whole face every open. Display state only; the real
///         emitters (radar, jammer) already keep their state server-side on the
///         console.
/// </summary>
public sealed class KsConsoleUiPrefs
{
    public KsInstrumentTab Tab = KsInstrumentTab.Radar;

    public bool ShowDocks = true;

    /// <summary>Only the types the crew actually cycled; absent types keep the scope's default.</summary>
    public readonly Dictionary<KsSensorType, KsCoverageDisplayMode> CoverageModes = new();

    public KsContactDetail ReadoutDetail = KsContactDetail.Full;

    /// <summary>Nav scope zoom in metres; null until the crew first touches the wheel.</summary>
    public float? RadarRange;

    public bool MapShowBeacons = true;
    public bool MapShowSensors;
}

/// <summary>
///     Holds each console's <see cref="KsConsoleUiPrefs"/> across UI opens. An
///         entity system rather than a static so the store dies with the
///         connection: a NetEntity recycled by the next round must not inherit a
///         stale face. Keyed by NetEntity because PVS churn hands the same console
///         a fresh client EntityUid every time it re-enters view.
/// </summary>
public sealed class KsConsolePrefsSystem : EntitySystem
{
    private readonly Dictionary<NetEntity, KsConsoleUiPrefs> _prefs = new();

    public KsConsoleUiPrefs For(EntityUid console)
    {
        var key = GetNetEntity(console);
        if (!_prefs.TryGetValue(key, out var prefs))
            _prefs[key] = prefs = new KsConsoleUiPrefs();
        return prefs;
    }
}
