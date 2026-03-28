using Content.Server.CombatMode;
using Content.Server.Hands.Systems;
using Content.Server.Interaction;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.CombatMode;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
///     Uses thing in active hand on the target entity.
/// </summary>
public sealed partial class InteractUsingOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly CombatModeSystem _combatModeSystem = default!;
    [Dependency] private readonly InteractionSystem _interactionSystem = default!;
    [Dependency] private readonly HandsSystem _handsSystem = default!;

    /// <summary>
    ///     Key that contains the target entity.
    /// </summary>
    [DataField(required: true)] public string TargetKey;

    // TODO LCDC HTN: Make plan for this (its quite ez)

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var ownerUid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_handsSystem.TryGetActiveItem(ownerUid, out var itemUid))
            return HTNOperatorStatus.Failed;

        if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var targetUid, _entManager) ||
            !_entManager.TryGetComponent(targetUid, out TransformComponent? targetTransformComponent))
        {
            return HTNOperatorStatus.Failed;
        }

        if (_entManager.TryGetComponent<CombatModeComponent>(ownerUid, out var combatMode))
            _combatModeSystem.SetInCombatMode(ownerUid, false, combatMode);

        _interactionSystem.InteractUsing(ownerUid, itemUid.Value, targetUid, targetTransformComponent.Coordinates);
        return HTNOperatorStatus.Finished;
    }
}
