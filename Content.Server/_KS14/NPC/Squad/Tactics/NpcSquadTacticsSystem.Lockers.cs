using System.Numerics;
using Content.Server._KS14.NPC.Suppression;
using Content.Shared._KS14.NPC;

namespace Content.Server._KS14.NPC.Squad.Tactics;

/// <summary>
///     Breaking open a locker the hostile is believed to be in, when nobody can open it: locked, or welded shut. A member with a
///         breaching charge that sticks to it puts that on it (<see cref="NpcSquadTacticsSettings.SearchChargeLockers"/>), as
///         on a door; otherwise, or if that does not work, it is shot open, as the glass in the way of a plan is
///         (<see cref="NpcSquadTacticsSettings.SearchBreakLockers"/>) - so long as it is worth it.
/// </summary>
public sealed partial class NpcSquadTacticsSystem
{
    [Dependency] private NpcSuppressiveFireSystem _npcSuppressiveFireSystem = default!;

    /// <summary>
    ///     How far, in steps' walk, a member with a charge may be from a locker to be the one to put it on.
    /// </summary>
    private const int LockerChargeWalk = 16;

    /// <summary>
    ///     Gives <paramref name="memberUid"/>, sent to <paramref name="point"/>, the orders to break it open if it is a locker to
    ///         break: a charge on it if anyone has one, else up to it and fire at it from where nothing else is in the way.
    ///         Returns whether it is one, and the orders are given; otherwise it is to be opened as any other.
    /// </summary>
    private bool TryOrderLockerBreak(EntityUid memberUid,
        EntityUid issuerUid,
        NpcHunt hunt,
        NpcHuntSearchPoint point,
        NpcSquadTacticsSettings settings,
        TimeSpan now)
    {
        if (point.Kind != NpcSearchPointKind.Locker ||
            point.StorageUid is not { } lockerUid ||
            !_entityStorageQuery.TryComp(lockerUid, out var storageComponent) ||
            storageComponent.Open ||
            !settings.SearchBreakLockers && !settings.SearchChargeLockers ||
            !IsBelievedIn(hunt, lockerUid) ||
            CanAnyoneOpen(lockerUid))
            return false;

        // The search picks up again from here once it has gone off, or not.
        if (settings.SearchChargeLockers && TryStartLockerBlast(hunt, lockerUid, now))
            return true;

        if (!settings.SearchBreakLockers ||
            !_gunSystem.TryGetGun(memberUid, out var gunEntity) ||
            GetAmmo(gunEntity.Owner).Count <= 0 ||
            !IsWorthShootingOpen(lockerUid, settings))
            return false;

        // Where it stands, not on it: shot open, it is gone, and the order with it.
        var lockerCoordinates = Transform(lockerUid).Coordinates;
        var memberMapCoordinates = _transformSystem.GetMapCoordinates(memberUid);
        var lockerMapCoordinates = _transformSystem.GetMapCoordinates(lockerUid);

        var inRange = memberMapCoordinates.MapId == lockerMapCoordinates.MapId &&
            (lockerMapCoordinates.Position - memberMapCoordinates.Position).Length() <= settings.SearchBreakLockerRange;

        if (inRange &&
            _npcLineOfFireSystem.TryGetFirstHit(memberMapCoordinates, lockerMapCoordinates, _npcLineOfFireSystem.GetShotMask(memberUid), out var hit) &&
            hit.HitEntity == lockerUid)
        {
            // Over combat, as for glass: one that can see the hostile once it is out has no business with lockers by then.
            if (_givingPlanOrders)
            {
                SetOrder(memberUid, issuerUid, NpcOrderKind.BreakBarrier, lockerCoordinates, Angle.Zero, OrderRange, lockerUid, now);
            }
            else
            {
                using var planOrders = GivePlanOrders();
                SetOrder(memberUid, issuerUid, NpcOrderKind.BreakBarrier, lockerCoordinates, Angle.Zero, OrderRange, lockerUid, now);
            }

            return true;
        }

        // Up to it first, as near as it would get to open it, but no nearer than it needs to be to shoot.
        var approachRange = MathF.Max(LockerOrderRange, settings.SearchBreakLockerRange - 1f);
        SetOrder(memberUid, issuerUid, NpcOrderKind.Search, lockerCoordinates, Angle.Zero, approachRange, null, now);
        return true;
    }

    /// <summary>
    ///     Whether <paramref name="lockerUid"/> is still worth shooting: it breaks, for no more damage than
    ///         <see cref="NpcSquadTacticsSettings.SearchBreakLockerDamage"/>, and NPCs have not yet put
    ///         <see cref="NpcSquadTacticsSettings.SearchBreakLockerRounds"/> into it. Every round fired at it on a
    ///         <see cref="NpcOrderKind.BreakBarrier"/> order counts (<c>SuppressOperator.barrierKey</c>).
    /// </summary>
    private bool IsWorthShootingOpen(EntityUid lockerUid, NpcSquadTacticsSettings settings)
    {
        return _npcSuppressiveFireSystem.GetRoundsTaken(lockerUid) < settings.SearchBreakLockerRounds &&
            TryGetBreakThreshold(lockerUid, out var threshold) &&
            threshold.Float() <= settings.SearchBreakLockerDamage;
    }

