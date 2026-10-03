using System.Threading;
using System.Threading.Tasks;
using Content.Server.Hands.Systems;
using Content.Server._KS14.NPC.Hands; // KS14
using Content.Shared.Hands.Components;
using Content.Shared.Inventory.VirtualItem;

namespace Content.Server.NPC.HTN.PrimitiveTasks.Operators.Interactions;


/// <summary>
/// Swaps to any free hand.
/// </summary>
public sealed partial class SwapToFreeHandOperator : HTNOperator
{
    [Dependency] private IEntityManager _entManager = default!;

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

        // KS14 start: the one way NPCs free a hand - an empty one, or one holding a wield's virtual item, which is dropped
        //      so the hand is empty this tick rather than once the wield's queued deletion of it goes through. See
        //      NpcHandsSystem.
        var npcHandsSystem = _entManager.System<NpcHandsSystem>();
        return npcHandsSystem.TryFreeHand(owner, out var hand) && npcHandsSystem.MakeActive(owner, hand)
            ? HTNOperatorStatus.Finished
            : HTNOperatorStatus.Failed;
        // KS14 end
    }
}
