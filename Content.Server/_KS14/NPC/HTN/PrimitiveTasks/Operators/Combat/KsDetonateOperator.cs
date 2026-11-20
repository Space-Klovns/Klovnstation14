using System.Threading;
using System.Threading.Tasks;
using Content.Server._KS14.NPC.HTN;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Trigger.Components;
using Content.Shared.Trigger.Systems;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Combat;

/// <summary>
///     Arms the owner's own <see cref="TimerTriggerComponent"/>: its beeps, popup and primed look are the warning, and
///         whatever the timer sets off (an <c>ExplodeOnTrigger</c>) is the blast. A suicide drone's last task. Runs until
///         the timer has gone off, which for something that blows itself up is never: nothing after it plans.
/// </summary>
public sealed partial class KsDetonateOperator : HTNOperator
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private TriggerSystem _triggerSystem = default!;

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        return (_entityManager.HasComponent<TimerTriggerComponent>(blackboard.GetOwner()), null);
    }

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);

        var ownerUid = blackboard.GetOwner();
        _triggerSystem.ActivateTimerTrigger(ownerUid, user: ownerUid);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        return _entityManager.HasComponent<ActiveTimerTriggerComponent>(blackboard.GetOwner())
            ? HTNOperatorStatus.Continuing
            : HTNOperatorStatus.Finished;
    }
}
