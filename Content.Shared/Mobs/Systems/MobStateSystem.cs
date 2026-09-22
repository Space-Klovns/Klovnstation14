using Content.Shared.ActionBlocker;
using Content.Shared.Administration.Logs;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs.Components;
using Content.Shared.Standing;
using Content.Shared._KS14.Components.Mobs;
using Robust.Shared.Timing;
namespace Content.Shared.Mobs.Systems;

[Virtual]
public partial class MobStateSystem : EntitySystem
{
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private StandingStateSystem _standing = default!;
    [Dependency] private ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private ILogManager _logManager = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    private ISawmill _sawmill = default!;

    [Dependency] private EntityQuery<MobStateComponent> _mobStateQuery = default!;

    public override void Initialize()
    {
        _sawmill = _logManager.GetSawmill("MobState");
        base.Initialize();
        SubscribeEvents();
    }

    #region Public API

    /// <summary>
    ///  Check if a Mob is Alive
    /// </summary>
    /// <param name="target">Target Entity</param>
    /// <param name="component">The MobState component owned by the target</param>
    /// <returns>If the entity is alive</returns>
    public bool IsAlive(EntityUid target, MobStateComponent? component = null)
    {
        if (!_mobStateQuery.Resolve(target, ref component, false))
            return false;
        return component.CurrentState == MobState.Alive;
    }

    /// <summary>
    ///  Check if a Mob is Critical
    /// </summary>
    /// <param name="target">Target Entity</param>
    /// <param name="component">The MobState component owned by the target</param>
    /// <returns>If the entity is Critical</returns>
    public bool IsCritical(EntityUid target, MobStateComponent? component = null)
    {
        if (!_mobStateQuery.Resolve(target, ref component, false))
            return false;
        return component.CurrentState == MobState.Critical;
    }

    /// <summary>
    ///  Check if a Mob is Dead
    /// </summary>
    /// <param name="target">Target Entity</param>
    /// <param name="component">The MobState component owned by the target</param>
    /// <returns>If the entity is Dead</returns>
    public bool IsDead(EntityUid target, MobStateComponent? component = null)
    {
        if (!_mobStateQuery.Resolve(target, ref component, false))
            return false;
        return component.CurrentState == MobState.Dead;
    }

    /// <summary>
    ///  Check if a Mob is Critical or Dead
    /// </summary>
    /// <param name="target">Target Entity</param>
    /// <param name="component">The MobState component owned by the target</param>
    /// <returns>If the entity is Critical or Dead</returns>
    public bool IsIncapacitated(EntityUid target, MobStateComponent? component = null)
    {
        if (!_mobStateQuery.Resolve(target, ref component, false))
            return false;
        return component.CurrentState is MobState.Critical or MobState.Dead;
    }

    /// <summary>
    ///  Check if a Mob is in an Invalid state
    /// </summary>
    /// <param name="target">Target Entity</param>
    /// <param name="component">The MobState component owned by the target</param>
    /// <returns>If the entity is in an Invalid State</returns>
    public bool IsInvalidState(EntityUid target, MobStateComponent? component = null)
    {
        if (!_mobStateQuery.Resolve(target, ref component, false))
            return false;
        return component.CurrentState is MobState.Invalid;
    }

    /// <summary>
    /// True while the mob is in Critical but has species soft-crit and
    /// damage below the soft-crit boundary.
    /// </summary>
    public bool IsSoftCrit(EntityUid uid, MobStateComponent? component = null,
        SoftCriticalComponent? soft = null)
    {
        if (!Resolve(uid, ref component, false) || component.CurrentState != MobState.Critical)
            return false;

        if (!_thresholds.TryGetSoftCritBoundary(uid, out var boundary, soft))
            return false;

        return _thresholds.GetTotalDamage(uid) < boundary;
    }

    #endregion
}
