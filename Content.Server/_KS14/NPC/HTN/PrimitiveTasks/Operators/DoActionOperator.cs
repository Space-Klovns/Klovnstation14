using Content.Server.Actions;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Actions.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
///     Does an action, it's that simple!
///         Performs an action whose ID is that of the given <see cref="Id"/>. Validity
///         of the action (cooldown etc.) will be checked, but whether the target (if any)
///         can be reached will not be checked.
///
///     Optionally, a blackboard key to use as the target of the action may be specified,
///         and this op will fail if it doesn't exist.
/// </summary>
public sealed partial class DoActionOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly ActionsSystem _actionsSystem = default!;

    /// <summary>
    ///     Ent ID of the action to do.
    /// </summary>
    [DataField(required: true)] public EntProtoId Id;

    /// <summary>
    ///     If null, there will be no target. Defaults to null.
    /// </summary>
    [DataField] public string? TargetKey = null;

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        if (!blackboard.TryGetValue<EntityUid>(NPCBlackboard.Owner, out var ownerUid, _entityManager))
            return HTNOperatorStatus.Failed;

        Entity<ActionComponent> actionEntity = default;
        foreach (var otherActionEntity in _actionsSystem.GetActions(ownerUid))
        {
            if (_entityManager.GetComponent<MetaDataComponent>/* Can't EntityQuery a MetaDataComp here apparently */(otherActionEntity.Owner).EntityPrototype?.ID != Id.ToString())
                continue;

            actionEntity = otherActionEntity;
            break;
        }

        if (actionEntity.Owner == default ||
            !_actionsSystem.ValidAction(actionEntity, canReach: true))
            return HTNOperatorStatus.Failed;

        if (TargetKey != null)
        {
            if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var targetUid, _entityManager))
                return HTNOperatorStatus.Failed;

            _actionsSystem.SetEventTarget(actionEntity, targetUid);
        }

        _actionsSystem.PerformAction(ownerUid, actionEntity, predicted: false);
        return HTNOperatorStatus.Finished;
    }
}
