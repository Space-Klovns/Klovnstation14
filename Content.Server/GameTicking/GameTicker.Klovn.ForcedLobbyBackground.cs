using System.Linq;
using Content.Shared.GameTicking.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server.GameTicking;

// Maps can force a lobby background on everyone (GameMapPrototype.ForcedLobbyBackground). It is still picked here,
//      once, and sent to every client in the lobby status, so all players always see the same one.
public sealed partial class GameTicker
{
    /// <summary>
    ///     Whether <see cref="LobbyBackground"/> came from the selected map rather than a random roll.
    /// </summary>
    [ViewVariables]
    private bool _ksLobbyBackgroundForced;

    private void KsInitializeForcedLobbyBackground()
    {
        _gameMapManager.KsSelectedMapChanged += KsOnSelectedMapChanged;
        ProtoMan.PrototypesReloaded += KsOnPrototypesReloaded;
    }

    // The map manager and prototype manager outlive this system, so the handlers are taken back off.
    private void KsShutdownForcedLobbyBackground()
    {
        _gameMapManager.KsSelectedMapChanged -= KsOnSelectedMapChanged;
        ProtoMan.PrototypesReloaded -= KsOnPrototypesReloaded;
    }

    // Upstream builds the roll pool once at startup, so added, removed or no-longer-rollable backgrounds would be
    //      missed until a restart. A map's forced background is read from the map each time and needs nothing here.
    private void KsOnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (!args.WasModified<LobbyBackgroundPrototype>())
            return;

        _lobbyBackgrounds?.Clear();
        InitializeLobbyBackground(); // rebuilds the pool, then rolls (or applies the map's) again
        SendStatusToAll();
    }

    private bool KsTryGetForcedLobbyBackground(out ProtoId<LobbyBackgroundPrototype> background)
    {
        background = default;
        if (_gameMapManager.GetSelectedMap() is not { ForcedLobbyBackground: { } forcedBackground } map)
            return false;

        if (!ProtoMan.TryIndex(forcedBackground, out var backgroundPrototype) ||
            !WhitelistedBackgroundExtensions.Contains(backgroundPrototype.Background.Extension))
        {
            _sawmill.Error($"Map {map.ID} forces lobby background {forcedBackground}, which is missing or not a supported image.");
            return false;
        }

        background = forcedBackground;
        return true;
    }

    /// <summary>
    ///     Sets <see cref="LobbyBackground"/> to the selected map's forced background, if it has one.
    /// </summary>
    /// <returns>Whether a background was forced, in which case nothing should be rolled.</returns>
    private bool KsTryApplyForcedLobbyBackground()
    {
        _ksLobbyBackgroundForced = KsTryGetForcedLobbyBackground(out var forcedBackground);
        if (_ksLobbyBackgroundForced)
            LobbyBackground = forcedBackground;

        return _ksLobbyBackgroundForced;
    }

    private void KsOnSelectedMapChanged()
    {
        var forced = KsTryGetForcedLobbyBackground(out var forcedBackground);
        if (forced && LobbyBackground == forcedBackground)
        {
            // It may have been rolled before the map was picked; it's the map's now, so leaving the map rerolls it.
            _ksLobbyBackgroundForced = true;
            return;
        }

        // A rolled background stays until a map forces one.
        if (!forced && !_ksLobbyBackgroundForced)
            return;

        // Applies the new map's background, or rerolls once the selection leaves a forcing map.
        RandomizeLobbyBackground();
        SendStatusToAll();
    }
}
