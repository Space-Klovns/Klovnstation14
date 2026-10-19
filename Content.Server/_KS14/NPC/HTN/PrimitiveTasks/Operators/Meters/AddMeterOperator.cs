using Content.Server._KS14.NPC.Meters;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Meters;

/// <summary>
///     Raises the owner's <see cref="Meter"/> by <see cref="Amount"/> (negative to lower it) when the task runs - not
///         when it is planned.
/// </summary>
/// <example>
///     <code>
///     - !type:HTNPrimitiveTask # something nearly got us
///       operator: !type:AddMeterOperator
///         meter: KsCaution
///         amount: 20
///     </code>
/// </example>
public sealed partial class AddMeterOperator : HTNOperator
{
    [Dependency] private NpcMeterSystem _npcMeterSystem = default!;

    [DataField(required: true)]
    public ProtoId<NpcMeterPrototype> Meter;

    [DataField(required: true)]
    public float Amount;

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);
        _npcMeterSystem.Add(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), Meter, Amount);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        return HTNOperatorStatus.Finished;
    }
}
