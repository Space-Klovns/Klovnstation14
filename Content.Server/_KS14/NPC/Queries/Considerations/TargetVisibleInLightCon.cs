using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Systems;
using Content.Server.NPC;
using Content.Server.NPC.Queries.Considerations;

namespace Content.Server._KS14.NPC.Queries.Considerations;

/// <summary>
///     1 if the target is lit well enough to be seen, or close enough to be seen anyway; 0 if it is hidden in the
///         dark. Always 1 while <see cref="NpcLightDetectionSystem"/> is off, so it can stay in queries regardless.
/// </summary>
/// <remarks>
///     No operative query uses it, and none needs to: they take their targets from <c>PerceivedContactsQuery</c>, and
///         <see cref="NpcPerceptionSystem"/> has already applied this same rule, with the same defaults, to anything it
///         returns - along with moving targets standing out, tracking one into the dark, and reacting slower in it. It is
///         for a query that does not go through perception, such as one built on <c>NearbyHostilesQuery</c>.
/// </remarks>
public sealed partial class TargetVisibleInLightCon : UtilityConsideration
{
    [Dependency] private NpcLightDetectionSystem _npcLightDetectionSystem = default!;
    [Dependency] private NpcPerceptionSystem _npcPerceptionSystem = default!;

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
        var lightLevel = _npcLightDetectionSystem.GetPerceivedLightLevel(ownerUid, targetUid, ProximityRange);
        return _npcPerceptionSystem.GetSeenLightLevel(ownerUid, lightLevel) >= MinimumLightLevel ? 1f : 0f;
    }
}
