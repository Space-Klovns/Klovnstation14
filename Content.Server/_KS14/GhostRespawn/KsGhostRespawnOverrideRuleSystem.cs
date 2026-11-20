using Content.Server.GameTicking.Rules;
using Content.Shared.GameTicking.Components;

namespace Content.Server._KS14.GhostRespawn;

/// <summary>
///     Lays every active <see cref="KsGhostRespawnOverrideRuleComponent"/> over <see cref="GhostRespawnSystem"/>'s
///         settings, refreshing them as such a rule starts, ends or goes away.
/// </summary>
public sealed partial class KsGhostRespawnOverrideRuleSystem : GameRuleSystem<KsGhostRespawnOverrideRuleComponent>
{
    [Dependency] private GhostRespawnSystem _ghostRespawnSystem = default!;

    /// <summary>
    ///     Scratch list for <see cref="OnRefreshSettings"/>.
    /// </summary>
    private readonly List<(TimeSpan ActivatedAt, EntityUid Uid, KsGhostRespawnOverrideRuleComponent Component)> _activeRules = [];

    protected override void Started(EntityUid uid, KsGhostRespawnOverrideRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        _ghostRespawnSystem.RefreshSettings();
    }

    protected override void Ended(EntityUid uid, KsGhostRespawnOverrideRuleComponent component, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(uid, component, gameRule, args);

        // The rule is no longer active here, so it drops out.
        _ghostRespawnSystem.RefreshSettings();
    }

    // A round restart deletes rules without ending them.
    [SubscribeLocalEvent]
    private void OnShutdown(Entity<KsGhostRespawnOverrideRuleComponent> entity, ref ComponentShutdown args)
    {
        _ghostRespawnSystem.RefreshSettings();
    }

    [SubscribeLocalEvent]
    private void OnRefreshSettings(ref KsRefreshGhostRespawnSettingsEvent args)
    {
        _activeRules.Clear();
        var query = QueryActiveRules();
        while (query.MoveNext(out var uid, out _, out var overrideComponent, out var gameRuleComponent))
        {
            // One shutting down is still stored while its shutdown handler refreshes.
            if (overrideComponent.LifeStage > ComponentLifeStage.Running)
                continue;

            _activeRules.Add((gameRuleComponent.ActivatedAt, uid, overrideComponent));
        }

        // Oldest first, so that the latest started is laid on top.
        _activeRules.Sort((a, b) => a.ActivatedAt != b.ActivatedAt ? a.ActivatedAt.CompareTo(b.ActivatedAt) : a.Uid.CompareTo(b.Uid));

        var settings = args.Settings;
        foreach (var (_, _, overrideComponent) in _activeRules)
        {
            settings = settings with
            {
                Enabled = overrideComponent.Enabled ?? settings.Enabled,
                AlwaysStartTimer = overrideComponent.AlwaysStartTimer ?? settings.AlwaysStartTimer,
                Cooldown = overrideComponent.Cooldown ?? settings.Cooldown,
                Penalty = overrideComponent.Penalty ?? settings.Penalty,
            };
        }

        args.Settings = settings;
        _activeRules.Clear();
    }
}
