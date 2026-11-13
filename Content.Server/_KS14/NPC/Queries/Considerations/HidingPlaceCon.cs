using Content.Server._KS14.NPC.Hiding;
using Content.Server._KS14.NPC.Utility;
using Content.Server.NPC;
using Content.Server.NPC.Queries.Considerations;

namespace Content.Server._KS14.NPC.Queries.Considerations;

/// <summary>
///     1 for a closet the owner could hide in (<see cref="NpcHidingSystem.IsHidingPlace"/>) within its
///         <see cref="NpcClosetHealerComponent.SearchRange"/>, 0 otherwise. Scored for every storage in sight, so the
///         distance goes first: most are too far off for the rest to matter.
/// </summary>
public sealed partial class HidingPlaceCon : UtilityConsideration
{
    [Dependency] private NpcHidingSystem _npcHidingSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<NpcClosetHealerComponent> _closetHealerQuery = default!;

    public override float GetScore(NPCBlackboard blackboard, EntityUid ownerUid, EntityUid targetUid)
    {
        if (!_closetHealerQuery.TryComp(ownerUid, out var closetHealerComponent))
            return 0f;

        var ownerCoordinates = _transformSystem.GetMapCoordinates(ownerUid);
        var targetCoordinates = _transformSystem.GetMapCoordinates(targetUid);

        return NpcCoordinates.IsWithin(ownerCoordinates, targetCoordinates, closetHealerComponent.SearchRange) &&
            _npcHidingSystem.IsHidingPlace(targetUid)
                ? 1f
                : 0f;
    }
}
