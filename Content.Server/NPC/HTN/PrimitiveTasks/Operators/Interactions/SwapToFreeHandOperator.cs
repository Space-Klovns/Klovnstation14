using System.Threading;
using System.Threading.Tasks;
using Content.Server.Hands.Systems;
using Content.Shared.Hands.Components;
using Content.Shared.Inventory.VirtualItem;

namespace Content.Server.NPC.HTN.PrimitiveTasks.Operators.Interactions;


/// <summary>
/// Swaps to any free hand.
/// </summary>
public sealed partial class SwapToFreeHandOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entManager = default!;

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        if (!blackboard.TryGetValue<List<string>>(NPCBlackboard.FreeHands, out var hands, _entManager) ||
            !_entManager.TryGetComponent<HandsComponent>(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), out var handsComp))
        {
            return (false, null);
        }

        foreach (var hand in hands)
        {
            return (true, new Dictionary<string, object>()
            {
                {
                    NPCBlackboard.ActiveHand, hand
                },
                {
                    NPCBlackboard.ActiveHandFree, true
                },
            });
        }

        return (false, null);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        // TODO: Need interaction cooldown
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var handSystem = _entManager.System<HandsSystem>();

        // KS14: ANK: Changed logic for this to ACTUALLY WORK
        if (!_entManager.TryGetComponent<HandsComponent>(owner, out var handsComponent))
            return HTNOperatorStatus.Failed;

        foreach (var hand in handsComponent.Hands.Keys)
        {
            if (handSystem.TryGetHeldItem((owner, handsComponent), hand, out var heldUid) &&
                !_entManager.HasComponent<VirtualItemComponent>(heldUid)) // KS14: ANK: dont continue if theres only a virtual item
                continue;

            handSystem.SetActiveHand((owner, handsComponent), hand);
            return HTNOperatorStatus.Finished;
        }

        return HTNOperatorStatus.Failed;
    }
}
