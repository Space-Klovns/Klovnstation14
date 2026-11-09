using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Server._KS14.NPC.Hands;
using Content.Server._KS14.NPC.Squad;
using Content.Server._KS14.NPC.Systems;
using Content.Server.NPC.Components;
using Content.Server.NPC.Pathfinding;
using Content.Server._KS14.NPC.Components;
using Content.Server._KS14.NPC.Utility;
using Content.Shared.Access;
using Content.Shared.Access.Systems;
using Content.Shared.Charges.Components;
using Content.Shared.Charges.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Doors.Components;
using Content.Shared.Emag.Components;
using Content.Shared.Emag.Systems;
using Content.Shared.Inventory;
using Content.Shared.NPC;
using Content.Shared.Prying.Components;
using Content.Shared.Prying.Systems;
using Content.Shared.Storage;
using Content.Shared.Tag;
using Content.Shared.Timing;
using Content.Shared.Tools;
using Content.Shared.Tools.Components;
using Content.Shared.Tools.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Doors;

public sealed partial class NpcDoorSystem
{
    /// <summary>
    ///     Steering's first answer to a door in its way that is shut to <paramref name="npcUid"/> and that it could
    ///         force: go round it instead, if there is a way round. Forcing a door is loud and spends an access breaker's
    ///         charges, and the pathfinder sends an NPC through any door needing access whenever that saves enough
    ///         walking - it does not know who has the access - so without this, every shortcut through maintenance got
    ///         forced. Returns whether it is now going round: its paths avoid the door (see
    ///         <see cref="GetBlockedDoors"/>), and steering drops its path for one that does. If there is none, steering
    ///         says so with <see cref="GiveUpDetours"/>, the door becomes the only way, and the next time it is in the
    ///         way it is forced. So it does too when the way round is not worth taking: see
    ///         <see cref="IsDetourWorthTaking"/>.
    ///     <para>
    ///         With <paramref name="believedLocked"/>, it goes round a door it believes it cannot open even with nothing
    ///             to force it: it does not try the handle of a door it thinks is locked while there is another way. Once
    ///             the door is the only way (<see cref="IsOnlyWay"/>), it tries it, and finds out.
    ///     </para>
    /// </summary>
    public bool TryDetourAroundDoor(EntityUid npcUid, EntityUid doorUid, NPCSteeringComponent steeringComponent, bool believedLocked = false)
    {
        if (!believedLocked && !CanForceFromSteering(npcUid, doorUid, out _))
            return false;

        var doorUserComponent = EnsureComp<NpcDoorUserComponent>(npcUid);
        var now = _gameTiming.CurTime;

        if (NpcExpiringMap.IsLive(doorUserComponent.ForceableDoors, doorUid, now) ||
            NpcExpiringMap.IsLive(doorUserComponent.DetourDoors, doorUid, now))
            return false;

        // The way through is what any way round is held to; the first door's, for a way round several.
        if (!AnyRemembered(doorUserComponent.DetourDoors, now))
        {
            doorUserComponent.DetourBaseDistance = GetPathDistance(_transformSystem.GetMapCoordinates(npcUid),
                steeringComponent.CurrentPath,
                _transformSystem.ToMapCoordinates(steeringComponent.Coordinates));
        }

        Remember(doorUserComponent.DetourDoors, doorUid, now, doorUserComponent.DetourForgetAfter);
        return true;
    }

    /// <summary>
    ///     Whether a path steering found for <paramref name="npcUid"/>, while it goes round doors it could force, is a way
    ///         round worth taking. Not if it runs through another door shut to it - that is forcing a door all the same,
    ///         only further off, and going round that one too walked it all over the station until there was nowhere
    ///         left to go round, when it forced whichever it happened to be standing at - and not if it is more than
    ///         <see cref="NpcDoorUserComponent.MaxDetourExtraDistance"/> further than the way through. Steering gives up
    ///         going round (<see cref="GiveUpDetours"/>) and forces the door instead. Always true while it goes round
    ///         nothing.
    /// </summary>
    public bool IsDetourWorthTaking(EntityUid npcUid, MapCoordinates fromCoordinates, List<PathPoly> path, MapCoordinates targetCoordinates)
    {
        if (!_doorUserQuery.TryComp(npcUid, out var doorUserComponent) || !AnyRemembered(doorUserComponent.DetourDoors, _gameTiming.CurTime))
            return true;

        if (RunsThroughShutDoor(npcUid, path))
            return false;

        return GetPathDistance(fromCoordinates, path, targetCoordinates) <=
            doorUserComponent.DetourBaseDistance + doorUserComponent.MaxDetourExtraDistance;
    }

