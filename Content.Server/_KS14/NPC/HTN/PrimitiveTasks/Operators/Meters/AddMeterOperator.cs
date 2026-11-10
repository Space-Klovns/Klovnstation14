using Content.Server._KS14.NPC.Meters;
using Content.Server.NPC;
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
public sealed partial class AddMeterOperator : KsInstantOperator
{
    [Dependency] private NpcMeterSystem _npcMeterSystem = default!;

    [DataField(required: true)]
    public ProtoId<NpcMeterPrototype> Meter;

    [DataField(required: true)]
    public float Amount;

    protected override void OnStartup(EntityUid ownerUid, NPCBlackboard blackboard)
    {
        _npcMeterSystem.Add(ownerUid, Meter, Amount);
    }
}
