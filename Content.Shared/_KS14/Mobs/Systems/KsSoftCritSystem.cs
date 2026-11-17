using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Mobs.Components;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared.ActionBlocker;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Speech;
using Content.Shared.Stunnable;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.Mobs.Systems;

/// <summary>
///     Tracks whether a critical mob is in soft crit. See <see cref="KsSoftCritComponent"/>.
/// </summary>
/// <remarks>
///     Crit itself is blocked by <see cref="MobStateSystem"/>, which asks <see cref="AllowsInCrit"/> before
///         cancelling an action. Anything else blocking the same action (stuns, cuffs, buckling...) still applies.
/// </remarks>
public sealed partial class KsSoftCritSystem : EntitySystem
{
    [Dependency] private ActionBlockerSystem _actionBlockerSystem = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private MovementSpeedModifierSystem _movementSpeedModifierSystem = default!;
    [Dependency] private SharedStunSystem _stunSystem = default!;
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;

    [Dependency] private EntityQuery<KsSoftCritComponent> _softCritQuery = default!;
    [Dependency] private EntityQuery<MobStateComponent> _mobStateQuery = default!;
    [Dependency] private EntityQuery<MobThresholdsComponent> _mobThresholdsQuery = default!;
    [Dependency] private EntityQuery<DamageableComponent> _damageableQuery = default!;

