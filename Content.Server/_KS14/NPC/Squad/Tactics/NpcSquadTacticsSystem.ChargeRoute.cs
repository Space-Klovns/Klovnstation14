using System.Diagnostics.CodeAnalysis;
using Content.Server._KS14.NPC.Utility;
using Content.Server.NPC.Components;
using Content.Shared._KS14.NPC;
using Content.Shared.DoAfter;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._KS14.NPC.Squad.Tactics;

/// <summary>
///     The way to a charge, and who is where along it. One walk out from the spot in front of the charge
///         (<see cref="NpcSquadCharge.RouteSteps"/>) says how far each member still has to go. From that:
///     <list type="bullet">
///         <item>The planter is given up on only once it stops getting any nearer, not after so long: the way round can be
///             long, and slow through a door.</item>
///         <item>The planter leads. Anyone behind it trails it; anyone ahead of it, on its way, steps off it, or stands still
///             for it to get past where there is no room to. Once it is past the edge of the blast, the rest stop there.</item>
///     </list>
/// </summary>
public sealed partial class NpcSquadTacticsSystem
{
    [Dependency] private EntityQuery<ActiveDoAfterComponent> _activeDoAfterQuery = default!;
    [Dependency] private EntityQuery<NPCSteeringComponent> _steeringQuery = default!;

    /// <summary>
    ///     How far out, in steps, the way to a charge is worked out. Past that, nobody is ahead of or behind anyone.
    /// </summary>
    private const int ChargeRouteSteps = 64;

    /// <summary>
    ///     How often the way to a charge is worked out again, for doors and walls that have come or gone.
    /// </summary>
    private static readonly TimeSpan ChargeRouteRefresh = TimeSpan.FromSeconds(3);

    /// <summary>
    ///     How far a planter further out than the way is worked out has to have moved to count as getting anywhere.
    /// </summary>
    private const float ChargeProgressDistance = 1f;

    /// <summary>
    ///     How near the spot in front of a charge everyone but its planter comes while it goes on: just out of its blast.
    /// </summary>
    private const float FollowChargeRange = ChargeDangerRange + 1f;

    /// <summary>
    ///     How many steps' walk from the spot in front of a charge someone may be and still count as having come along to it.
    ///         Anyone within <see cref="FollowChargeRange"/> of it across open floor is: this only rules out anyone near it only
    ///         through a wall, with the long way round still to walk.
    /// </summary>
    private const int FollowChargeWalk = 16;

    /// <summary>
    ///     How many steps fewer to go than the planter a member has to have to count as ahead of it. Side by side is not.
    /// </summary>
    private const int LeadMargin = 1;

    /// <summary>
    ///     How near the planter those trailing it come, and how far it may get from where they were sent before they are sent
    ///         after it again. Not every step: each is a new order, and a new path.
    /// </summary>
    private const float TrailRange = 2f;
    private const float TrailSlack = 2f;

    /// <summary>
    ///     How near the tile to the side of the planter's way one getting out of it has to get: onto it, not just next to it.
    /// </summary>
    private const float AsideOrderRange = 0.4f;

    /// <summary>
    ///     The tiles the planter is walking, as of this update. See <see cref="TracePlanterRoute"/>.
    /// </summary>
    private readonly HashSet<Vector2i> _planterRoute = new();

    /// <summary>
    ///     Brings <see cref="NpcSquadCharge.RouteSteps"/> up to date, every <see cref="ChargeRouteRefresh"/>.
    /// </summary>
    private void RefreshChargeRoute(NpcSquadCharge charge, NpcHunt hunt, TimeSpan now)
    {
        if (charge.RouteBuiltAt is { } builtAt && now - builtAt < ChargeRouteRefresh)
            return;

        charge.RouteBuiltAt = now;
        charge.RouteSteps.Clear();

        if (!_npcGridSystem.TryGetGrid(hunt.GridUid, out var grid) ||
            _transformSystem.GetGrid(charge.Pick.Site.StandCoordinates) != grid.Owner)
            return;

        charge.RouteGridUid = grid.Owner;
        _npcSquadCoverSystem.FloodTiles(charge.Pick.PlanterUid, grid, charge.Pick.Site.StandTile, ChargeRouteSteps, charge.RouteSteps, stopAtDoors: false);
    }

    /// <summary>
    ///     How many steps <paramref name="uid"/> has to walk to the spot in front of the charge, and the tile it is on. False if it
    ///         is further out than that is worked out, or somewhere else altogether.
    /// </summary>
    private bool TryGetStepsToCharge(NpcSquadCharge charge, EntityUid uid, out int steps, out Vector2i tile)
    {
        steps = default;

        return _npcGridSystem.TryGetGridTile(_transformSystem.GetMoverCoordinates(uid), out var grid, out tile) &&
            grid.Owner == charge.RouteGridUid &&
            charge.RouteSteps.TryGetValue(tile, out steps);
    }