    /// <summary>
    ///     Has a member that carries a charge that sticks to <paramref name="lockerUid"/> put it on, the rest get clear, and set it
    ///         off, as for a door (<see cref="RunCharge"/>), with the search on hold until then. Once per locker, and never on a
    ///         plan's own hunt, which has charge steps of its own and nothing that runs a blast on it.
    /// </summary>
    private bool TryStartLockerBlast(NpcHunt hunt, EntityUid lockerUid, TimeSpan now)
    {
        if (hunt.AllowCharges || hunt.Blast != null || !AnyoneHasACharge() || !hunt.ChargedLockers.Add(lockerUid))
            return false;

        var plan = new NpcSquadPlan
        {
            TargetUid = hunt.TargetUid ?? EntityUid.Invalid,
            StartedAt = now,
            StepStartedAt = now,
            State = NpcSquadPlanState.Running,
            Hunt = hunt,
            MemberCount = _members.Count,
        };

        // A hunt with no room has no grid, and a charge goes by one: the locker's, for as long as the blast lasts.
        var lentGrid = hunt.GridUid == null;
        if (lentGrid)
            hunt.GridUid = Transform(lockerUid).GridUid;

        plan.DoorCharge = FindLockerCharge(plan, lockerUid);

        if (plan.DoorCharge == null)
        {
            if (lentGrid)
                hunt.GridUid = null;

            return false;
        }

        hunt.BlastLockerLentGrid = lentGrid;

        hunt.Blast = plan;
        hunt.BlastAction = NpcSquadAction.BlowDoor;
        hunt.BlastLockerUid = lockerUid;
        SetPhase(hunt, NpcHuntPhase.Blast, now);
        return true;
    }

    /// <summary>
    ///     Who puts a charge on <paramref name="lockerUid"/>, and from where: the floor beside it nearest the walk of the first
    ///         member that has one that sticks to it. Not a locker where the blast would let the air out.
    /// </summary>
    private NpcChargePick? FindLockerCharge(NpcSquadPlan plan, EntityUid lockerUid)
    {
        var hunt = plan.Hunt!;
        var lockerTransform = Transform(lockerUid);

        if (!_npcGridSystem.TryGetGrid(lockerTransform.GridUid, out var grid) || hunt.GridUid != grid.Owner)
            return null;

        var lockerTile = _mapSystem.TileIndicesFor(grid, lockerTransform.Coordinates);

        foreach (var memberUid in _members)
        {
            if (!_npcChargeSystem.TryGetCharge(memberUid, lockerUid, out _))
                continue;

            var memberTransform = Transform(memberUid);
            if (memberTransform.GridUid != grid.Owner)
                continue;

            _floodSteps.Clear();
            _npcSquadCoverSystem.FloodTiles(memberUid, grid, _mapSystem.TileIndicesFor(grid, memberTransform.Coordinates), LockerChargeWalk, _floodSteps, stopAtDoors: false);

            if (!TryGetLockerStandTile(lockerTile, out var standTile, out var steps))
                continue;

            if (IsUnsafeToBlow(hunt, grid, lockerTile, standTile, lockerTile + (lockerTile - standTile)))
                return null;

            var site = new NpcChargeSite(lockerUid,
                lockerTile,
                GetTileCoordinates(grid.Owner, standTile),
                standTile,
                lockerTransform.Coordinates,
                ((Vector2) (lockerTile - standTile)).ToWorldAngle(),
                IsDoor: true);

            // In the room with it, more than likely: anywhere out of the blast is as good to set it off from.
            if (TryPickPlanter(plan, grid, site, routeLength: steps, out var pick, outsideRoom: false))
                return pick;
        }

        return null;
    }

    /// <summary>
    ///     Of the floor either side of <paramref name="lockerTile"/>, what <see cref="_floodSteps"/> gets to soonest.
    /// </summary>
    private bool TryGetLockerStandTile(Vector2i lockerTile, out Vector2i standTile, out int steps)
    {
        standTile = default;
        steps = int.MaxValue;

        foreach (var offset in ChargeSiteOffsets)
        {
            var tile = lockerTile + offset;

            if (_floodSteps.TryGetValue(tile, out var tileSteps) && tileSteps < steps)
            {
                standTile = tile;
                steps = tileSteps;
            }
        }

        return steps != int.MaxValue;
    }

    /// <summary>
    ///     A locker blast is over, however it went: the search picks up where it was. Shot open from here, if it still stands and
    ///         is worth it; the charge is not tried on it again.
    /// </summary>
    private void EndLockerBlast(NpcHunt hunt, TimeSpan now)
    {
        if (hunt.Blast is { } blast)
        {
            ReleaseCharge(blast);

            // The time it took is not the search's: neither the hunt's, nor that of whoever was sent to the locker, who would
            //      otherwise have run out of it and be taken off it before a round was fired.
            hunt.StartedAt += now - blast.StartedAt;

            foreach (var point in hunt.SearchPoints)
            {
                if (point.StorageUid == hunt.BlastLockerUid && point.AssigneeUid != null)
                    point.AssignedAt = now;
            }
        }

        if (hunt.BlastLockerLentGrid)
            hunt.GridUid = null;

        hunt.Blast = null;
        hunt.BlastLockerUid = null;
        hunt.BlastLockerLentGrid = false;
        SetPhase(hunt, NpcHuntPhase.Search, now);
    }

    /// <summary>
    ///     Whether a member saw the hunted hostile get into <paramref name="lockerUid"/>, or vanish beside it.
    /// </summary>
    private bool IsBelievedIn(NpcHunt hunt, EntityUid lockerUid)
    {
        if (hunt.TargetUid is not { } targetUid)
            return false;

        foreach (var memberUid in _members)
        {
            if (_npcPerceptionSystem.TryGetContact(memberUid, targetUid, out var contact) &&
                contact.State is NpcContactState.Concealed or NpcContactState.Suspected &&
                contact.ContainerUid == lockerUid)
                return true;
        }

        return false;
    }

    private bool CanAnyoneOpen(EntityUid lockerUid)
    {
        foreach (var memberUid in _members)
        {
            if (_entityStorageSystem.CanOpen(memberUid, lockerUid, silent: true))
                return true;
        }

        return false;
    }
}
