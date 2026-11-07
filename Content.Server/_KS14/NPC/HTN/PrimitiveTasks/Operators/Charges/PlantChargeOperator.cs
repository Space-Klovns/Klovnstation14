using Content.Server._KS14.NPC.Charges;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Charges;

/// <summary>
///     Sticks the charge at <see cref="ChargeKey"/> on what is at <see cref="TargetKey"/>, wiring it up first if it takes a
///         wire. See <see cref="NpcChargeSystem"/>. If the task is cut short its hands go back as they were all the same.
/// </summary>
public sealed partial class PlantChargeOperator : HTNOperator
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private NpcChargeSystem _npcChargeSystem = default!;

    [DataField]
    public string TargetKey = "OrderTarget";

    [DataField]
    public string ChargeKey = "OrderTool";

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);

        if (blackboard.TryGetValue<EntityUid>(ChargeKey, out var chargeUid, _entityManager))
            _npcChargeSystem.TryStartPlant(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), chargeUid);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var targetUid, _entityManager) ||
            !blackboard.TryGetValue<EntityUid>(ChargeKey, out var chargeUid, _entityManager))
            return HTNOperatorStatus.Failed;

        if (_npcChargeSystem.IsPlanted(chargeUid, targetUid))
            return HTNOperatorStatus.Finished;

        var ownerUid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!_npcChargeSystem.IsPlanting(ownerUid))
            return HTNOperatorStatus.Failed;

        _npcChargeSystem.UpdatePlant(ownerUid, targetUid, chargeUid);
        return HTNOperatorStatus.Continuing;
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        base.TaskShutdown(blackboard, status);
        _npcChargeSystem.StopPlant(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
    }
}
