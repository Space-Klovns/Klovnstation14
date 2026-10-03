// Trauma/KS14: moved DestructibleComponent to shared
using Content.Server.NPC.Components;
using Content.Server.NPC.Pathfinding;
using Content.Shared.CombatMode;
using Content.Shared.Destructible;
using Content.Shared.DoAfter;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems; // KS14
using Content.Server._KS14.NPC.Doors; // KS14
using Content.Shared.NPC;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Utility;
using ClimbableComponent = Content.Shared.Climbing.Components.ClimbableComponent;
using ClimbingComponent = Content.Shared.Climbing.Components.ClimbingComponent;

namespace Content.Server.NPC.Systems;

public sealed partial class NPCSteeringSystem
{
    /*
     * For any custom path handlers, e.g. destroying walls, opening airlocks, etc.
     * Putting it onto steering seemed easier than trying to make a custom compound task for it.
     * I also considered task interrupts although the problem is handling stuff like pathfinding overlaps
     * Ideally we could do interrupts but that's TODO.
     */

    /*
     * TODO:
     * - Add path cap
     * - Circle cast BFS in LOS to determine targets.
     * - Store last known coordinates of X targets.
     * - Require line of sight for melee
     * - Add new behavior where they move to melee target's last known position (diffing theirs and current)
     *  then do the thing like from dishonored where it gets passed to a search system that opens random stuff.
     *
     * Also need to make sure it picks nearest obstacle path so it starts smashing in front of it.
     */

    [Dependency] private SharedDoorSystem _doorSystem = default!; // KS14
    [Dependency] private NpcDoorSystem _npcDoorSystem = default!; // KS14
    [Dependency] private EntityQuery<DoorComponent> _doorQuery = default!;
    [Dependency] private EntityQuery<ClimbableComponent> _climbableQuery = default!;
    [Dependency] private EntityQuery<DestructibleComponent /* Trauma/KS14: moved DestructibleComponent to shared */> _destructibleQuery = default!;