    /// <summary>
    ///     Whether the planter of <paramref name="charge"/> is still getting there: it has got nearer within
    ///         <see cref="NpcSquadTacticsSettings.PlanChargeTimeout"/>, going by the walk, not a straight line, or been busy with
    ///         its hands (a door on the way, or the charge itself). How long it has been at it says nothing: the way round can be
    ///         long, and slow through a door, and nearer each time is bound to get there.
    /// </summary>
    private bool IsPlanterGettingThere(NpcSquadCharge charge, NpcHunt hunt, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        RefreshChargeRoute(charge, hunt, now);

        var planterUid = charge.Pick.PlanterUid;
        var planterCoordinates = _transformSystem.GetMoverCoordinates(planterUid);
        var progressed = _activeDoAfterQuery.HasComp(planterUid);

        if (TryGetStepsToCharge(charge, planterUid, out var steps, out _))
        {
            if (steps < charge.BestSteps)
            {
                charge.BestSteps = steps;
                progressed = true;
            }
        }
        else
        {
            // Further out than the way is worked out: anywhere it walks will do, until it is within it.
            progressed |= !NpcCoordinates.IsWithin(planterCoordinates, charge.ProgressCoordinates, ChargeProgressDistance, EntityManager, _transformSystem);
        }

        if (progressed)
        {
            charge.ProgressAt = now;
            charge.ProgressCoordinates = planterCoordinates;
        }

        return now - charge.ProgressAt < settings.PlanChargeTimeout;
    }

    /// <summary>
    ///     Whether the planter of a wired charge that is on is still getting on with setting it off: it has got nearer where it
    ///         presses the activator from within <see cref="NpcSquadTacticsSettings.PlanChargeTimeout"/>, or been busy with its
    ///         hands. Once it is there, that is how long it has to press it. Not a set time from when the charge went on: getting
    ///         there can be slow, past the rest of the squad, and an order to press it can wait on them being clear.
    /// </summary>
    private bool IsPlanterGettingClear(NpcSquadCharge charge, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        var planterUid = charge.Pick.PlanterUid;
        var progressed = _activeDoAfterQuery.HasComp(planterUid);

        if (_transformSystem.GetMoverCoordinates(planterUid).TryDistance(EntityManager, _transformSystem, charge.Pick.StandOffCoordinates, out var distance) &&
            distance < charge.BestStandOffDistance - ChargeProgressDistance / 2)
        {
            charge.BestStandOffDistance = distance;
            progressed = true;
        }

        if (progressed)
            charge.ProgressAt = now;

        return now - charge.ProgressAt < settings.PlanChargeTimeout;
    }

    /// <summary>
    ///     Fills <see cref="_planterRoute"/> with the tiles the planter is walking: the path it is following, and, as that may
    ///         run out short of the charge, the shortest walk on from where it is. Returns how many steps it has to go, if known.
    /// </summary>
    private int? TracePlanterRoute(NpcSquadCharge charge)
    {
        _planterRoute.Clear();

        if (!TryGetStepsToCharge(charge, charge.Pick.PlanterUid, out var steps, out var tile) ||
            !_npcGridSystem.TryGetGrid(charge.RouteGridUid, out var grid))
            return null;

        var planterSteps = steps;

        if (_steeringQuery.TryComp(charge.Pick.PlanterUid, out var steeringComponent))
        {
            foreach (var poly in steeringComponent.CurrentPath)
            {
                if (poly.GraphUid == grid.Owner)
                    _planterRoute.Add(_mapSystem.TileIndicesFor(grid, poly.Coordinates));
            }
        }

        // Downhill to the charge, keeping straight on where that is as short: down the middle of nothing, along its own side.
        _planterRoute.Add(tile);
        var direction = Vector2i.Zero;

        while (steps > 0 && TryStepTowardCharge(charge, tile, steps, ref direction, out tile))
        {
            steps--;
            _planterRoute.Add(tile);
        }

        return planterSteps;
    }

    private bool TryStepTowardCharge(NpcSquadCharge charge, Vector2i tile, int steps, ref Vector2i direction, out Vector2i next)
    {
        if (direction != Vector2i.Zero &&
            charge.RouteSteps.TryGetValue(tile + direction, out var straightSteps) &&
            straightSteps == steps - 1)
        {
            next = tile + direction;
            return true;
        }

        foreach (var offset in ChargeSiteOffsets)
        {
            if (!charge.RouteSteps.TryGetValue(tile + offset, out var offsetSteps) || offsetSteps != steps - 1)
                continue;

            direction = offset;
            next = tile + offset;
            return true;
        }

        next = default;
        return false;
    }

