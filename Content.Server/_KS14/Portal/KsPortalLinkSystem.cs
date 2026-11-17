using System.Linq;
using Content.Shared._KS14.Portal;
using Content.Shared.Teleportation.Components;
using Content.Shared.Teleportation.Systems;

namespace Content.Server._KS14.Portal;

/// <summary>
///     Links portals one-to-one, for <see cref="KsPortalLinkCommand"/> and anything else that wants the same
///         guarantee.
/// </summary>
public sealed partial class KsPortalLinkSystem : EntitySystem
{
    [Dependency] private LinkedEntitySystem _linkedEntitySystem = default!;
    [Dependency] private EntityQuery<LinkedEntityComponent> _linkedQuery = default!;

    /// <summary>
    ///     Links the two portals to each other and to nothing else, dropping whatever links either had.
    /// </summary>
    public bool TryLinkExclusive(EntityUid firstUid, EntityUid secondUid)
    {
        if (firstUid == secondUid || !HasComp<KsPortalViewComponent>(firstUid) || !HasComp<KsPortalViewComponent>(secondUid))
            return false;

        UnlinkAll(firstUid);
        UnlinkAll(secondUid);

        return _linkedEntitySystem.TryLink(firstUid, secondUid, deleteOnEmptyLinks: false);
    }

    /// <summary>
    ///     Removes every link the entity has, without deleting anything.
    /// </summary>
    public void UnlinkAll(EntityUid uid)
    {
        if (!_linkedQuery.TryComp(uid, out var linkedComponent))
            return;

        foreach (var linkedUid in linkedComponent.LinkedEntities.ToArray())
        {
            // Unlinking a portal that was made to delete on empty would delete it, which an admin re-linking
            //      it does not want. Re-adding an existing link in each direction only clears that flag.
            _linkedEntitySystem.OneWayLink(uid, linkedUid, deleteOnEmptyLinks: false);
            _linkedEntitySystem.OneWayLink(linkedUid, uid, deleteOnEmptyLinks: false);

            _linkedEntitySystem.TryUnlink(uid, linkedUid);
        }
    }
}
