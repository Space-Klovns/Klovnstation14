using System.Threading;
using System.Threading.Tasks;
using Content.Server._KS14.NPC.Hands;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
///     With a gun in the other hand and none in the active one - a magazine swap cut short by a replan - makes that hand
///         active again, dropping whatever the active one held. Fails to plan otherwise. See
///         <see cref="NpcHandsSystem.TryTakeUpGunInOtherHand"/>.
/// </summary>
public sealed partial class TakeUpGunOperator : HTNOperator
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private NpcHandsSystem _npcHandsSystem = default!;

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        if (!blackboard.TryGetOwner(_entityManager, out var ownerUid) || !_npcHandsSystem.TryFindGunInOtherHand(ownerUid, out var hand))
            return (false, null);

        return (true, new Dictionary<string, object>
        {
            { NPCBlackboard.ActiveHand, hand },
            { NPCBlackboard.ActiveHandFree, false },
        });
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        return _npcHandsSystem.TryTakeUpGunInOtherHand(blackboard.GetOwner()) ? HTNOperatorStatus.Finished : HTNOperatorStatus.Failed;
    }
}
