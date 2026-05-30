using Robust.Shared.Player;

namespace Content.Shared._KS14.GameTicking;

/// <summary>
///     Raised on a player session when they join the game after being in the lobby,
///         but not if they disconnected in the lobby and such.
/// </summary>
public record struct KsPlayerLeftLobbyEvent(ICommonSession PlayerSession);