    /// <summary>
    ///     Whether <paramref name="path"/> goes through a door <paramref name="npcUid"/> cannot open by hand.
    /// </summary>
    public bool RunsThroughShutDoor(EntityUid npcUid, List<PathPoly> path)
    {
        // A door's tile is several polys in a row: each tile is looked at once.
        (EntityUid GridUid, Vector2i Tile)? lastTile = null;

        foreach (var poly in path)
        {
            if ((poly.Data.Flags & PathfindingBreadcrumbFlag.Door) == 0x0 ||
                !_mapGridQuery.TryComp(poly.GraphUid, out var mapGridComponent))
                continue;

            var tile = _mapSystem.TileIndicesFor(poly.GraphUid, mapGridComponent, poly.Coordinates);
            if (lastTile == (poly.GraphUid, tile))
                continue;

            lastTile = (poly.GraphUid, tile);

            var anchoredEnumerator = _mapSystem.GetAnchoredEntities(poly.GraphUid, mapGridComponent, tile);
            while (anchoredEnumerator.MoveNext(out var anchoredUid))
            {
                if (_doorQuery.HasComp(anchoredUid.Value) && GetDoorAccess(npcUid, anchoredUid.Value) == NpcDoorAccess.Locked)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     How far it is from <paramref name="fromCoordinates"/> along <paramref name="path"/> to
    ///         <paramref name="targetCoordinates"/>.
    /// </summary>
    private float GetPathDistance(MapCoordinates fromCoordinates, IEnumerable<PathPoly> path, MapCoordinates targetCoordinates)
    {
        var distance = 0f;
        var previousPosition = fromCoordinates.Position;

        foreach (var poly in path)
        {
            var position = _transformSystem.ToMapCoordinates(poly.Coordinates).Position;
            distance += Vector2.Distance(previousPosition, position);
            previousPosition = position;
        }

        return distance + Vector2.Distance(previousPosition, targetCoordinates.Position);
    }

    /// <summary>
    ///     Whether <paramref name="doorUid"/> was found to be the only way for <paramref name="npcUid"/>: going round it
    ///         left no path, or none worth taking. See <see cref="TryDetourAroundDoor"/>.
    /// </summary>
    public bool IsOnlyWay(EntityUid npcUid, EntityUid doorUid)
    {
        return _doorUserQuery.TryComp(npcUid, out var doorUserComponent) &&
            NpcExpiringMap.IsLive(doorUserComponent.ForceableDoors, doorUid, _gameTiming.CurTime);
    }

    /// <summary>
    ///     Steering found no path for <paramref name="npcUid"/>. If it was going round doors it could force, they are
    ///         the only way: it stops going round them and forces them instead. Returns whether it was, in which case
    ///         steering asks again, through them.
    /// </summary>
    public bool GiveUpDetours(EntityUid npcUid)
    {
        if (!_doorUserQuery.TryComp(npcUid, out var doorUserComponent) || doorUserComponent.DetourDoors.Count == 0)
            return false;

        var now = _gameTiming.CurTime;
        var gaveUp = false;
        NpcExpiringMap.PruneExpired(doorUserComponent.ForceableDoors, now);

        foreach (var (doorUid, expiresAt) in doorUserComponent.DetourDoors)
        {
            if (expiresAt <= now)
                continue;

            doorUserComponent.ForceableDoors[doorUid] = now + doorUserComponent.DetourForgetAfter;
            gaveUp = true;
        }

        doorUserComponent.DetourDoors.Clear();
        return gaveUp;
    }
}
