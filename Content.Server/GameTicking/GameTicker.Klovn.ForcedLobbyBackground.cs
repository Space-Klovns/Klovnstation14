using System.Linq;
using Content.Shared.GameTicking.Prototypes;
using Content.Shared.Maps;
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

    /// <summary>
    ///     The last broken map/background pair logged, so one misconfiguration is reported once rather than on every
    ///         lookup (each roll and each selection change looks it up).
    /// </summary>
    private (string MapId, string BackgroundId)? _ksLoggedInvalidForcedBackground;

    private void KsInitializeForcedLobbyBackground()
    {
        _gameMapManager.KsSelectedMapChanged += KsRefreshForcedLobbyBackground;
        ProtoMan.PrototypesReloaded += KsOnPrototypesReloaded;
    }

    // The map manager and prototype manager outlive this system, so the handlers are taken back off.
    private void KsShutdownForcedLobbyBackground()
    {
        _gameMapManager.KsSelectedMapChanged -= KsRefreshForcedLobbyBackground;
        ProtoMan.PrototypesReloaded -= KsOnPrototypesReloaded;
    }

    private void KsOnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        // Upstream builds the roll pool once at startup, so added, removed or no-longer-rollable backgrounds would be
        //      missed until a restart.
        if (args.WasModified<LobbyBackgroundPrototype>())
        {
            _lobbyBackgrounds?.Clear();
            InitializeLobbyBackground(); // rebuilds the pool, then rolls (or applies the map's) again
            SendStatusToAll();
            return;
        }

        // The selected map's forcedLobbyBackground may have been added, changed or removed.
        if (args.WasModified<GameMapPrototype>())
            KsRefreshForcedLobbyBackground();
    }

    private bool KsTryGetForcedLobbyBackground(out ProtoId<LobbyBackgroundPrototype> background)
    {
        background = default;
        if (_gameMapManager.GetSelectedMap() is not { } selectedMap)
            return false;

        // The map manager keeps the instance it selected, which a prototype reload replaces; read the current one.
        //      A persistence map is a copy under the same id, so it reads the same field.
        if (ProtoMan.TryIndex<GameMapPrototype>(selectedMap.ID, out var currentMap))
            selectedMap = currentMap;

        if (selectedMap.ForcedLobbyBackground is not { } forcedBackground)
            return false;

        if (!ProtoMan.TryIndex(forcedBackground, out var backgroundPrototype) ||
            !WhitelistedBackgroundExtensions.Contains(backgroundPrototype.Background.Extension))
        {
            var invalidPair = (selectedMap.ID, forcedBackground.Id);
            if (_ksLoggedInvalidForcedBackground != invalidPair)
            {
                _ksLoggedInvalidForcedBackground = invalidPair;
                _sawmill.Error($"Map {selectedMap.ID} forces lobby background {forcedBackground}, which is missing or not a supported image.");
            }

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

    /// <summary>
    ///     Brings <see cref="LobbyBackground"/> in line with the selected map: its forced background if it has one, a
    ///         reroll if the selection has just left a forcing map, and otherwise the rolled background stays. Clients
    ///         are only sent a status when the background actually changed.
    /// </summary>
    private void KsRefreshForcedLobbyBackground()
    {
        var forced = KsTryGetForcedLobbyBackground(out var forcedBackground);
        if (!forced && !_ksLobbyBackgroundForced)
            return;

        var previousBackground = LobbyBackground;
        if (forced)
        {
            // Set even when it already matches: it may have been rolled before the map was picked, and it's the
            //      map's now, so leaving the map rerolls it.
            _ksLobbyBackgroundForced = true;
            LobbyBackground = forcedBackground;
        }
        else
        {
            RandomizeLobbyBackground(); // nothing forces one now, so this rolls and clears the flag
        }

        if (LobbyBackground != previousBackground)
            SendStatusToAll();
    }
}
