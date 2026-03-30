using Content.Server.Hands.Systems;
using Content.Shared.Hands.Components;

namespace Content.Server.NPC.HTN.PrimitiveTasks.Operators.Interactions;

/// <summary>
/// Drops the active hand entity underneath us.
/// </summary>
public sealed partial class DropOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly HandsSystem _handsSystem = default!; // KS14: ANK

    // KS14: ANK
    /// <summary>
    ///     Normally this fails if nothing is in the hand.
    /// </summary>
    [DataField] public bool SucceedIfHandEmpty = false;

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        if (!blackboard.TryGetValue(NPCBlackboard.ActiveHand, out string? activeHand, _entManager))
        {
            return HTNOperatorStatus.Finished;
        }

        var owner = blackboard.GetValueOrDefault<EntityUid>(NPCBlackboard.Owner, _entManager);
        // TODO: Need some sort of interaction cooldown probably.

        // KS14: ANK
        if (SucceedIfHandEmpty &&
            _handsSystem.HandIsEmpty(owner, activeHand))
            return HTNOperatorStatus.Finished;

        if (_handsSystem.TryDrop(owner))
        {
            return HTNOperatorStatus.Finished;
        }

        return HTNOperatorStatus.Failed;
    }
}
