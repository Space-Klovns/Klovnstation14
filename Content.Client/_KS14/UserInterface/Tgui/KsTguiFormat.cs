using System.Globalization;

namespace Content.Client._KS14.UserInterface.Tgui;

/// <summary>
///     Number formats the tgui windows share.
/// </summary>
public static class KsTguiFormat
{
    /// <summary>
    ///     tgui's <c>formatPressure</c>: whole kilopascals below 10000 kPa, megapascals to one decimal above.
    /// </summary>
    public static string Pressure(float kilopascals)
    {
        if (kilopascals < 10000f)
            return Loc.GetString("ks-tgui-pressure", ("pressure", MathF.Round(kilopascals).ToString("F0", CultureInfo.CurrentCulture)));

        return Loc.GetString("ks-tgui-pressure-mega", ("pressure", (kilopascals / 1000f).ToString("F1", CultureInfo.CurrentCulture)));
    }
}
