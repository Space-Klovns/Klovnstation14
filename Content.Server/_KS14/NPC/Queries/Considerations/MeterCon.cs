using Content.Server._KS14.NPC.Meters;
using Content.Server.NPC;
using Content.Server.NPC.Queries.Considerations;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.NPC.Queries.Considerations;

/// <summary>
///     The owner's <see cref="Meter"/> as a share of its maximum, from 0 to 1: lets a meter weigh a utility choice. A
///         cautious NPC preferring positions close to its squad, say. The same for every target, so it moves scores
///         against other queries' rather than ordering targets.
/// </summary>
public sealed partial class MeterCon : UtilityConsideration
{
    [Dependency] private NpcMeterSystem _npcMeterSystem = default!;

    [DataField(required: true)]
    public ProtoId<NpcMeterPrototype> Meter;

    public override float GetScore(NPCBlackboard blackboard, EntityUid ownerUid, EntityUid targetUid)
    {
        return Math.Clamp(_npcMeterSystem.GetFraction(ownerUid, Meter), 0f, 1f);
    }
}