    /// <summary>
    ///     Whether <paramref name="memberUid"/> is one of those coming along to a charge: not its planter, nor stacked up on a
    ///         way in, nor watching the hostile.
    /// </summary>
    private static bool IsFollowingCharge(EntityUid memberUid, NpcSquadPlan plan, NpcHunt hunt)
    {
        return memberUid != plan.Charge?.Pick.PlanterUid &&
            memberUid != plan.WatcherUid &&
            !hunt.StagedMembers.ContainsKey(memberUid);
    }

    /// <summary>
    ///     The order for one coming along behind the planter of <paramref name="charge"/>, while it goes to put it on. The
    ///         planter leads, and the rest go in through the hole after it.
    /// </summary>
    /// <param name="planterSteps">How far the planter has to go, from <see cref="TracePlanterRoute"/>.</param>
    private void OrderChargeFollower(EntityUid memberUid, EntityUid issuerUid, NpcSquadCharge charge, NpcHunt hunt, int? planterSteps, TimeSpan now)
    {
        var site = charge.Pick.Site;

        // Ahead of it: out of its way, or anyone between it and the charge is someone for it to squeeze past.
        if (TryGetStepsToCharge(charge, memberUid, out var memberSteps, out var memberTile) &&
            (planterSteps is not { } leadSteps || memberSteps < leadSteps - LeadMargin))
        {
            GiveWay(memberUid, issuerUid, charge, hunt, memberTile, memberSteps, now);
            return;
        }

        // Just out of the blast already.
        if (HasComeAlong(charge, memberUid))
        {
            SetOrder(memberUid, issuerUid, NpcOrderKind.Stage, site.StandCoordinates, site.LocalFacing, FollowChargeRange, null, now);
            return;
        }

        // To the edge of the blast, on this one's side of it, once the planter is past that. Before then it is where the planter
        //      is going, and this one would be in its way again: until then, after the planter.
        if (planterSteps is { } planterLeft &&
            TryGetFollowSpot(memberUid, issuerUid, charge, out var followCoordinates, out var followSteps) &&
            followSteps > planterLeft + LeadMargin)
        {
            SetOrder(memberUid, issuerUid, NpcOrderKind.Stage, followCoordinates, site.LocalFacing, OrderRange, null, now);
            return;
        }

        TrailPlanter(memberUid, issuerUid, charge, now);
    }

    /// <summary>
    ///     Sends <paramref name="memberUid"/> after the planter, to just behind where it is. Not again for every step it takes:
    ///         only once it is <see cref="TrailSlack"/> on from where this one was sent last.
    /// </summary>
    private void TrailPlanter(EntityUid memberUid, EntityUid issuerUid, NpcSquadCharge charge, TimeSpan now)
    {
        var target = _transformSystem.GetMoverCoordinates(charge.Pick.PlanterUid);

        if (_orderQuery.TryComp(memberUid, out var orderComponent) &&
            orderComponent.Order is { Kind: NpcOrderKind.Stage } order &&
            order.IssuerUid == issuerUid &&
            order.Range == TrailRange &&
            NpcCoordinates.IsWithin(order.Coordinates, target, TrailSlack, EntityManager, _transformSystem))
        {
            target = order.Coordinates;
        }

        SetOrder(memberUid, issuerUid, NpcOrderKind.Stage, target, charge.Pick.Site.LocalFacing, TrailRange, null, now);
    }

    /// <summary>
    ///     Gets <paramref name="memberUid"/>, ahead of the planter, out of its way: onto a tile beside it, off the way the planter
    ///         is walking. Never further on, where the planter is going. Where there is none, as in a corridor a tile wide, or
    ///         where it is off that way already, it stands still: the planter gets past one standing still.
    /// </summary>
    private void GiveWay(EntityUid memberUid, EntityUid issuerUid, NpcSquadCharge charge, NpcHunt hunt, Vector2i memberTile, int memberSteps, TimeSpan now)
    {
        var site = charge.Pick.Site;

        if (_planterRoute.Contains(memberTile) && TryGetAsideTile(charge, memberTile, memberSteps, out var asideTile))
        {
            SetOrder(memberUid, issuerUid, NpcOrderKind.Stage, GetTileCoordinates(charge.RouteGridUid, asideTile), site.LocalFacing, AsideOrderRange, null, now);
            return;
        }

        // Stepping aside already: it carries on, rather than be told to stand still half way.
        if (_orderQuery.TryComp(memberUid, out var orderComponent) &&
            orderComponent.Order is { Kind: NpcOrderKind.Stage, Range: AsideOrderRange } order &&
            order.IssuerUid == issuerUid &&
            !NpcCoordinates.IsWithin(_transformSystem.GetMoverCoordinates(memberUid), order.Coordinates, AsideOrderRange, EntityManager, _transformSystem))
            return;

        HoldStill(memberUid, issuerUid, site.Tile, hunt, now);
    }

