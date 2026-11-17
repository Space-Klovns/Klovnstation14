using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Utility;
using Content.Server.NPC;
using Content.Server.NPC.Queries.Considerations;
using Robust.Server.GameObjects;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Queries.Considerations;

/// <summary>
///     1 if the owner can see the spot it believes a hostile to be at, whether or not the hostile is really there. 0 if
///         not, or if it knows nothing of the hostile.
/// </summary>
/// <remarks>
///     A hostile in sight is where it is believed to be, and perception has just found a line to it, smoke and all; only
///         how far off it is is checked then. Anything else costs a ray.
/// </remarks>
public sealed partial class ContactInLOSCon : KsBelievedContactCon
{
    [Dependency] private NpcLineOfSightSystem _npcLineOfSightSystem = default!;
    [Dependency] private TransformSystem _transformSystem = default!;

    [DataField(required: true)]
    public float Radius;

    protected override float GetBelievedScore(EntityUid ownerUid, EntityUid targetUid, EntityCoordinates believedCoordinates)
    {
        var range = Radius + 0.5f;
        var ownerCoordinates = _transformSystem.GetMapCoordinates(ownerUid);
        var believedMapCoordinates = _transformSystem.ToMapCoordinates(believedCoordinates);

        if (NpcPerceptionSystem.IsVisible(ownerUid, targetUid))
            return NpcCoordinates.IsWithin(ownerCoordinates, believedMapCoordinates, range) ? 1f : 0f;

        return _npcLineOfSightSystem.InLineOfSight(ownerCoordinates, believedMapCoordinates, range) ? 1f : 0f;
    }
}
