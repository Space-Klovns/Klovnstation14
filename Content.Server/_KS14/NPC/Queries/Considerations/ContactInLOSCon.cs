using Content.Server._KS14.NPC.Perception;
using Content.Server.NPC;
using Content.Server.NPC.Queries.Considerations;
using Robust.Server.GameObjects;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Queries.Considerations;

/// <summary>
///     1 if the owner can see the spot it believes a hostile to be at, whether or not the hostile is really there. 0 if
///         not, or if it knows nothing of the hostile.
/// </summary>
public sealed partial class ContactInLOSCon : KsBelievedContactCon
{
    [Dependency] private NpcLineOfSightSystem _npcLineOfSightSystem = default!;
    [Dependency] private TransformSystem _transformSystem = default!;

    [DataField(required: true)]
    public float Radius;

    protected override float GetBelievedScore(EntityUid ownerUid, EntityCoordinates believedCoordinates)
    {
        return _npcLineOfSightSystem.InLineOfSight(
            _transformSystem.GetMapCoordinates(ownerUid),
            _transformSystem.ToMapCoordinates(believedCoordinates),
            Radius + 0.5f) ? 1f : 0f;
    }
}
