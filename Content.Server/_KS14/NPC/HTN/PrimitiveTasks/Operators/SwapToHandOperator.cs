using System.Threading;
using System.Threading.Tasks;
using Content.Server.Hands.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Hands.Components;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
///     Swaps to the hand id at the given key.
/// </summary>
public sealed partial class SwapToHandOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly HandsSystem _handsSystem = default!;

    //[Dependency] private readonly EntityQuery<HandsComponent> _handsQuery = default;

    /// <summary>
    ///     Key of ID of the hand.
    /// </summary>
    [DataField(required: true)] public string Key = "";

    [DataField] public bool ProbablyFree = false; // TODO LCDC HTN: make this proper

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        if (!blackboard.TryGetValue<string>(Key, out var handId, _entityManager) ||
            !blackboard.TryGetValue<EntityUid>(NPCBlackboard.Owner, out var ownerUid, _entityManager) ||
            !_entityManager.TryGetComponent<HandsComponent>(ownerUid, out var handsComponent))
        {
            return (false, null);
        }

        return (true, new Dictionary<string, object>()
        {
            {
                NPCBlackboard.ActiveHand, handId
            },
            {
                NPCBlackboard.ActiveHandFree, _handsSystem.HandIsEmpty((ownerUid, handsComponent), handId)
            },
        });
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var ownerUid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_handsSystem.TrySetActiveHand(ownerUid, blackboard.GetValue<string>(Key)))
            return HTNOperatorStatus.Failed;

        return HTNOperatorStatus.Finished;
    }
}
