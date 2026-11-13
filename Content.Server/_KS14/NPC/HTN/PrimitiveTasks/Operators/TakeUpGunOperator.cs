using System.Threading;
using System.Threading.Tasks;
using Content.Server._KS14.NPC.Doors;
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
/// <remarks>
///     Not while forcing a door (<see cref="NpcDoorSystem.IsBreaching(EntityUid)"/>): the tool in the active hand and the
///         gun in the other look just like a swap cut short, and dropping the tool for the gun ends the breach, which a
///         move straight after starts again - over and over.
/// </remarks>
public sealed partial class TakeUpGunOperator : HTNOperator
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private NpcDoorSystem _npcDoorSystem = default!;
    [Dependency] private NpcHandsSystem _npcHandsSystem = default!;

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        if (!blackboard.TryGetOwner(_entityManager, out var ownerUid) ||
            _npcDoorSystem.IsBreaching(ownerUid) ||
            !_npcHandsSystem.TryFindGunInOtherHand(ownerUid, out var hand))
            return (false, null);

        return (true, new Dictionary<string, object>
        {
            { NPCBlackboard.ActiveHand, hand },
            { NPCBlackboard.ActiveHandFree, false },
        });
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var ownerUid = blackboard.GetOwner();
        return !_npcDoorSystem.IsBreaching(ownerUid) && _npcHandsSystem.TryTakeUpGunInOtherHand(ownerUid)
            ? HTNOperatorStatus.Finished
            : HTNOperatorStatus.Failed;
    }
}
