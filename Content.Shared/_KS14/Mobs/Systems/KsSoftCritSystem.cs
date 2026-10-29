using Content.Shared._KS14.Mobs.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Speech;

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
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MovementSpeedModifierSystem _movementSpeed = default!;

    [Dependency] private EntityQuery<KsSoftCritComponent> _softCritQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KsSoftCritComponent, MobStateChangedEvent>(OnMobStateChanged);
        // Mob thresholds still run on total damage and DamageChangedEvent. Soft crit has to measure damage
        // the same way to line up with them, and DamageDealtEvent doesn't cover healing out of hard crit.
#pragma warning disable CS0618
        SubscribeLocalEvent<KsSoftCritComponent, DamageChangedEvent>(OnDamageChanged);
#pragma warning restore CS0618
        SubscribeLocalEvent<KsSoftCritComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<KsSoftCritComponent, AfterAutoHandleStateEvent>(OnAfterHandleState);
    }

    /// <summary>
    ///     True while the mob is critical but not yet in hard crit.
    /// </summary>
    public bool IsSoftCrit(EntityUid uid)
    {
        return _softCritQuery.TryComp(uid, out KsSoftCritComponent? softCrit) && softCrit.Active;
    }

    /// <summary>
    ///     Whether crit should let this action through: soft crit mobs can still crawl, turn and whisper.
    /// </summary>
    public bool AllowsInCrit(EntityUid uid, CancellableEntityEventArgs args)
    {
        return args is UpdateCanMoveEvent or ChangeDirectionAttemptEvent or SpeakAttemptEvent && IsSoftCrit(uid);
    }

    private void OnMobStateChanged(Entity<KsSoftCritComponent> ent, ref MobStateChangedEvent args)
    {
        Refresh(ent);
    }

#pragma warning disable CS0618
    private void OnDamageChanged(Entity<KsSoftCritComponent> ent, ref DamageChangedEvent args)
#pragma warning restore CS0618
    {
        Refresh(ent);
    }

    private void OnRefreshSpeed(Entity<KsSoftCritComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.Active)
            args.ModifySpeed(ent.Comp.CrawlSpeedModifier);
    }

    private void OnAfterHandleState(Entity<KsSoftCritComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        // Keep client prediction in line with the server.
        UpdateEffects(ent);
    }

    private void Refresh(Entity<KsSoftCritComponent> ent)
    {
        var active = ShouldBeSoftCrit(ent);
        if (active == ent.Comp.Active)
            return;

        ent.Comp.Active = active;
        Dirty(ent);
        UpdateEffects(ent);
    }

    private void UpdateEffects(EntityUid uid)
    {
        _actionBlocker.UpdateCanMove(uid);
        _movementSpeed.RefreshMovementSpeedModifiers(uid);
    }

    /// <summary>
    ///     How deep into soft crit the mob is: 0 right at the crit threshold, 1 at hard crit.
    ///     Returns 0 when not in soft crit.
    /// </summary>
    public float GetSoftCritSeverity(EntityUid uid)
    {
        if (!IsSoftCrit(uid) || !TryGetSoftCritProgress(uid, out var progress))
            return 0f;

        return Math.Clamp(progress, 0f, 1f);
    }

    private bool ShouldBeSoftCrit(EntityUid uid)
    {
        if (!TryComp<MobStateComponent>(uid, out var mobState) || mobState.CurrentState != MobState.Critical)
            return false;

        return TryGetSoftCritProgress(uid, out var progress) && progress < 1f;
    }

    /// <summary>
    ///     Where the mob's damage sits between the crit threshold (0) and hard crit (1).
    /// </summary>
    private bool TryGetSoftCritProgress(EntityUid uid, out float progress)
    {
        progress = 0f;
        if (!_softCritQuery.TryComp(uid, out var softCrit) ||
            !TryComp<MobThresholdsComponent>(uid, out var thresholds) ||
            !TryComp<DamageableComponent>(uid, out var damageable))
        {
            return false;
        }

        if (!TryGetThreshold(thresholds, MobState.Critical, out var crit) ||
            !TryGetThreshold(thresholds, MobState.Dead, out var dead))
        {
            return false;
        }

        var softCritLength = (dead - crit).Float() * softCrit.HardCritFraction;
        if (softCritLength <= 0f)
            return false;

        // Same measure MobThresholdSystem uses for its thresholds.
#pragma warning disable CS0618
        var damage = _damageable.GetTotalDamage((uid, damageable));
#pragma warning restore CS0618
        progress = (damage - crit).Float() / softCritLength;
        return true;
    }

    /// <summary>
    ///     Gets a threshold the same way <see cref="MobThresholdSystem"/> checks it, adjustments included.
    /// </summary>
    private static bool TryGetThreshold(MobThresholdsComponent thresholds, MobState state, out FixedPoint2 threshold)
    {
        foreach (var (damage, thresholdState) in thresholds.Thresholds)
        {
            if (thresholdState != state)
                continue;

            threshold = damage + thresholds.ThresholdAdjustments.GetValueOrDefault(state);
            return true;
        }

        threshold = FixedPoint2.Zero;
        return false;
    }
}