    private SteeringObstacleStatus TryHandleFlags(EntityUid uid, NPCSteeringComponent component, PathPoly poly)
    {
        DebugTools.Assert(!poly.Data.IsFreeSpace);
        // TODO: Store PathFlags on the steering comp
        // and be able to re-check it.

        var layer = 0;
        var mask = 0;

        if (TryComp<FixturesComponent>(uid, out var manager))
        {
            (layer, mask) = _physics.GetHardCollision(uid, manager);
        }
        else
        {
            return SteeringObstacleStatus.Failed;
        }

        // TODO: Should cache the fact we're doing this somewhere.
        // See https://github.com/space-wizards/space-station-14/issues/11475
        if ((poly.Data.CollisionLayer & mask) != 0x0 ||
            (poly.Data.CollisionMask & layer) != 0x0)
        {
            var id = component.DoAfterId;

            // Still doing what we were doing before.
            var doAfterStatus = _doAfter.GetStatus(id);

            switch (doAfterStatus)
            {
                case DoAfterStatus.Running:
                    return SteeringObstacleStatus.Continuing;
                case DoAfterStatus.Cancelled:
                    return SteeringObstacleStatus.Failed;
            }

            var obstacleEnts = new List<EntityUid>();

            GetObstacleEntities(poly, mask, layer, obstacleEnts);
            var isDoor = (poly.Data.Flags & PathfindingBreadcrumbFlag.Door) != 0x0;
            var isAccessRequired = (poly.Data.Flags & PathfindingBreadcrumbFlag.Access) != 0x0;
            var isClimbable = (poly.Data.Flags & PathfindingBreadcrumbFlag.Climb) != 0x0;

            // KS14 start: open any door we are allowed through, whether it opens by bumping or by clicking.
            //      Leaving bump-open doors to the NPC physically walking into them left NPCs standing in front of
            //      public airlocks, and it fell through to Failed below. TryOpen checks access, power and bolts.
            //      Upstream (and the old KS14 ANK version) split this on the Access flag instead:
            /*
            // Just walk into it stupid
            if (isDoor && !isAccessRequired)
            {
                // ... At least if it's not a bump open.
                foreach (var ent in obstacleEnts)
                {
                    if (!_doorQuery.TryGetComponent(ent, out var door))
                        continue;

                    if (!door.BumpOpen && (component.Flags & PathFlags.Interact) != 0x0)
                    {
                        if (door.State != DoorState.Opening)
                        {
                            _interaction.InteractionActivate(uid, ent);
                            return SteeringObstacleStatus.Continuing;
                        }
                    }
                }

                // If we get to here then didn't succeed for reasons.
            }
            */
            // A shut door here it could neither open nor force, if any: reported if nothing else gets it through.
            EntityUid? shutDoorUid = null;

            if (isDoor && (component.Flags & PathFlags.Interact) != 0x0)
            {
                foreach (var obstacleUid in obstacleEnts)
                {
                    if (!_doorQuery.TryGetComponent(obstacleUid, out var doorComponent))
                        continue;

                    // Forcing it already: wait for it to give.
                    if (_npcDoorSystem.IsBreaching(uid, obstacleUid))
                        return SteeringObstacleStatus.Continuing;

                    // Shutters and blast doors open from their buttons, never by hand. Prying is all that is left.
                    var openByHand = doorComponent.ClickOpen || doorComponent.BumpOpen;

                    // Opening or open: the navmesh still shows it shut until its chunk is rebuilt. Closing or
                    //      denying: it will be closed, and openable again, in a moment. Either way, wait rather
                    //      than fall through to prying or smashing it.
                    if (doorComponent.State is DoorState.Opening or DoorState.Open ||
                        openByHand && doorComponent.State is DoorState.Closing or DoorState.Denying)
                        return SteeringObstacleStatus.Continuing;

                    if (doorComponent.State != DoorState.Closed)
                        continue;

                    if (openByHand)
                    {
                        // Asked before trying: trying may change what it would say.
                        var believedOpenable = _npcDoorSystem.GetDoorAccess(uid, obstacleUid) == NpcDoorAccess.Openable;

                        if (_doorSystem.TryOpen(obstacleUid, doorComponent, uid, quiet: true))
                            return SteeringObstacleStatus.Continuing;

                        // Thought it could get through, and could not: it was fooled. See NpcDoorSystem.
                        if (believedOpenable)
                            _npcDoorSystem.ReportRefused(uid, obstacleUid);
                    }

                    // Shut to it, and the way it is going: force it with something it carries, if it does that.
                    if (_npcDoorSystem.TryBreachBlockingDoor(uid, obstacleUid))
                        return SteeringObstacleStatus.Continuing;

                    shutDoorUid ??= obstacleUid;
                }

                // Locked to us, bolted, unpowered or welded, with nothing to force it: fall through to prying or smashing.
            }
            // KS14 end

            if ((component.Flags & PathFlags.Prying) != 0x0 && isDoor)
            {
                // Get the relevant obstacle
                foreach (var ent in obstacleEnts)
                {
                    if (_doorQuery.TryGetComponent(ent, out var door) && door.State != DoorState.Open)
                    {
                        // TODO: Use the verb.

                        if (door.State != DoorState.Opening)
                            _pryingSystem.TryPry(ent, uid, out id, uid);

                        component.DoAfterId = id;
                        return SteeringObstacleStatus.Continuing;
                    }
                }

                if (obstacleEnts.Count == 0)
                    return SteeringObstacleStatus.Completed;
            }
            // Try climbing obstacles
            else if ((component.Flags & PathFlags.Climbing) != 0x0 && isClimbable)
            {
                if (TryComp<ClimbingComponent>(uid, out var climbing))
                {
                    if (climbing.IsClimbing)
                    {
                        return SteeringObstacleStatus.Completed;
                    }
                    else if (climbing.NextTransition != null)
                    {
                        return SteeringObstacleStatus.Continuing;
                    }

                    // Get the relevant obstacle
                    foreach (var ent in obstacleEnts)
                    {
                        if (_climbableQuery.TryGetComponent(ent, out var table) &&
                            _climb.CanVault(table, uid, uid, out _) &&
                            _climb.TryClimb(uid, uid, ent, out id, table, climbing))
                        {
                            component.DoAfterId = id;
                            return SteeringObstacleStatus.Continuing;
                        }
                    }
                }

                if (obstacleEnts.Count == 0)
                    return SteeringObstacleStatus.Completed;
            }
            // Try smashing obstacles.
            else if ((component.Flags & PathFlags.Smashing) != 0x0)
            {
                if (_melee.TryGetWeapon(uid, out _, out var meleeWeapon) && meleeWeapon.NextAttack <= _timing.CurTime && TryComp<CombatModeComponent>(uid, out var combatMode))
                {
                    _combat.SetInCombatMode(uid, true, combatMode);
                    // TODO: This is a hack around grilles and windows.
                    _random.Shuffle(obstacleEnts);
                    var attackResult = false;

                    foreach (var ent in obstacleEnts)
                    {
                        // TODO: Validate we can damage it
                        if (_destructibleQuery.HasComponent(ent))
                        {
                            attackResult = _melee.AttemptLightAttack(uid, uid, meleeWeapon, ent);
                            break;
                        }
                    }

                    _combat.SetInCombatMode(uid, false, combatMode);

                    // Blocked or the likes?
                    if (!attackResult)
                        return FailAtDoor(uid, shutDoorUid); // KS14: Failed -> FailAtDoor

                    if (obstacleEnts.Count == 0)
                        return SteeringObstacleStatus.Completed;

                    return SteeringObstacleStatus.Continuing;
                }
            }

            return FailAtDoor(uid, shutDoorUid); // KS14: Failed -> FailAtDoor
        }

        return SteeringObstacleStatus.Completed;
    }

    // KS14 start
    /// <summary>
    ///     Gives up on the way through, remembering the door that stopped it, if one did, so that its next path goes
    ///         round that door rather than back into it. See <see cref="NpcDoorSystem.ReportBlocked"/>.
    /// </summary>
    private SteeringObstacleStatus FailAtDoor(EntityUid uid, EntityUid? shutDoorUid)
    {
        if (shutDoorUid is { } doorUid)
            _npcDoorSystem.ReportBlocked(uid, doorUid);

        return SteeringObstacleStatus.Failed;
    }
    // KS14 end

    private void GetObstacleEntities(PathPoly poly, int mask, int layer, List<EntityUid> ents)
    {
        // TODO: Can probably re-use this from pathfinding or something
        if (!TryComp<MapGridComponent>(poly.GraphUid, out var grid))
        {
            return;
        }

        foreach (var ent in _mapSystem.GetLocalAnchoredEntities(poly.GraphUid, grid, poly.Box))
        {
            if (!_physicsQuery.TryGetComponent(ent, out var body) ||
                !body.Hard ||
                !body.CanCollide ||
                (body.CollisionMask & layer) == 0x0 && (body.CollisionLayer & mask) == 0x0)
            {
                continue;
            }

            ents.Add(ent);
        }
    }

    private enum SteeringObstacleStatus : byte
    {
        Completed,
        Failed,
        Continuing
    }
}