    /// <summary>
    ///     A tile next to <paramref name="memberTile"/> off the planter's way, that nobody is on: to the side, or else back the
    ///         way the planter is coming.
    /// </summary>
    private bool TryGetAsideTile(NpcSquadCharge charge, Vector2i memberTile, int memberSteps, out Vector2i asideTile)
    {
        asideTile = default;

        if (!_npcGridSystem.TryGetGrid(charge.RouteGridUid, out var grid))
            return false;

        var aside = NpcBestOf<Vector2i>.Lowest();
        foreach (var offset in ChargeSiteOffsets)
        {
            var tile = memberTile + offset;

            if (_planterRoute.Contains(tile) ||
                !charge.RouteSteps.TryGetValue(tile, out var steps) ||
                steps < memberSteps ||
                IsAnyMemberOn(grid, tile))
                continue;

            aside.Offer(tile, steps - memberSteps);
        }

        asideTile = aside.Value;
        return aside.Found;
    }

    /// <summary>
    ///     Whether <paramref name="uid"/> is just out of the blast of <paramref name="charge"/> already: near the spot in front of
    ///         it in a straight line, and a short walk from it, not only through a wall.
    /// </summary>
    private bool HasComeAlong(NpcSquadCharge charge, EntityUid uid)
    {
        return TryGetStepsToCharge(charge, uid, out var steps, out _) &&
            steps <= FollowChargeWalk &&
            NpcCoordinates.IsWithin(_transformSystem.GetMoverCoordinates(uid), charge.Pick.Site.StandCoordinates, FollowChargeRange, EntityManager, _transformSystem);
    }

    /// <summary>
    ///     Where <paramref name="memberUid"/>, still on its way to a charge, is to stop: of the floor a short walk from the spot
    ///         in front of it and out of its blast, the tile on its way there. A spot it was sent to already is kept while it
    ///         still is one, or each step it took could bring a new order. False if there is none.
    /// </summary>
    /// <param name="steps">How many steps the spot is from the spot in front of the charge.</param>
    private bool TryGetFollowSpot(EntityUid memberUid, EntityUid issuerUid, NpcSquadCharge charge, out EntityCoordinates coordinates, out int steps)
    {
        coordinates = default;
        steps = default;

        if (!_npcGridSystem.TryGetGrid(charge.RouteGridUid, out var grid) ||
            !_npcGridSystem.TryGetGridTile(_transformSystem.GetMoverCoordinates(memberUid), out var memberGrid, out var memberTile) ||
            memberGrid.Owner != grid.Owner)
            return false;

        if (_orderQuery.TryComp(memberUid, out var orderComponent) &&
            orderComponent.Order is { Kind: NpcOrderKind.Stage, Range: OrderRange } order &&
            order.IssuerUid == issuerUid &&
            _transformSystem.GetGrid(order.Coordinates) == grid.Owner &&
            _mapSystem.TileIndicesFor(grid, order.Coordinates) is var orderTile &&
            IsFollowTile(charge, grid, orderTile))
        {
            coordinates = order.Coordinates;
            steps = charge.RouteSteps[orderTile];
            return true;
        }

        // Scored by about how long a way to the charge through it is: from the member in a straight line, which is never
        //      longer than the walk, then the walk on from it. The lowest is on the member's way, and as near the charge as
        //      is safe. Nearest the member alone could be the far end of its own side of a wall.
        var onTheWay = NpcBestOf<Vector2i>.Lowest();
        foreach (var (tile, tileSteps) in charge.RouteSteps)
        {
            if (IsFollowTile(charge, grid, tile))
                onTheWay.Offer(tile, (tile - memberTile).Length + tileSteps);
        }

        if (!onTheWay.Found)
            return false;

        coordinates = GetTileCoordinates(grid.Owner, onTheWay.Value);
        steps = charge.RouteSteps[onTheWay.Value];
        return true;
    }

    /// <summary>
    ///     Whether <paramref name="tile"/> is a short walk from the spot in front of a charge and out of its blast, by a tile
    ///         to spare: one stopping as far short as an order lets it is still out.
    /// </summary>
    private static bool IsFollowTile(NpcSquadCharge charge, Entity<MapGridComponent> grid, Vector2i tile)
    {
        return charge.RouteSteps.TryGetValue(tile, out var steps) &&
            steps <= FollowChargeWalk &&
            (tile - charge.Pick.Site.Tile).Length * grid.Comp.TileSize >= ChargeDangerRange + OrderRange;
    }
}
