using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;

public sealed partial class StaticWaitOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entityManager = default!;

    [DataField("key", required: true)] public string DelayKey = "Delay";
    [DataField] public string TimerKey = null!;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);

        if (TimerKey is not { })
            TimerKey = DelayKey + "-autogen-timer";
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var timer = blackboard.GetValueOrDefault<float>(TimerKey, _entityManager);

        timer += frameTime;
        blackboard.SetValue(TimerKey, timer);

        return timer > blackboard.GetValue<float>(DelayKey) ? HTNOperatorStatus.Finished : HTNOperatorStatus.Continuing;
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        base.TaskShutdown(blackboard, status);

        if (status != HTNOperatorStatus.BetterPlan)
            blackboard.Remove<float>(TimerKey);
    }
}
