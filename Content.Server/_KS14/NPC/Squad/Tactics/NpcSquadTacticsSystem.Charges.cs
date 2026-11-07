using System.Numerics;
using Content.Server._KS14.NPC.Charges;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._KS14.NPC;
using Content.Shared.Doors.Components;
using Content.Shared.Maps;
using Content.Shared.Tag;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.NPC.Squad.Tactics;

/// <summary>
///     Making a way in with a charge. Sites come from <see cref="FindDoorCharge"/> and <see cref="FindWallCharge"/>, and from
///         there on <see cref="RunCharge"/> does not care which: someone sticks a charge on, everyone gets clear, and either the
///         timer runs out or the planter presses the activator.
/// </summary>
public sealed partial class NpcSquadTacticsSystem
{
    [Dependency] private AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private NpcChargeSystem _npcChargeSystem = default!;
    [Dependency] private TagSystem _tagSystem = default!;
    [Dependency] private TurfSystem _turfSystem = default!;

    private static readonly ProtoId<TagPrototype> WallTag = "Wall";

    /// <summary>
    ///     How close to its spot a planter has to get, which is a step from what it sticks the charge on and within arm's reach
    ///         of it. Any further and sticking it on never gets started.
    /// </summary>
    private const float PlantOrderRange = 0.5f;

    private const float BlowDoorCost = 8f;
    private const float BlowWallCost = 14f;

    /// <summary>
    ///     How much waiting out a timer costs a plan, per second of it. Less than a second per second: nobody is exposed.
    /// </summary>
    private const float ChargeFuseCost = 0.5f;

    /// <summary>
    ///     How many walls to look for a way round to, nearest first.
    /// </summary>
    private const int MaxWallCandidates = 6;

    /// <summary>
    ///     How far from anything open to space a charge may go off. What is next to it goes as well.
    /// </summary>
    private const int HullClearance = 2;

    /// <summary>
    ///     How near a charge and how far from it a planter may press its activator: out of the blast, and on the wire.
    /// </summary>
    public const float MinStandOff = 2.5f;
    public const float MaxStandOff = 3.5f;

    private const int StandOffSearchSteps = 6;

    /// <summary>
    ///     How far across, in steps from the hostile, a room with no way in can be.
    /// </summary>
    private const int SealedRoomSteps = 24;

    /// <summary>
    ///     How close to its spot a planter counts as there.
    /// </summary>
    private const float StandOffRange = 0.8f;

    /// <summary>
    ///     Anyone nearer a charge than this gets out of sight of it.
    /// </summary>
    private const float ChargeDangerRange = 7f;

    /// <summary>
    ///     How long the rest get to clear before a wired charge is set off regardless.
    /// </summary>
    private static readonly TimeSpan ClearingTime = TimeSpan.FromSeconds(3);

    /// <summary>
    ///     How long past when it should have gone off before a charge is given up on.
    /// </summary>
    private static readonly TimeSpan ChargeOverdue = TimeSpan.FromSeconds(3);

    /// <summary>
    ///     How long after a charge goes off to look at what it did.
    /// </summary>
    private static readonly TimeSpan BlastTime = TimeSpan.FromSeconds(1);

