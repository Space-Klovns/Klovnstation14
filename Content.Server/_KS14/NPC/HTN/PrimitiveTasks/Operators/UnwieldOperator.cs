using Content.Server.Hands.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.Wieldable;
using Content.Shared.Wieldable.Components;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
///     Tries to free a hand by unwielding.
/// </summary>
public sealed partial class UnwieldOperator : HTNOperator
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private WieldableSystem _wieldableSystem = default!;
    [Dependency] private HandsSystem _handsSystem = default!;
    [Dependency] private EntityQuery<WieldableComponent> _wieldableQuery = default!;

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var ownerUid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!blackboard.TryGetValue<string>(NPCBlackboard.ActiveHand, out var activeHand, _entityManager) ||
            !_handsSystem.TryGetHeldItem(ownerUid, activeHand, out var weaponUid))
            return HTNOperatorStatus.Failed;

        if (!_wieldableQuery.TryGetComponent(weaponUid, out var wieldableComponent) ||
            !wieldableComponent.Wielded)
            return HTNOperatorStatus.Finished;

        return _wieldableSystem.TryUnwield(weaponUid.Value, wieldableComponent, ownerUid) ? HTNOperatorStatus.Finished : HTNOperatorStatus.Failed;
    }
}
