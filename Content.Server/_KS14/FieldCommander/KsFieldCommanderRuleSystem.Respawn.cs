using Content.Server._KS14.GhostRespawn;
using Content.Shared.GameTicking.Components;

namespace Content.Server._KS14.FieldCommander;

public sealed partial class KsFieldCommanderRuleSystem
{
    [Dependency] private GhostRespawnSystem _ghostRespawnSystem = default!;
    [Dependency] private IViewVariablesManager _viewVariablesManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        // vvwrite on these goes through their setters, which tell waiting ghosts; the window itself can't write them.
        var handler = _viewVariablesManager.GetTypeHandler<KsFieldCommanderRespawnWindowComponent>();
        handler.AddPath(
            nameof(KsFieldCommanderRespawnWindowComponent.Window),
            (_, windowComponent) => windowComponent.Window,
            (uid, window, windowComponent) =>
            {
                if (windowComponent != null)
                    SetRespawnWindow((uid, windowComponent), window);
            });
        handler.AddPath(
            nameof(KsFieldCommanderRespawnWindowComponent.OpenWhileNoWaveScheduled),
            (_, windowComponent) => windowComponent.OpenWhileNoWaveScheduled,
            (uid, open, windowComponent) =>
            {
                if (windowComponent != null)
                    SetOpenWhileNoWaveScheduled((uid, windowComponent), open);
            });
    }

    public override void Shutdown()
    {
        base.Shutdown();

        // The manager outlives this system, and its lambdas would keep it alive.
        var handler = _viewVariablesManager.GetTypeHandler<KsFieldCommanderRespawnWindowComponent>();
        handler.RemovePath(nameof(KsFieldCommanderRespawnWindowComponent.Window));
        handler.RemovePath(nameof(KsFieldCommanderRespawnWindowComponent.OpenWhileNoWaveScheduled));
    }

    /// <summary>
    ///     Sets how long before each wave respawning opens, and tells waiting ghosts.
    /// </summary>
    public void SetRespawnWindow(Entity<KsFieldCommanderRespawnWindowComponent> entity, TimeSpan window)
    {
        if (entity.Comp.Window == window)
            return;

        entity.Comp.Window = window;
        RefreshRespawnWindow();
    }

    /// <summary>
    ///     Sets whether ghosts may respawn while no wave is scheduled, and tells waiting ghosts.
    /// </summary>
    public void SetOpenWhileNoWaveScheduled(Entity<KsFieldCommanderRespawnWindowComponent> entity, bool open)
    {
        if (entity.Comp.OpenWhileNoWaveScheduled == open)
            return;

        entity.Comp.OpenWhileNoWaveScheduled = open;
        RefreshRespawnWindow();
    }

    [SubscribeLocalEvent]
    private void OnCanGhostRespawn(ref KsCanGhostRespawnEvent args)
    {
        var curTime = _gameTiming.CurTime;
        var query = EntityQueryEnumerator<KsFieldCommanderRespawnWindowComponent, KsFieldCommanderRuleComponent, ActiveGameRuleComponent>();
        while (query.MoveNext(out var windowComponent, out var ruleComponent, out _))
        {
            if (windowComponent.LifeStage > ComponentLifeStage.Running)
                continue;

            if (!IsRespawnWindowOpen(windowComponent, ruleComponent, curTime, out var opensAt))
                args.Deny(windowComponent.Reason, opensAt);
        }
    }

    // A round restart deletes rules without ending them.
    [SubscribeLocalEvent]
    private void OnRespawnWindowShutdown(Entity<KsFieldCommanderRespawnWindowComponent> entity, ref ComponentShutdown args)
    {
        _ghostRespawnSystem.RefreshGates();
    }

    /// <summary>
    ///     Whether the respawn window before the next wave is open now; if not, <paramref name="opensAt"/> is when it
    ///         will, or null if that can't be told. With no wave scheduled, open only if
    ///         <see cref="KsFieldCommanderRespawnWindowComponent.OpenWhileNoWaveScheduled"/>.
    /// </summary>
    private static bool IsRespawnWindowOpen(
        KsFieldCommanderRespawnWindowComponent windowComponent,
        KsFieldCommanderRuleComponent ruleComponent,
        TimeSpan curTime,
        out TimeSpan? opensAt)
    {
        opensAt = null;

        // A stopped clock (no wave scheduled, or a paused timeline) gives no time to wait for.
        if (ruleComponent.NextWaveTime == TimeSpan.MaxValue)
            return windowComponent.OpenWhileNoWaveScheduled;

        var windowOpens = ruleComponent.NextWaveTime - windowComponent.Window;
        if (curTime >= windowOpens)
            return true;

        opensAt = windowOpens;
        return false;
    }

    /// <summary>
    ///     Tells ghosts waiting to respawn that the respawn window moved. Call whenever what
    ///         <see cref="IsRespawnWindowOpen"/> reads changes, other than time passing.
    /// </summary>
    private void RefreshRespawnWindow()
    {
        _ghostRespawnSystem.RefreshGates();
    }
}
