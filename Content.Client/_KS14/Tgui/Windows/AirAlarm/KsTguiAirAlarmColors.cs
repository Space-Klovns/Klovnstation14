using Content.Shared.Atmos.Monitor;

namespace Content.Client._KS14.Tgui.Windows.AirAlarm;

internal static class KsTguiAirAlarmColors
{
    public static Color ForAlarm(AtmosAlarmType alarm)
    {
        return alarm switch
        {
            AtmosAlarmType.Danger or AtmosAlarmType.Emagged => KsTguiPalettes.Bad,
            AtmosAlarmType.Warning => KsTguiPalettes.Average,
            _ => KsTguiPalettes.Good,
        };
    }

    /// <summary>
    ///     The more severe of two states, with Normal under Warning under Danger.
    /// </summary>
    public static AtmosAlarmType Worse(AtmosAlarmType first, AtmosAlarmType second)
    {
        return Severity(second) > Severity(first) ? second : first;
    }

    private static int Severity(AtmosAlarmType alarm)
    {
        return alarm switch
        {
            AtmosAlarmType.Emagged => 3,
            AtmosAlarmType.Danger => 3,
            AtmosAlarmType.Warning => 2,
            AtmosAlarmType.Normal => 1,
            _ => 0,
        };
    }

    public static string StateKey(AtmosAlarmType alarm)
    {
        return alarm switch
        {
            AtmosAlarmType.Danger or AtmosAlarmType.Emagged => "ks-tgui-airalarm-status-danger",
            AtmosAlarmType.Warning => "ks-tgui-airalarm-status-caution",
            _ => "ks-tgui-airalarm-status-optimal",
        };
    }
}
