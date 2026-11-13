using Content.Server._KS14.NPC.LineOfFire;
using Content.Server.NPC;
using Content.Server.NPC.Queries.Considerations;
using Robust.Server.GameObjects;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Queries.Considerations;

/// <summary>
///     1 if something the owner throws would reach the spot it believes a hostile to be at. 0 if something solid is in the
///         way, or if it knows nothing of the hostile. Not <see cref="ContactInLOSCon"/>: glass can be seen through, and a
///         grenade thrown at it comes down on the thrower's side. A grenade is as wide as it is
///         (<see cref="NpcLineOfFireSystem.IsThrowClear"/>), so one that would clip a corner or a door frame on the way does
///         not count.
/// </summary>
public sealed partial class ContactThrowClearCon : KsBelievedContactCon
{
    [Dependency] private NpcLineOfFireSystem _npcLineOfFireSystem = default!;
    [Dependency] private TransformSystem _transformSystem = default!;

    protected override float GetBelievedScore(EntityUid ownerUid, EntityUid targetUid, EntityCoordinates believedCoordinates)
    {
        var ownerMapCoordinates = _transformSystem.GetMapCoordinates(ownerUid);
        var believedMapCoordinates = _transformSystem.ToMapCoordinates(believedCoordinates);
        if (ownerMapCoordinates.MapId != believedMapCoordinates.MapId)
            return 0f;

        return _npcLineOfFireSystem.IsThrowClear(ownerMapCoordinates, believedMapCoordinates) ? 1f : 0f;
    }
}
