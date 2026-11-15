using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN.Preconditions; // KS14
using Content.Shared.CombatMode;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Audio;

namespace Content.Server.NPC.HTN.PrimitiveTasks.Operators.Combat.Ranged;

public sealed partial class GunOperator : HTNOperator, IHtnConditionalShutdown
{
    [Dependency] private IEntityManager _entManager = default!;

    [DataField("shutdownState")]
    public HTNPlanState ShutdownState { get; private set; } = HTNPlanState.TaskFinished;

    /// <summary>
    /// Key that contains the target entity.
    /// </summary>
    [DataField("targetKey", required: true)]
    public string TargetKey = default!;

    /// <summary>
    /// Minimum damage state that the target has to be in for us to consider attacking.
    /// </summary>
    [DataField("targetState")]
    public MobState TargetState = MobState.Alive;

    /// <summary>
    /// Do we require line of sight of the target before failing.
    /// </summary>
    [DataField("requireLOS")]
    public bool RequireLOS = false;

    /// <summary>
    /// If true, only opaque objects will block line of sight.
    /// </summary>
    [DataField("opaqueKey")]
    public bool UseOpaqueForLOSChecks = false;

    // KS14 start
    /// <summary>
    ///     Tell seeing the target from being able to hit it: in sight (walls and the like, not mobs), aim and track it, and
    ///         hold fire while a round would not get there or would hit a friend, without failing. For turrets. See
    ///         <see cref="NPCRangedCombatComponent.KsHoldFireWhenBlocked"/>; overrides <see cref="UseOpaqueForLOSChecks"/>.
    /// </summary>
    [DataField("ksHoldFireWhenBlocked")]
    public bool KsHoldFireWhenBlocked;
    // KS14 end

    // Like movement we add a component and pass it off to the dedicated system.

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        // Don't attack if they're already as wounded as we want them.
        if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var target, _entManager))
        {
            return (false, null);
        }

        if (_entManager.TryGetComponent<MobStateComponent>(target, out var mobState) &&
            mobState.CurrentState > TargetState)
        {
            return (false, null);
        }

        return (true, null);
    }

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);

        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var ranged = _entManager.EnsureComponent<NPCRangedCombatComponent>(owner);
        ranged.Target = blackboard.GetValue<EntityUid>(TargetKey);
        ranged.UseOpaqueForLOSChecks = UseOpaqueForLOSChecks;
        ranged.KsHoldFireWhenBlocked = KsHoldFireWhenBlocked; // KS14
        ranged.KsStandingMobsBlockSight = blackboard.GetValueOrDefault<bool>(TargetInLOSPrecondition.KsStandingMobsBlockSightKey, _entManager); // KS14

        if (blackboard.TryGetValue<float>(NPCBlackboard.RotateSpeed, out var rotSpeed, _entManager))
        {
            ranged.RotationSpeed = new Angle(rotSpeed);
        }

        if (blackboard.TryGetValue<SoundSpecifier>("SoundTargetInLOS", out var losSound, _entManager))
        {
            ranged.SoundTargetInLOS = losSound;
        }

        // Set the shoot delay based on the gun's fire rate
        if (_entManager.TryGetComponent<GunComponent>(owner, out var gunComp) && gunComp.FireRate > 0)
        {
            ranged.ShootDelay = 1f / gunComp.FireRate;
        }
    }

    public void ConditionalShutdown(NPCBlackboard blackboard)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        _entManager.System<SharedCombatModeSystem>().SetInCombatMode(owner, false);
        _entManager.RemoveComponent<NPCRangedCombatComponent>(owner);
        blackboard.Remove<EntityUid>(TargetKey);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        base.Update(blackboard, frameTime);
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        HTNOperatorStatus status;

        if (_entManager.TryGetComponent<NPCRangedCombatComponent>(owner, out var combat) &&
            blackboard.TryGetValue<EntityUid>(TargetKey, out var target, _entManager))
        {
            combat.Target = target;

            // Success
            if (_entManager.TryGetComponent<MobStateComponent>(combat.Target, out var mobState) &&
                mobState.CurrentState > TargetState)
            {
                status = HTNOperatorStatus.Finished;
            }
            else
            {
                switch (combat.Status)
                {
                    case CombatStatus.TargetUnreachable:
                        status = HTNOperatorStatus.Failed;
                        break;
                    case CombatStatus.NotInSight:
                        if (RequireLOS)
                            status = HTNOperatorStatus.Failed;
                        else
                            status = HTNOperatorStatus.Continuing;
                        break;
                    case CombatStatus.Normal:
                    case CombatStatus.KsShotBlocked: // KS14: aiming, holding fire
                        status = HTNOperatorStatus.Continuing;
                        break;
                    default:
                        status = HTNOperatorStatus.Failed;
                        break;
                }
            }
        }
        else
        {
            status = HTNOperatorStatus.Failed;
        }

        // Mark it as finished to continue the plan.
        if (status == HTNOperatorStatus.Continuing && ShutdownState == HTNPlanState.PlanFinished)
        {
            status = HTNOperatorStatus.Finished;
        }

        return status;
    }
}
