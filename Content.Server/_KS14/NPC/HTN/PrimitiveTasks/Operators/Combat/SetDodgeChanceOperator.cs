using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared._KS14.GunDodger;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Combat;

/// <summary>
///     Sets the owner's <see cref="GunDodgerComponent.DodgeChance"/>, e.g. when a boss changes phase.
/// </summary>
public sealed partial class SetDodgeChanceOperator : HTNOperator
{
    [Dependency] private GunDodgerSystem _gunDodgerSystem = default!;

    /// <summary>
    ///     Chance, from 0 to 1, of dodging a given shot.
    /// </summary>
    [DataField(required: true)]
    public float DodgeChance;

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        _gunDodgerSystem.SetDodgeChance(owner, DodgeChance);
        return HTNOperatorStatus.Finished;
    }
}
