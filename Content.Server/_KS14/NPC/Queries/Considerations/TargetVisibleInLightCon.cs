using Content.Server._KS14.NPC.Systems;
using Content.Server.NPC;
using Content.Server.NPC.Queries.Considerations;

namespace Content.Server._KS14.NPC.Queries.Considerations;

/// <summary>
///     1 if the target is lit well enough to be seen, or close enough to be seen anyway; 0 if it is hidden in the
///         dark. Always 1 while <see cref="NpcLightDetectionSystem"/> is off, so it can stay in queries regardless.
/// </summary>
public sealed partial class TargetVisibleInLightCon : UtilityConsideration
{
    [Dependency] private NpcLightDetectionSystem _npcLightDetectionSystem = default!;

    /// <summary>
    ///     The least light, from 0 to 1, that a target further away than <see cref="ProximityRange"/> needs to be
    ///         noticed at all.
    /// </summary>
    [DataField] public float MinimumLightLevel = 0.03f;

    /// <summary>
    ///     Within this many tiles, a target is seen however dark it is.
    /// </summary>
    [DataField] public float ProximityRange = 2.5f;

    public override float GetScore(NPCBlackboard blackboard, EntityUid ownerUid, EntityUid targetUid)
    {
        return _npcLightDetectionSystem.GetPerceivedLightLevel(ownerUid, targetUid, ProximityRange) >= MinimumLightLevel
            ? 1f
            : 0f;
    }
}
