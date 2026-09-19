using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.Systems;

namespace Content.Server._KS14.NPC.Operators;

public sealed partial class NPCIdleOperator : HTNOperator
{
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private NPCSteeringSystem _steering = default!;

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        // Pin the goal on our own tile - zero movement input results.
        _steering.Register(owner, _entMan.GetComponent<TransformComponent>(owner).Coordinates);

        return HTNOperatorStatus.Finished;
    }
}
