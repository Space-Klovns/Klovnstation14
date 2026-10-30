using Content.Server._KS14.NPC.Meters;
using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.NPC.HTN.Preconditions.Meters;

/// <summary>
///     Met while the owner's <see cref="Meter"/> is at least <see cref="Min"/> and at most <see cref="Max"/>, either of
///         which may be left out. An NPC that has never had the meter raised is at 0.
/// </summary>
/// <example>
///     <code>
///     - !type:MeterPrecondition # only when cautious
///       meter: KsCaution
///       min: 30
///     </code>
/// </example>
public sealed partial class MeterPrecondition : HTNPrecondition
{
    [Dependency] private NpcMeterSystem _npcMeterSystem = default!;

    [DataField(required: true)]
    public ProtoId<NpcMeterPrototype> Meter;

    [DataField]
    public float? Min;

    [DataField]
    public float? Max;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        var value = _npcMeterSystem.GetValue(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), Meter);
        return (Min is not { } min || value >= min) && (Max is not { } max || value <= max);
    }
}
