using Content.Client._KS14.Lobby;
using Robust.Client.UserInterface;

namespace Content.Client.Lobby.UI;

// The lobby countdown is two labels: a small caption (StartTime) over a big value (StartTimeValue).
public sealed partial class LobbyGui
{
    /// <summary>
    ///     How many seconds before the start the countdown starts heating up.
    /// </summary>
    private const double KsUrgentSeconds = 20.0;

    private static readonly Color KsUrgentColor = Color.FromHex("#FF5A3C");
    private static readonly Color KsFlashColor = Color.FromHex("#FFE9C4");

    /// <summary>
    ///     Fills the banner's TIMER cell. <paramref name="caption"/> is unused by the main menu, which has a fixed TIMER
    ///         caption; a null <paramref name="value"/> shows the idle dashes.
    ///     Pass <paramref name="secondsLeft"/> while a live countdown runs: in the last <see cref="KsUrgentSeconds"/> the
    ///         value shifts from the accent toward red and flashes on every second. Called every frame, so it animates.
    /// </summary>
    public void KsSetCountdown(string? caption, string? value, double? secondsLeft = null)
    {
        StartTime.Text = caption ?? string.Empty;
        StartTimeValue.Text = string.IsNullOrEmpty(value) ? Loc.GetString("ks-tarkov-banner-timer-idle") : value;
        StartTimeValue.FontColorOverride = KsGetCountdownColor(secondsLeft);
    }

    /// <summary>
    ///     Null (the stylesheet's amber) outside the last <see cref="KsUrgentSeconds"/>.
    /// </summary>
    private static Color? KsGetCountdownColor(double? secondsLeft)
    {
        if (secondsLeft is not { } seconds || seconds < 0.0 || seconds > KsUrgentSeconds)
            return null;

        // 0 at the threshold, 1 at zero: the base colour heats from amber to red-orange.
        var urgency = (float)(1.0 - seconds / KsUrgentSeconds);
        var color = Color.InterpolateBetween(KsLobbySheetlet.AccentColor, KsUrgentColor, urgency);

        // The fractional second runs 1 -> 0 between digit changes, so cubing it gives a flash the moment the digit
        //      ticks over that fades before the next one. It grows stronger as the start nears.
        var flash = (float)Math.Pow(seconds - Math.Floor(seconds), 3.0);
        return Color.InterpolateBetween(color, KsFlashColor, flash * urgency * 0.75f);
    }
}