    private bool _enabled;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_configurationManager, KsCCVars.SoftCritEnabled, OnEnabledChanged, invokeImmediately: true);
    }

    /// <summary>
    ///     True while the mob is critical but not yet in hard crit.
    /// </summary>
    public bool IsSoftCrit(EntityUid uid)
    {
        return _softCritQuery.TryComp(uid, out var softCritComponent) && softCritComponent.Active;
    }

    /// <summary>
    ///     Whether crit should let this action through: soft crit mobs can still crawl, turn and whisper.
    /// </summary>
    public bool AllowsInCrit(EntityUid uid, CancellableEntityEventArgs args)
    {
        return args is UpdateCanMoveEvent or ChangeDirectionAttemptEvent or SpeakAttemptEvent && IsSoftCrit(uid);
    }

    /// <summary>
    ///     How deep into soft crit the mob is: 0 right at the crit threshold, 1 at hard crit.
    ///     Returns 0 when not in soft crit.
    /// </summary>
    public float GetSoftCritSeverity(Entity<KsSoftCritComponent?> entity)
    {
        if (!_softCritQuery.Resolve(entity.Owner, ref entity.Comp, logMissing: false) ||
            !entity.Comp.Active ||
            !TryGetSoftCritProgress((entity.Owner, entity.Comp), out var progress))
        {
            return 0f;
        }

        return Math.Clamp(progress, 0f, 1f);
    }

    private void OnEnabledChanged(bool enabled)
    {
        if (_enabled == enabled)
            return;

        _enabled = enabled;

        // Put anyone already critical into or out of soft crit right away, paused maps included, so nobody
        //      is left half in soft crit once their map unpauses.
        var softCritEnumerator = AllEntityQuery<KsSoftCritComponent>();
        while (softCritEnumerator.MoveNext(out var uid, out var softCritComponent))
        {
            Refresh((uid, softCritComponent));
        }
    }

    /// <summary>
    ///     Raised after every damage change, every mob state change and every threshold change
    ///         (adjustments included), each time after the mob state has been brought up to date.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnThresholdChecked(Entity<KsSoftCritComponent> entity, ref MobThresholdChecked args)
    {
        Refresh(entity);
    }

    [SubscribeLocalEvent]
    private void OnRefreshSpeed(Entity<KsSoftCritComponent> entity, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (entity.Comp.Active)
            args.ModifySpeed(entity.Comp.CrawlSpeedModifier);
    }

    [SubscribeLocalEvent]
    private void OnAfterHandleState(Entity<KsSoftCritComponent> entity, ref AfterAutoHandleStateEvent args)
    {
        // Keep client prediction in line with the server.
        UpdateEffects(entity);
    }

    [SubscribeLocalEvent]
    private void OnStandUpAttempt(Entity<KsSoftCritComponent> entity, ref StandUpAttemptEvent args)
    {
        if (!entity.Comp.Active || args.Cancelled)
            return;

        // Crit normally keeps you down by not letting you move at all, but soft crit lets you crawl,
        //      which would otherwise let you get back up from the knockdown. Going weightless still ends
        //      the knockdown; StandingStateSystem.KsOnWeightlessnessChanged puts the mob back down when
        //      gravity returns, because this cancels its stand-up.
        args.Cancelled = true;
        args.Autostand = false;
        args.Message = (KsPopupMessage.Create("ks-soft-crit-stand-attempt"), PopupType.SmallCaution);
    }

    private void Refresh(Entity<KsSoftCritComponent> entity)
    {
        // The server's result comes in with the state.
        if (_gameTiming.ApplyingState)
            return;

        var active = ShouldBeSoftCrit(entity);
        if (active == entity.Comp.Active)
            return;

        entity.Comp.Active = active;
        Dirty(entity);

        if (active)
        {
            // Crawl like any other knocked down mob, for as long as soft crit lasts.
            _stunSystem.TryCrawling(entity.Owner, time: null, autoStand: false);
            _stunSystem.SetAutoStand(entity.Owner, autoStand: false);
        }
        else
        {
            // Get up as normal once able: right away if healed, or never while hard crit or death
            //      keeps you from moving. Any knockdown time left from something else still applies.
            _stunSystem.SetAutoStand(entity.Owner, autoStand: true);
        }

        UpdateEffects(entity);
    }

    private void UpdateEffects(EntityUid uid)
    {
        _actionBlockerSystem.UpdateCanMove(uid);
        _movementSpeedModifierSystem.RefreshMovementSpeedModifiers(uid);
    }

    private bool ShouldBeSoftCrit(Entity<KsSoftCritComponent> entity)
    {
        if (!_enabled ||
            !_mobStateQuery.TryComp(entity, out var mobStateComponent) ||
            mobStateComponent.CurrentState != MobState.Critical)
        {
            return false;
        }

        return TryGetSoftCritProgress(entity, out var progress) && progress < 1f;
    }

    /// <summary>
    ///     Where the mob's damage sits between the crit threshold (0) and hard crit (1).
    /// </summary>
    private bool TryGetSoftCritProgress(Entity<KsSoftCritComponent> entity, out float progress)
    {
        progress = 0f;
        if (!_mobThresholdsQuery.TryComp(entity, out var thresholdsComponent) ||
            !_damageableQuery.TryComp(entity, out var damageableComponent))
        {
            return false;
        }

        if (!TryGetThreshold(thresholdsComponent, MobState.Critical, out var critThreshold) ||
            !TryGetThreshold(thresholdsComponent, MobState.Dead, out var deadThreshold))
        {
            return false;
        }

        var softCritLength = (float)(deadThreshold - critThreshold) * entity.Comp.HardCritFraction;
        if (softCritLength <= 0f)
            return false;

        // Mob thresholds still run on total damage, and soft crit has to measure it the same way to line up with them.
#pragma warning disable CS0618
        var damage = _damageableSystem.GetTotalDamage((entity.Owner, damageableComponent));
#pragma warning restore CS0618
        progress = (float)(damage - critThreshold) / softCritLength;
        return true;
    }

    /// <summary>
    ///     Gets a threshold the same way <see cref="MobThresholdSystem"/> checks it, adjustments included,
    ///         so chems that shift crit (like klovnide's safe-crit) shift the soft crit window along with it.
    /// </summary>
    private static bool TryGetThreshold(MobThresholdsComponent thresholdsComponent, MobState state, out FixedPoint2 threshold)
    {
        foreach (var (damage, thresholdState) in thresholdsComponent.Thresholds)
        {
            if (thresholdState != state)
                continue;

            threshold = damage + thresholdsComponent.ThresholdAdjustments.GetValueOrDefault(state);
            return true;
        }

        threshold = FixedPoint2.Zero;
        return false;
    }
}
