using System.Diagnostics.CodeAnalysis;
using Content.Server.Actions;
using Content.Server.NPC;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Actions;

/// <summary>
///     Does an action, it's that simple!
///         Performs an action whose ID is that of the given <see cref="Id"/>. Validity
///         of the action (cooldown etc.) will be checked, but <b>whether the target (if any)
///         can be reached will not be checked</b>.
///
///     Optionally, a blackboard key to use as the target of the action may be specified,
///         and this op will fail if it doesn't exist.
/// </summary>
public abstract partial class KsBaseActionOperator : HTNOperator
{
    [Dependency] protected IEntityManager EntityManager = default!;
    [Dependency] protected ActionsSystem ActionsSystem = default!;

    /// <summary>
    ///     Ent ID of the action to do.
    /// </summary>
    [DataField(required: true)] public EntProtoId Id;

    protected bool TryGetValidAction(NPCBlackboard blackboard, [NotNullWhen(true)] out EntityUid ownerUid, [NotNullWhen(true)] out Entity<ActionComponent> actionEntity)
    {
        actionEntity = default;

        if (!blackboard.TryGetOwner(EntityManager, out ownerUid))
            return false;

        foreach (var otherActionEntity in ActionsSystem.GetActions(ownerUid))
        {
            if (EntityManager.GetComponent<MetaDataComponent>/* Can't EntityQuery a MetaDataComp here apparently */(otherActionEntity.Owner).EntityPrototype?.ID != Id.ToString())
                continue;

            actionEntity = otherActionEntity;
            break;
        }

        if (actionEntity.Owner == default ||
            !ActionsSystem.ValidAction(actionEntity, canReach: true))
            return false;

        return true;
    }

    /// <summary>
    ///     Performs the action <see cref="Id"/> on the owner, targeted at <paramref name="targetCoordinates"/>. False if
    ///         the owner has no valid such action. If its event is not a <see cref="WorldTargetActionEvent"/>, that is
    ///         a mistake in the prototype: it throws if <paramref name="throwOnWrongEvent"/>, or else counts as no valid
    ///         action.
    /// </summary>
    protected bool TryPerformWorldAction(NPCBlackboard blackboard, EntityCoordinates targetCoordinates, bool throwOnWrongEvent)
    {
        if (!TryGetValidAction(blackboard, out var ownerUid, out var actionEntity) ||
            ActionsSystem.GetEvent(actionEntity) is not { } actionEvent)
            return false;

        if (actionEvent is not WorldTargetActionEvent worldTargetActionEvent)
        {
            if (throwOnWrongEvent)
                throw new InvalidOperationException($"Expected event of type {actionEvent.GetType()} on entity {EntityManager.ToPrettyString(actionEntity)} on owner {EntityManager.ToPrettyString(ownerUid)} to be of type {nameof(WorldTargetActionEvent)}");

            return false;
        }

        worldTargetActionEvent.Target = targetCoordinates;
        ActionsSystem.PerformAction(ownerUid, actionEntity, actionEvent: worldTargetActionEvent, predicted: false);
        return true;
    }
}
