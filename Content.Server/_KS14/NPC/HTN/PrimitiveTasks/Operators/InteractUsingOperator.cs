using System.Threading;
using System.Threading.Tasks;
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
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private CombatModeSystem _combatModeSystem = default!;
    [Dependency] private InteractionSystem _interactionSystem = default!;
    [Dependency] private HandsSystem _handsSystem = default!;

    /// <summary>
    ///     Key that contains the target entity.
    /// </summary>
    [DataField(required: true)] public string TargetKey;

    /// <remarks>
    ///     Only the target is checked: the item to use is usually equipped by an earlier task in the same plan,
    ///         which has no planning effects to say so.
    /// </remarks>
    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var targetUid, _entManager) ||
            !_entManager.EntityExists(targetUid))
        {
            return (false, null);
        }

        return (true, null);
    }

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
