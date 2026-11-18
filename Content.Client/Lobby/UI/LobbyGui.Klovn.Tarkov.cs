using Content.Client._KS14.Lobby;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;

namespace Content.Client.Lobby.UI;

/// <summary>What the ready item says about the player: shown under the ESCAPE FROM KLOVN title.</summary>
public enum KsLobbyReadyStatus : byte
{
    NotReady,
    Ready,
    Join,
}

// Text and numbers on the main menu that LobbyState feeds.
public sealed partial class LobbyGui
{
    private static readonly Color KsReadyColor = Color.FromHex("#8FE05A");
    private static readonly Color KsNotReadyColor = Color.FromHex("#9A9A9A");
    private static readonly Color KsJoinColor = Color.FromHex("#F0A21E");

    private int _ksPlayerCount = -1;

    /// <summary>
    ///     Says under the title whether the player is ready, not ready, or can join the running round.
    /// </summary>
    public void KsSetReadyStatus(KsLobbyReadyStatus status)
    {
        var (key, color) = status switch
        {
            KsLobbyReadyStatus.Ready => ("ks-lobby-ready-ready", KsReadyColor),
            KsLobbyReadyStatus.Join => ("ks-lobby-ready-join", KsJoinColor),
            _ => ("ks-lobby-ready-not-ready", KsNotReadyColor),
        };

        ReadyStatus.Text = Loc.GetString(key);
        ReadyStatus.FontColorOverride = color;
    }

    /// <summary>
    ///     Shows how many players are connected in the banner. Called every frame, so it only touches the label when the count changed.
    /// </summary>
    public void KsSetPlayerCount(int count)
    {
        if (count == _ksPlayerCount)
            return;

        _ksPlayerCount = count;
        BannerPlayers.Text = count.ToString();
    }

    /// <summary>
    ///     Shows how long the running round has been going in the banner's TIMER cell.
    /// </summary>
    public void KsSetRoundElapsed(TimeSpan elapsed)
    {
        var hours = (int) elapsed.TotalHours;
        KsSetCountdown(null, $"{hours}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}");
    }

    /// <summary>
    ///     Fills the build line, bottom left.
    /// </summary>
    public void KsSetVersionText(string text)
    {
        KsVersion.Text = text;
    }
}
