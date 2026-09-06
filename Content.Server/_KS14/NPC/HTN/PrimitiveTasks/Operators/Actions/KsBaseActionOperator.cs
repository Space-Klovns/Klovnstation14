using System.Diagnostics.CodeAnalysis;
using Content.Server.Actions;
using Content.Server.NPC;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Actions.Components;
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
        if (!blackboard.TryGetValue(NPCBlackboard.Owner, out ownerUid, EntityManager))
        {
            actionEntity = default;
            return false;
        }

        actionEntity = default;
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
}
