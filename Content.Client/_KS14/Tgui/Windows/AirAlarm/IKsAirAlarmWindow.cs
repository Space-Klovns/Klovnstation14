using Content.Shared.Atmos;
using Content.Shared.Atmos.Monitor;
using Content.Shared.Atmos.Monitor.Components;

namespace Content.Client._KS14.Tgui.Windows.AirAlarm;

/// <summary>
///     What the air alarm's bound user interface needs of its window, so that the stock window and the tgui one are
///         interchangeable.
/// </summary>
public interface IKsAirAlarmWindow : IDisposable
{
    event Action<string, IAtmosDeviceData>? AtmosDeviceDataChanged;
    event Action<IAtmosDeviceData>? AtmosDeviceDataCopied;
    event Action<string, AtmosMonitorThresholdType, AtmosAlarmThreshold, Gas?>? AtmosAlarmThresholdChanged;
    event Action<AirAlarmMode>? AirAlarmModeChanged;
    event Action<bool>? AutoModeChanged;
    event Action? ResyncAllRequested;

    void SetEntity(EntityUid uid);

    /// <summary>
    ///     Whether the local player may change settings. The server decides regardless.
    /// </summary>
    void SetAccessEnabled(bool hasAccess);

    void UpdateState(AirAlarmUIState state);
}
