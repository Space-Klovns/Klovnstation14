using Content.Server._KS14.NPC.Charges;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Charges;

/// <summary>
///     Presses the activator at <see cref="ActivatorKey"/>, in one go, as with a grenade: see <see cref="NpcChargeSystem"/>.
/// </summary>
public sealed partial class DetonateChargeOperator : HTNOperator
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private NpcChargeSystem _npcChargeSystem = default!;

    [DataField]
    public string ActivatorKey = "OrderTool";

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        if (!blackboard.TryGetValue<EntityUid>(ActivatorKey, out var activatorUid, _entityManager))
            return HTNOperatorStatus.Failed;

        return _npcChargeSystem.TryDetonate(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), activatorUid)
            ? HTNOperatorStatus.Finished
            : HTNOperatorStatus.Failed;
    }
}
