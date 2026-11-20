namespace Content.Shared._KS14.Lobby;

/// <summary>
///     The lobby layouts <see cref="CCVar.KsCCVars.LobbyLayout"/> can name. See Klovn/Docs/lobby.md.
/// </summary>
public static class KsLobbyLayouts
{
    /// <summary>The translucent "tactical menu": menu column, character preview, chat. Shown most of the year.</summary>
    public const string Tactical = "tactical";

    /// <summary>The "Escape from Klovn" main menu, a gag.</summary>
    public const string Kharkiv = "kharkiv";

    /// <summary>
    ///     The default: <see cref="Kharkiv"/> on clown holidays (<see cref="IsKharkivSeason"/>), <see cref="Tactical"/>
    ///     the rest of the year. The other two ids force a layout whatever the date.
    /// </summary>
    public const string Auto = "auto";

    public static readonly IReadOnlyList<string> All = [Auto, Tactical, Kharkiv];

    public static bool IsKnown(string? id)
    {
        return id == Auto || id == Tactical || id == Kharkiv;
    }

    /// <summary>
    ///     Whether <paramref name="date"/> falls on a clown holiday, when <see cref="Auto"/> shows the Kharkiv layout:
    ///     <list type="bullet">
    ///         <item>April 1-3: April Fools', and two days to recover.</item>
    ///         <item>August 1-7: International Clown Week.</item>
    ///         <item>November 5: World Clown Day.</item>
    ///         <item>December 27 - January 1: the end of the year.</item>
    ///     </list>
    ///     Both ends of every range are included. Only the month and day are read.
    /// </summary>
    public static bool IsKharkivSeason(DateTime date)
    {
        return date.Month switch
        {
            4 => date.Day <= 3,
            8 => date.Day <= 7,
            11 => date.Day == 5,
            12 => date.Day >= 27,
            1 => date.Day == 1,
            _ => false,
        };
    }

    /// <summary>
    ///     The layout <paramref name="id"/> shows on <paramref name="date"/>: <see cref="Tactical"/> or
    ///     <see cref="Kharkiv"/>. <see cref="Auto"/>, and any id this doesn't know, follow the calendar.
    /// </summary>
    public static string Resolve(string? id, DateTime date)
    {
        return id switch
        {
            Tactical => Tactical,
            Kharkiv => Kharkiv,
            _ => IsKharkivSeason(date) ? Kharkiv : Tactical,
        };
    }
}
