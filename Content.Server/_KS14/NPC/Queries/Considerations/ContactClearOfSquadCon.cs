using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Squad;
using Content.Server.NPC;
using Content.Server.NPC.Queries.Considerations;
using Robust.Server.GameObjects;

namespace Content.Server._KS14.NPC.Queries.Considerations;

/// <summary>
///     1 if no squadmate of the owner is within <see cref="Radius"/> of where it believes a hostile to be, those that are
///         down included: somewhere safe to send a grenade. 0 if one is, or if it knows nothing of the hostile.
/// </summary>
public sealed partial class ContactClearOfSquadCon : UtilityConsideration
{
    [Dependency] private NpcPerceptionSystem _npcPerceptionSystem = default!;
    [Dependency] private NpcSquadSystem _npcSquadSystem = default!;
    [Dependency] private TransformSystem _transformSystem = default!;

    [DataField(required: true)]
    public float Radius;

    private readonly HashSet<EntityUid> _squadmates = new();

    public override float GetScore(NPCBlackboard blackboard, EntityUid ownerUid, EntityUid targetUid)
    {
        if (!_npcPerceptionSystem.TryGetBelievedCoordinates(ownerUid, targetUid, out var believedCoordinates, out _, out _))
            return 0f;

        var believedMapCoordinates = _transformSystem.ToMapCoordinates(believedCoordinates);

        _squadmates.Clear();
        _npcSquadSystem.GetSquadmates(ownerUid, _squadmates);

        foreach (var squadmateUid in _squadmates)
        {
            var squadmateMapCoordinates = _transformSystem.GetMapCoordinates(squadmateUid);

            if (squadmateMapCoordinates.MapId == believedMapCoordinates.MapId &&
                (squadmateMapCoordinates.Position - believedMapCoordinates.Position).LengthSquared() <= Radius * Radius)
                return 0f;
        }

        return 1f;
    }
}
