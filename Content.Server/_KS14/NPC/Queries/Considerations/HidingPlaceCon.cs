using Content.Server._KS14.NPC.Hiding;
using Content.Server.NPC;
using Content.Server.NPC.Queries.Considerations;

namespace Content.Server._KS14.NPC.Queries.Considerations;

/// <summary>
///     1 for a closet the owner could hide in (<see cref="NpcHidingSystem.IsHidingPlace"/>) within its
///         <see cref="NpcClosetHealerComponent.SearchRange"/>, 0 otherwise.
/// </summary>
public sealed partial class HidingPlaceCon : UtilityConsideration
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private NpcHidingSystem _npcHidingSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    public override float GetScore(NPCBlackboard blackboard, EntityUid ownerUid, EntityUid targetUid)
    {
        if (!_entityManager.TryGetComponent<NpcClosetHealerComponent>(ownerUid, out var closetHealerComponent) ||
            !_npcHidingSystem.IsHidingPlace(targetUid))
            return 0f;

        var ownerCoordinates = _transformSystem.GetMapCoordinates(ownerUid);
        var targetCoordinates = _transformSystem.GetMapCoordinates(targetUid);

        return ownerCoordinates.MapId == targetCoordinates.MapId &&
            (ownerCoordinates.Position - targetCoordinates.Position).Length() <= closetHealerComponent.SearchRange
                ? 1f
                : 0f;
    }
}
