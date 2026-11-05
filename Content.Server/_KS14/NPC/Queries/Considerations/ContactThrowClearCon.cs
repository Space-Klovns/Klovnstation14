using Content.Server._KS14.NPC.Perception;
using Content.Server.NPC;
using Content.Server.NPC.Queries.Considerations;
using Content.Shared.Physics;
using Robust.Server.GameObjects;

namespace Content.Server._KS14.NPC.Queries.Considerations;

/// <summary>
///     1 if something the owner throws would reach the spot it believes a hostile to be at. 0 if something solid is in the
///         way, or if it knows nothing of the hostile. Not <see cref="ContactInLOSCon"/>: glass can be seen through, and a
///         grenade thrown at it comes down on the thrower's side.
/// </summary>
public sealed partial class ContactThrowClearCon : UtilityConsideration
{
    [Dependency] private NpcLineOfSightSystem _npcLineOfSightSystem = default!;
    [Dependency] private NpcPerceptionSystem _npcPerceptionSystem = default!;
    [Dependency] private TransformSystem _transformSystem = default!;

    public override float GetScore(NPCBlackboard blackboard, EntityUid ownerUid, EntityUid targetUid)
    {
        if (!_npcPerceptionSystem.TryGetBelievedCoordinates(ownerUid, targetUid, out var believedCoordinates, out _, out _))
            return 0f;

        var ownerMapCoordinates = _transformSystem.GetMapCoordinates(ownerUid);
        var believedMapCoordinates = _transformSystem.ToMapCoordinates(believedCoordinates);
        if (ownerMapCoordinates.MapId != believedMapCoordinates.MapId)
            return 0f;

        var offset = believedMapCoordinates.Position - ownerMapCoordinates.Position;
        var distance = offset.Length();

        return _npcLineOfSightSystem.GetClearDistance(ownerMapCoordinates, offset, distance, (int) CollisionGroup.ItemMask) >= distance ? 1f : 0f;
    }
}
