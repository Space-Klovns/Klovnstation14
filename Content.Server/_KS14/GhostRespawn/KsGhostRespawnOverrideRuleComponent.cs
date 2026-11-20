namespace Content.Server._KS14.GhostRespawn;

/// <summary>
///     A gamerule that, while active, replaces <see cref="GhostRespawnSystem"/>'s cvar-driven settings with its own:
///         each field left out keeps the cvar's value (or an earlier rule's), each one set replaces it outright,
///         rather than adding to it. The cvars themselves are left alone and apply again once the rule ends.
/// </summary>
/// <remarks>
///     <para>With several active, the most recently started rule wins each field it sets.</para>
///     <para>To restrict <em>when</em> a respawn is allowed rather than how long it takes, handle
///         <see cref="KsCanGhostRespawnEvent"/> instead (as the Field Commander's respawn window does); anything that
///         changes the settings in code rather than YAML handles <see cref="KsRefreshGhostRespawnSettingsEvent"/>.</para>
/// </remarks>
/// <example>
/// <code>
/// - type: KsGhostRespawnOverrideRule
///   enabled: true
///   cooldown: 60
///   penalty: 30
/// </code>
/// </example>
[RegisterComponent, Access(typeof(KsGhostRespawnOverrideRuleSystem))]
public sealed partial class KsGhostRespawnOverrideRuleComponent : Component
{
    /// <summary>
    ///     Whether respawning is allowed at all (<c>klovn.ghostrespawn.enabled</c>).
    /// </summary>
    [DataField]
    public bool? Enabled;

    /// <summary>
    ///     Whether leaving a living body starts the timer too (<c>klovn.ghostrespawn.always_start_respawn_timer</c>).
    /// </summary>
    [DataField]
    public bool? AlwaysStartTimer;

    /// <summary>
    ///     How long after the first death a respawn is allowed (<c>klovn.ghostrespawn.cooldown_seconds</c>). Pending
    ///         timers move by the difference when the rule starts or ends.
    /// </summary>
    [DataField]
    public TimeSpan? Cooldown;

    /// <summary>
    ///     Added to the cooldown after every respawn taken (<c>klovn.ghostrespawn.penalty_seconds</c>).
    /// </summary>
    [DataField]
    public TimeSpan? Penalty;
}