    private static readonly Vector2i[] ChargeSiteOffsets = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];

    private readonly Dictionary<Vector2i, int> _floodSteps = new();
    private readonly List<(int Steps, Vector2i Room, Vector2i Wall, Vector2i Outside)> _wallCandidates = new();

    /// <summary>
    ///     A hunt that cannot get in makes a way in, if it is one to (<see cref="NpcSquadTacticsSettings.HuntCharges"/>) and has
    ///         what it takes. A door before a wall. It tries this the once.
    /// </summary>
    private bool TryStartBlast(NpcHunt hunt, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        if (!settings.HuntCharges || hunt.BlastTried || !AnyoneHasACharge())
            return false;

        hunt.BlastTried = true;

        var plan = new NpcSquadPlan
        {
            TargetUid = hunt.TargetUid ?? EntityUid.Invalid,
            StartedAt = now,
            StepStartedAt = now,
            State = NpcSquadPlanState.Running,
            Hunt = hunt,
            MemberCount = _members.Count,
        };

        FindCharges(plan, settings);

        if (plan.DoorCharge == null && plan.WallCharge == null)
            return false;

        hunt.Blast = plan;
        hunt.BlastAction = plan.DoorCharge != null ? NpcSquadAction.BlowDoor : NpcSquadAction.BlowWall;
        SetPhase(hunt, NpcHuntPhase.Blast, now);
        return true;
    }

    /// <summary>
    ///     The charge goes on, everyone gets clear, and it goes off. Then they go through the hole, and look for what they heard.
    ///         Should it not work, they are shut out after all. Returns whether the way is open.
    /// </summary>
    private bool UpdateBlast(EntityUid issuerUid, EntityUid? leaderUid, NpcHunt hunt, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        var plan = hunt.Blast!;

        // Hostile or no hostile, this is not to be cut short by anything less than one turning up: see OverCombat.
        _givingPlanOrders = true;
        var blown = RunCharge(issuerUid, plan, hunt.BlastAction, settings, now);
        _givingPlanOrders = false;

        if (!blown)
        {
            if (plan.Charge != null)
                return false;

            hunt.Blast = null;
            ExhaustShutOut(leaderUid, hunt, settings, now);
            return false;
        }

        hunt.Blast = null;

        if (hunt.GridUid is { } gridUid && _mapGridQuery.TryComp(gridUid, out var mapGridComponent))
            BuildSearchPoints(leaderUid ?? _members[0], hunt, (gridUid, mapGridComponent), settings);

        hunt.UnseenTiles.UnionWith(hunt.RoomTiles);
        AssignEntrances(hunt, settings);
        SetPhase(hunt, hunt.StagedMembers.Count > 0 ? NpcHuntPhase.Entry : NpcHuntPhase.Search, now);
        return true;
    }

    /// <summary>
    ///     Works out what a charge could do from here. A door is looked at every time, as cheap and liable to change. A wall
    ///         only once a plan: that is a search, and walls stay where they are.
    /// </summary>
    private void FindCharges(NpcSquadPlan plan, NpcSquadTacticsSettings settings)
    {
        plan.DoorCharge = FindDoorCharge(plan);

        if (plan.WallSearched)
        {
            if (plan.WallCharge is { } pick && !IsStillGood(pick))
                plan.WallCharge = null;

            return;
        }

        plan.WallSearched = true;
        plan.WallCharge = FindWallCharge(plan, settings);
    }

    private bool IsStillGood(NpcChargePick pick)
    {
        return _members.Contains(pick.PlanterUid) &&
            !TerminatingOrDeleted(pick.Site.TargetUid) &&
            _npcChargeSystem.TryGetCharge(pick.PlanterUid, pick.Site.TargetUid, out _);
    }

    private bool AnyoneHasACharge()
    {
        foreach (var memberUid in _members)
        {
            if (_npcChargeSystem.TryGetCharge(memberUid, null, out _))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     A room with no way in at all is no room to anything that goes by its ways in, and it is what a wall charge is for.
    /// </summary>
    private void PrepareSealedRoom(NpcSquadPlan plan)
    {
        if (AnyoneHasACharge())
            TryPrepareSealedRoom(plan.Hunt!, plan.TargetUid, Transform(plan.TargetUid).Coordinates);
    }

    /// <summary>
    ///     Whether <paramref name="coordinates"/> is cut off from the squad: walking out from there, through every door, nothing
    ///         gets to a member before the floor runs out. A floor that does not run out is somewhere too big to say, and is
    ///         taken to lead somewhere. What it finds becomes the room.
    /// </summary>
    private bool TryPrepareSealedRoom(NpcHunt hunt, EntityUid walkerUid, EntityCoordinates coordinates)
    {
        if (_transformSystem.GetGrid(coordinates) is not { } gridUid || !_mapGridQuery.TryComp(gridUid, out var mapGridComponent))
            return false;

        var grid = new Entity<MapGridComponent>(gridUid, mapGridComponent);

        _floodSteps.Clear();
        _npcSquadCoverSystem.FloodTiles(walkerUid, grid, _mapSystem.TileIndicesFor(grid, coordinates), SealedRoomSteps, _floodSteps, stopAtDoors: false);

        foreach (var (tile, steps) in _floodSteps)
        {
            if (steps >= SealedRoomSteps || IsAnyMemberOn(grid, tile))
                return false;
        }

        hunt.GridUid = gridUid;
        hunt.RoomTiles.UnionWith(_floodSteps.Keys);
        return true;
    }

    /// <summary>
    ///     The nearest floor to the outside of the room that <paramref name="walkerUid"/> can walk to, and how far that is.
    /// </summary>
    private bool TryFindNearestOutside(NpcHunt hunt, EntityUid walkerUid, NpcSquadTacticsSettings settings, out EntityCoordinates coordinates, out int length)
    {
        coordinates = default;
        length = 0;

        if (hunt.GridUid is not { } gridUid ||
            !_mapGridQuery.TryComp(gridUid, out var mapGridComponent) ||
            Transform(walkerUid).GridUid != gridUid)
            return false;

        var grid = new Entity<MapGridComponent>(gridUid, mapGridComponent);
        var walkerTile = _mapSystem.TileIndicesFor(grid, Transform(walkerUid).Coordinates);

        FindWallCandidates(hunt, walkerUid, grid, walkerTile, settings);

        var avoidTiles = new HashSet<Vector2i>(hunt.RoomTiles);
        avoidTiles.UnionWith(hunt.ThresholdTiles);

        var waypoints = new List<Vector2i>();
        var tried = 0;

        foreach (var (_, _, _, outsideTile) in _wallCandidates)
        {
            if (++tried > MaxWallCandidates)
                break;

            waypoints.Clear();
            if (!_npcSquadCoverSystem.TryFindRouteAround(walkerUid, grid, walkerTile, outsideTile, avoidTiles, settings.MaxStageDistance, waypoints, out length))
                continue;

            coordinates = GetTileCoordinates(gridUid, outsideTile);
            return true;
        }

        return false;
    }

    private bool IsAnyMemberOn(Entity<MapGridComponent> grid, Vector2i tile)
    {
        foreach (var memberUid in _members)
        {
            var transformComponent = Transform(memberUid);

            if (transformComponent.GridUid == grid.Owner && _mapSystem.TileIndicesFor(grid, transformComponent.Coordinates) == tile)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     The door of a way in that nobody gets through any other way, if a charge can be put on one.
    /// </summary>
    private NpcChargePick? FindDoorCharge(NpcSquadPlan plan)
    {
        var hunt = plan.Hunt!;

        if (hunt.GridUid is not { } gridUid || !_mapGridQuery.TryComp(gridUid, out var mapGridComponent))
            return null;

        var grid = new Entity<MapGridComponent>(gridUid, mapGridComponent);

        for (var e = 0; e < hunt.Entrances.Count; e++)
        {
            var entrance = hunt.Entrances[e];

            if (entrance.DoorUid is not { } doorUid ||
                !_doorQuery.TryComp(doorUid, out var doorComponent) ||
                doorComponent.State is DoorState.Open or DoorState.Opening ||
                HoldsBackDanger(doorUid) ||
                !IsShutToThem(plan, e))
                continue;

            var doorCoordinates = Transform(doorUid).Coordinates;
            var doorTile = _mapSystem.TileIndicesFor(grid, doorCoordinates);
            var standCoordinates = GetTileCoordinates(gridUid, doorTile).Offset(-entrance.LocalFacing.ToWorldVec());
            var standTile = _mapSystem.TileIndicesFor(grid, standCoordinates);

            if (IsUnsafeToBlow(hunt, grid, doorTile, standTile, _mapSystem.TileIndicesFor(grid, entrance.EntryCoordinates)))
                continue;

            var site = new NpcChargeSite(doorUid, doorTile, standCoordinates, standTile, entrance.EntryCoordinates, entrance.LocalFacing, IsDoor: true);

            if (TryPickPlanter(plan, grid, site, routeLength: 0, out var pick))
                return pick;
        }

        return null;
    }

    /// <summary>
    ///     Whether way in <paramref name="entranceIndex"/> opens for nobody: it has no lead, or one with nothing but a charge, or
    ///         one whose tool has already failed.
    /// </summary>
    private static bool IsShutToThem(NpcSquadPlan plan, int entranceIndex)
    {
        foreach (var staging in plan.Hunt!.StagedMembers.Values)
        {
            if (!staging.IsLead || staging.EntranceIndex != entranceIndex)
                continue;

            if (staging.Method == Doors.NpcBreachMethod.None)
                return false;

            if (staging.Method != Doors.NpcBreachMethod.Charge && (plan.BannedActions & NpcSquadGoap.GetBan(NpcSquadAction.ForceDoor)) == 0x0)
                return false;
        }

        return true;
    }

    /// <summary>
    ///     A wall of the hostile's room with floor the other side that somebody with a charge can walk to, the nearest there is.
    /// </summary>
    private NpcChargePick? FindWallCharge(NpcSquadPlan plan, NpcSquadTacticsSettings settings)
    {
        var hunt = plan.Hunt!;

        if (hunt.GridUid is not { } gridUid || !_mapGridQuery.TryComp(gridUid, out var mapGridComponent))
            return null;

        var carrierUid = EntityUid.Invalid;

        foreach (var memberUid in _members)
        {
            if (memberUid == plan.WatcherUid || !_npcChargeSystem.TryGetCharge(memberUid, null, out _))
                continue;

            carrierUid = memberUid;
            break;
        }

        if (!carrierUid.IsValid() || Transform(carrierUid).GridUid != gridUid)
            return null;

        var grid = new Entity<MapGridComponent>(gridUid, mapGridComponent);
        var carrierTile = _mapSystem.TileIndicesFor(grid, Transform(carrierUid).Coordinates);

        FindWallCandidates(hunt, carrierUid, grid, carrierTile, settings);

        var avoidTiles = new HashSet<Vector2i>(hunt.RoomTiles);
        avoidTiles.UnionWith(hunt.ThresholdTiles);

        var waypoints = new List<Vector2i>();
        var tried = 0;

        foreach (var (_, roomTile, wallTile, outsideTile) in _wallCandidates)
        {
            if (GetWallOn(grid, wallTile) is not { } wallUid || IsUnsafeToBlow(hunt, grid, wallTile, outsideTile, roomTile))
                continue;

            if (++tried > MaxWallCandidates)
                break;

            waypoints.Clear();
            if (!_npcSquadCoverSystem.TryFindRouteAround(carrierUid, grid, carrierTile, outsideTile, avoidTiles, settings.MaxStageDistance, waypoints, out var length))
                continue;

            var facing = ((Vector2) (roomTile - outsideTile)).ToWorldAngle();
            var site = new NpcChargeSite(wallUid,
                wallTile,
                GetTileCoordinates(gridUid, outsideTile),
                outsideTile,
                GetTileCoordinates(gridUid, roomTile),
                facing,
                IsDoor: false);

            if (TryPickPlanter(plan, grid, site, length, out var pick))
                return pick;
        }

        return null;
    }

    /// <summary>
    ///     Every wall one thick between the room and floor <paramref name="walkerUid"/> can get to, nearest first. Goes by walking
    ///         distance, near enough, through doors and the room as well: the real way round is only worked out for those
    ///         that make the cut.
    /// </summary>
    private void FindWallCandidates(NpcHunt hunt, EntityUid walkerUid, Entity<MapGridComponent> grid, Vector2i walkerTile, NpcSquadTacticsSettings settings)
    {
        _floodSteps.Clear();
        _npcSquadCoverSystem.FloodTiles(walkerUid, grid, walkerTile, settings.MaxStageDistance, _floodSteps, stopAtDoors: false);

        _wallCandidates.Clear();

        foreach (var roomTile in hunt.RoomTiles)
        {
            foreach (var offset in ChargeSiteOffsets)
            {
                var wallTile = roomTile + offset;
                var outsideTile = wallTile + offset;

                if (hunt.RoomTiles.Contains(wallTile) ||
                    hunt.ThresholdTiles.Contains(wallTile) ||
                    hunt.RoomTiles.Contains(outsideTile) ||
                    hunt.ThresholdTiles.Contains(outsideTile) ||
                    _floodSteps.ContainsKey(wallTile) ||
                    !_floodSteps.TryGetValue(outsideTile, out var steps))
                    continue;

                _wallCandidates.Add((steps, roomTile, wallTile, outsideTile));
            }
        }

        _wallCandidates.Sort((a, b) => a.Steps.CompareTo(b.Steps));
    }

    /// <summary>
    ///     The wall on <paramref name="tile"/>, if there is one a charge can break, and nothing there that is a door. Going by
    ///         what it is and not just that it is in the way: a window, a machine or a grille is no wall.
    /// </summary>
    private EntityUid? GetWallOn(Entity<MapGridComponent> grid, Vector2i tile)
    {
        EntityUid? wallUid = null;

        var anchoredEnumerator = _mapSystem.GetAnchoredEntities(grid, grid.Comp, tile);
        while (anchoredEnumerator.MoveNext(out var anchoredUid))
        {
            if (_doorQuery.HasComp(anchoredUid.Value))
                return null;

            if (!_tagSystem.HasTag(anchoredUid.Value, WallTag))
                continue;

            if (!_destructibleSystem.TryGetDestroyedAt(anchoredUid.Value, out var destroyedAt) ||
                destroyedAt is not { } threshold ||
                threshold == Content.Shared.FixedPoint.FixedPoint2.MaxValue)
                return null;

            wallUid ??= anchoredUid.Value;
        }

        return wallUid;
    }

    /// <summary>
    ///     Whether a blast at <paramref name="tile"/> could open the station to space, or let in what is either side of it.
    /// </summary>
    private bool IsUnsafeToBlow(NpcHunt hunt, Entity<MapGridComponent> grid, Vector2i tile, Vector2i oneSide, Vector2i otherSide)
    {
        for (var x = -HullClearance; x <= HullClearance; x++)
        {
            for (var y = -HullClearance; y <= HullClearance; y++)
            {
                if (!_mapSystem.TryGetTile(grid.Comp, tile + new Vector2i(x, y), out var gridTile) || _turfSystem.IsSpace(gridTile))
                    return true;
            }
        }

        var minPressure = hunt.Settings.PlanChargeMinPressure;
        return IsThin(grid, oneSide, minPressure) || IsThin(grid, otherSide, minPressure);
    }

    private bool IsThin(Entity<MapGridComponent> grid, Vector2i tile, float minPressure)
    {
        return _atmosphereSystem.GetTileMixture((grid.Owner, null, null), null, tile) is { } mixture && mixture.Pressure < minPressure;
    }

    /// <summary>
    ///     Finds who should put a charge on <paramref name="site"/>. Wired over timed, whoever carries it. A wired charge needs
    ///         somewhere to stand off from within reach of its wire, or it is no good here.
    /// </summary>
    private bool TryPickPlanter(NpcSquadPlan plan, Entity<MapGridComponent> grid, NpcChargeSite site, int routeLength, out NpcChargePick pick)
    {
        pick = default;
        var found = false;

        foreach (var memberUid in _members)
        {
            if (memberUid == plan.WatcherUid || !_npcChargeSystem.TryGetCharge(memberUid, site.TargetUid, out var charge))
                continue;

            if (charge.Method == NpcChargeMethod.Timed)
            {
                if (found)
                    continue;

                pick = new NpcChargePick(site, memberUid, charge, site.StandCoordinates, routeLength);
                found = true;
                continue;
            }

            if (!TryFindStandOff(memberUid, grid, site, plan.Hunt!, out var standOffCoordinates))
                continue;

            pick = new NpcChargePick(site, memberUid, charge, standOffCoordinates, routeLength);
            return true;
        }

        return found;
    }

    /// <summary>
    ///     Floor outside the room, far enough from <paramref name="site"/> to be out of the blast and near enough to be on the
    ///         wire. Round a corner from it if there is such a place, and otherwise the shortest walk.
    /// </summary>
    private bool TryFindStandOff(EntityUid memberUid, Entity<MapGridComponent> grid, NpcChargeSite site, NpcHunt hunt, out EntityCoordinates coordinates)
    {
        coordinates = default;

        _floodSteps.Clear();
        _npcSquadCoverSystem.FloodTiles(memberUid, grid, site.StandTile, StandOffSearchSteps, _floodSteps);

        var standMapCoordinates = _transformSystem.ToMapCoordinates(site.StandCoordinates);
        var bestSteps = int.MaxValue;
        var bestHidden = false;
        var found = false;

        foreach (var (tile, steps) in _floodSteps)
        {
            if (hunt.RoomTiles.Contains(tile) || hunt.ThresholdTiles.Contains(tile))
                continue;

            var distance = ((Vector2) (tile - site.Tile)).Length();
            if (distance < MinStandOff || distance > MaxStandOff)
                continue;

            var tileCoordinates = GetTileCoordinates(grid, tile);
            var hidden = !_npcLineOfSightSystem.InLineOfSight(standMapCoordinates, _transformSystem.ToMapCoordinates(tileCoordinates), MaxStandOff + 1f);

            if (found && (bestHidden && !hidden || bestHidden == hidden && steps >= bestSteps))
                continue;

            coordinates = tileCoordinates;
            bestSteps = steps;
            bestHidden = hidden;
            found = true;
        }

        return found;
    }

    /// <summary>
    ///     One member sticks a charge on, the rest get clear of it, and it goes off. Returns whether it has done its job.
    /// </summary>
    private bool RunCharge(EntityUid issuerUid, NpcSquadPlan plan, NpcSquadAction action, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        if (plan.Charge == null)
        {
            if ((action == NpcSquadAction.BlowDoor ? plan.DoorCharge : plan.WallCharge) is not { } chosen)
            {
                FailCharge(issuerUid, plan, action, settings, now);
                return false;
            }

            plan.Charge = new NpcSquadCharge { Pick = chosen };
        }

        var charge = plan.Charge;
        var pick = charge.Pick;
        var chargeUid = pick.Charge.ChargeUid;
        var targetUid = pick.Site.TargetUid;

        if (charge.Phase == NpcSquadChargePhase.Planting)
        {
            if (_npcChargeSystem.IsPlanted(chargeUid, targetUid))
            {
                charge.Phase = NpcSquadChargePhase.Clearing;
                charge.PlantedAt = now;
            }
            else if (TerminatingOrDeleted(chargeUid) ||
                TerminatingOrDeleted(targetUid) ||
                !_members.Contains(pick.PlanterUid) ||
                now - plan.StepStartedAt >= settings.PlanChargeTimeout)
            {
                FailCharge(issuerUid, plan, action, settings, now);
                return false;
            }
            else
            {
                GiveOrdersWhilePlanting(issuerUid, plan, charge, now);
                return false;
            }
        }

        if (charge.Phase == NpcSquadChargePhase.Clearing)
        {
            if (TerminatingOrDeleted(chargeUid))
            {
                charge.Phase = NpcSquadChargePhase.Blasted;
                charge.BlastedAt = now;
            }
            else if (IsChargeLost(charge, now))
            {
                FailCharge(issuerUid, plan, action, settings, now);
                return false;
            }
            else
            {
                GiveOrdersWhileClearing(issuerUid, plan, charge, now);
                return false;
            }
        }

        if (now - charge.BlastedAt < BlastTime)
            return false;

        if (!TerminatingOrDeleted(targetUid))
        {
            FailCharge(issuerUid, plan, action, settings, now);
            return false;
        }

        FinishCharge(plan, charge, settings);
        return true;
    }

    /// <summary>
    ///     Whether a charge that is on will not now go off: its wire has gone, nobody is left to press it, or it is overdue.
    /// </summary>
    private bool IsChargeLost(NpcSquadCharge charge, TimeSpan now)
    {
        var pick = charge.Pick;

        if (pick.Charge.ActivatorUid is not { } activatorUid)
            return now - charge.PlantedAt >= pick.Charge.Fuse + ChargeOverdue;

        return !_members.Contains(pick.PlanterUid) ||
            !_npcChargeSystem.IsWireIntact(pick.Charge.ChargeUid, activatorUid) ||
            now - charge.PlantedAt >= ClearingTime + ChargeOverdue;
    }

    private void GiveOrdersWhilePlanting(EntityUid issuerUid, NpcSquadPlan plan, NpcSquadCharge charge, TimeSpan now)
    {
        var pick = charge.Pick;
        var site = pick.Site;
        var hunt = plan.Hunt!;

        foreach (var memberUid in _members)
        {
            if (memberUid == pick.PlanterUid)
            {
                SetOrder(memberUid, issuerUid, NpcOrderKind.PlantCharge, site.StandCoordinates, site.LocalFacing, PlantOrderRange, site.TargetUid, now, toolUid: pick.Charge.ChargeUid);
                continue;
            }

            if (hunt.StagedMembers.TryGetValue(memberUid, out var staging))
            {
                var entrance = hunt.Entrances[staging.EntranceIndex];
                SetOrder(memberUid, issuerUid, NpcOrderKind.Stage, entrance.StageCoordinates, entrance.LocalFacing, OrderRange, null, now);
            }
            else if (memberUid != plan.WatcherUid)
            {
                HoldStill(memberUid, issuerUid, site.Tile, hunt, now);
            }
        }
    }

    private void GiveOrdersWhileClearing(EntityUid issuerUid, NpcSquadPlan plan, NpcSquadCharge charge, TimeSpan now)
    {
        var pick = charge.Pick;
        var site = pick.Site;
        var hunt = plan.Hunt!;
        var chargeCoordinates = GetTileCoordinates(hunt.GridUid!.Value, site.Tile);
        var everyoneClear = true;

        foreach (var memberUid in _members)
        {
            if (memberUid == pick.PlanterUid && pick.Charge.ActivatorUid != null)
                continue;

            if (!Transform(memberUid).Coordinates.TryDistance(EntityManager, _transformSystem, chargeCoordinates, out var distance) ||
                distance >= ChargeDangerRange)
            {
                HoldStill(memberUid, issuerUid, site.Tile, hunt, now);
                continue;
            }

            // Out of sight of the floor in front of it. Of the charge itself would do for nobody: nothing can see into a wall.
            SetOrder(memberUid, issuerUid, NpcOrderKind.TakeCover, site.StandCoordinates, Angle.Zero, CoverOrderRange, null, now);
            everyoneClear &= !_npcLineOfSightSystem.InLineOfSight(_transformSystem.GetMapCoordinates(memberUid), _transformSystem.ToMapCoordinates(site.StandCoordinates), ChargeDangerRange);
        }

        if (pick.Charge.ActivatorUid is not { } activatorUid)
            return;

        var planterCoordinates = Transform(pick.PlanterUid).Coordinates;
        var inPlace = planterCoordinates.TryDistance(EntityManager, _transformSystem, pick.StandOffCoordinates, out var fromSpot) && fromSpot <= StandOffRange;
        var facing = (chargeCoordinates.Position - pick.StandOffCoordinates.Position).ToWorldAngle();

        if (inPlace && (everyoneClear || now - charge.PlantedAt >= ClearingTime) || now - charge.PlantedAt >= ClearingTime + ClearingTime / 2)
            SetOrder(pick.PlanterUid, issuerUid, NpcOrderKind.Detonate, planterCoordinates, facing, OrderRange, null, now, toolUid: activatorUid);
        else
            SetOrder(pick.PlanterUid, issuerUid, NpcOrderKind.Stage, pick.StandOffCoordinates, facing, StandOffRange, null, now);
    }

    /// <summary>
    ///     Far enough from what is going on: stays where it is, facing it.
    /// </summary>
    private void HoldStill(EntityUid memberUid, EntityUid issuerUid, Vector2i tile, NpcHunt hunt, TimeSpan now)
    {
        var standCoordinates = _transformSystem.GetMoverCoordinates(memberUid);
        var tileCoordinates = _transformSystem.ToMapCoordinates(GetTileCoordinates(hunt.GridUid!.Value, tile));
        var facing = (tileCoordinates.Position - _transformSystem.GetMapCoordinates(memberUid).Position).ToWorldAngle();

        SetOrder(memberUid, issuerUid, NpcOrderKind.Watch, standCoordinates, ToLocalFacing(standCoordinates, facing), OrderRange, null, now);
    }

    private void FailCharge(EntityUid issuerUid, NpcSquadPlan plan, NpcSquadAction action, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        if (plan.Charge?.Pick.Charge.ActivatorUid is { } activatorUid)
            _npcChargeSystem.LetGoOfWire(activatorUid);

        plan.Charge = null;
        FailPlanStep(issuerUid, plan, action, settings, now);
    }

    /// <summary>
    ///     The blast did its job. Anything inside close enough is in no state to fight, and a hole in a wall is a way in like any
    ///         other from here on. Everyone is near enough to go straight in, while the dust is up.
    /// </summary>
    private void FinishCharge(NpcSquadPlan plan, NpcSquadCharge charge, NpcSquadTacticsSettings settings)
    {
        var hunt = plan.Hunt!;
        var site = charge.Pick.Site;

        if (!TerminatingOrDeleted(plan.TargetUid) &&
            _transformSystem.GetMoverCoordinates(plan.TargetUid).TryDistance(EntityManager, _transformSystem, GetTileCoordinates(hunt.GridUid!.Value, site.Tile), out var distance) &&
            distance <= settings.PlanChargeBlastRange)
            plan.Softened = true;

        // Whatever was in the way is gone, and nothing should go asking it whether it opens.
        for (var e = 0; e < hunt.Entrances.Count; e++)
        {
            if (hunt.Entrances[e].DoorUid == site.TargetUid)
                hunt.Entrances[e] = hunt.Entrances[e] with { DoorUid = null };
        }

        if (!site.IsDoor)
        {
            hunt.ThresholdTiles.Add(site.Tile);
            hunt.Entrances.Add(new NpcHuntEntrance(site.StandCoordinates, site.InsideCoordinates, site.StandTile, site.LocalFacing, null));
            AssignEntrances(hunt, settings);
            LeaveAWatcher(plan);
        }

        plan.Stacked = true;
        plan.Charge = null;
    }
}
