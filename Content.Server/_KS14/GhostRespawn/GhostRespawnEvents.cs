using Content.Shared._KS14.GhostRespawn;
using Robust.Shared.Player;

namespace Content.Server._KS14.GhostRespawn;

/// <summary>
///     What <see cref="GhostRespawnSystem"/> runs on: whether respawning is on, when the timer starts, and how long it
///         is. The cvars give the starting values; <see cref="KsRefreshGhostRespawnSettingsEvent"/> handlers (such as an
///         active <see cref="KsGhostRespawnOverrideRuleComponent"/>) replace them.
/// </summary>
/// <param name="Enabled">Whether a ghost may respawn at all.</param>
/// <param name="AlwaysStartTimer">Whether leaving a body that is still alive starts the timer too.</param>
/// <param name="Cooldown">How long after the first death a respawn is allowed.</param>
/// <param name="Penalty">Added to the cooldown of every later death, per respawn taken.</param>
public readonly record struct GhostRespawnSettings(bool Enabled, bool AlwaysStartTimer, TimeSpan Cooldown, TimeSpan Penalty);

/// <summary>
///     Raised broadcast by <see cref="GhostRespawnSystem.RefreshSettings"/> to work out the effective
///         <see cref="GhostRespawnSettings"/>, starting from the cvars. Pure: handlers only write
///         <see cref="Settings"/>.
/// </summary>
/// <remarks>
///     The result is cached on <see cref="GhostRespawnSystem.Settings"/>, so a system that contributes calls
///         <see cref="GhostRespawnSystem.RefreshSettings"/> whenever its contribution changes, or the cache goes stale.
///         Handlers run in no set order: a handler that replaces a field replaces whatever came before it, so two
///         contributors that both replace the same field should be combined by one owner, as
///         <see cref="KsGhostRespawnOverrideRuleSystem"/> does for every override rule.
/// </remarks>
[ByRefEvent]
public record struct KsRefreshGhostRespawnSettingsEvent(GhostRespawnSettings Settings);

/// <summary>
///     Raised broadcast by <see cref="GhostRespawnSystem.GetGate"/> to ask whether <see cref="Session"/> may respawn
///         now, on top of the respawn timer: after the timer is up when the ghost asks to respawn, and for every
///         pending ghost to show why it must wait. Pure: handlers only call <see cref="Deny"/>.
/// </summary>
/// <remarks>
///     Any rule or system can restrict respawning this way without <see cref="GhostRespawnSystem"/> knowing about it
///         (<c>KsFieldCommanderRuleSystem</c> allows it only shortly before a wave). A denied respawn costs nothing: the
///         penalty is charged only for a respawn that happens. Whoever denies calls
///         <see cref="GhostRespawnSystem.RefreshGates"/> whenever its answer changes for a reason other than time
///         reaching <see cref="OpensAt"/> (which the system rechecks by itself), so the ghosts' buttons stay current.
/// </remarks>
[ByRefEvent]
public record struct KsCanGhostRespawnEvent(ICommonSession Session)
{
    public bool Allowed = true;

    /// <summary>
    ///     Why not, shown to the ghost. Set with <see cref="Deny"/>.
    /// </summary>
    public LocId? Reason;

    /// <summary>
    ///     When the respawn should be allowed, if a denier can say; null when it can't. Set with
    ///         <see cref="Deny"/>.
    /// </summary>
    public TimeSpan? OpensAt;

    /// <summary>
    ///     Refuses the respawn. With several deniers the latest opening wins, and one that can't say when
    ///         (<paramref name="opensAt"/> null) beats any that can.
    /// </summary>
    public void Deny(LocId reason, TimeSpan? opensAt = null)
    {
        if (Allowed || OpensAt is { } current && (opensAt is not { } next || next > current))
        {
            Reason = reason;
            OpensAt = opensAt;
        }

        Allowed = false;
    }

    /// <summary>
    ///     The answer as the client is told it: null when allowed.
    /// </summary>
    public readonly GhostRespawnGate? ToGate()
        => Allowed || Reason is not { } reason ? null : new GhostRespawnGate(reason, OpensAt);
}
