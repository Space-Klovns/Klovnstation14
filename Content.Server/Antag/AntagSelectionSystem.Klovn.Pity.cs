using Content.Server._KS14.Antag;
using Content.Server.Antag.Components;
using Content.Server.Players.PlayTimeTracking;
using Content.Shared._KS14.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Server.Antag;

// KS14: antag pity. The longer a player has played since they last rolled an antag, the likelier they are to roll one.
public sealed partial class AntagSelectionSystem
{
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IPlayTimeTrackingManager _playTimeTrackingManager = default!;
    [Dependency] private LastRolledAntagManager _lastRolledAntagManager = default!;

    /// <summary>
    ///     A player's weight in antag selection: 1 when they have just rolled one, rising linearly with the overall
    ///         playtime since then to <see cref="KsCCVars.AntagPityMaxWeight"/>.
    /// </summary>
    private float GetPityWeight(ICommonSession player)
    {
        if (!_configurationManager.GetCVar(KsCCVars.AntagPityEnabled))
            return 1f;

        var hoursToMax = _configurationManager.GetCVar(KsCCVars.AntagPityHoursToMax);
        var maxWeight = _configurationManager.GetCVar(KsCCVars.AntagPityMaxWeight);
        if (hoursToMax <= 0f || maxWeight <= 1f)
            return 1f;

        // A player who has never rolled one counts from zero: all of their playtime
        _lastRolledAntagManager.TryGetLastRolled(player.UserId, out var lastRolled);
        var sinceLastRolled = _playTimeTrackingManager.GetOverallPlaytime(player) - (lastRolled ?? TimeSpan.Zero);

        var fraction = Math.Clamp((float)sinceLastRolled.TotalHours / hoursToMax, 0f, 1f);
        return 1f + (maxWeight - 1f) * fraction;
    }

    [SubscribeLocalEvent]
    private void OnAntagSelectedForPity(Entity<AntagSelectionComponent> entity, ref AfterAntagEntitySelectedEvent args)
    {
        if (args.Session is not { } session)
            return;

        // Stored as overall playtime rather than a date, so that time spent away from the game doesn't count
        _lastRolledAntagManager.TrySetLastRolled(session.UserId, _playTimeTrackingManager.GetOverallPlaytime(session));
    }
}
