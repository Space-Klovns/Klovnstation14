using Content.Server._KS14.NPC.Hands;
using Content.Server._KS14.NPC.Doors;
using Content.Server._KS14.NPC.Meters;
using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Recovery;
using Content.Server._KS14.NPC.Systems;
using Content.Server._KS14.NPC.Utility;
using Content.Shared._KS14.NPC;
using Content.Shared.Doors.Components;
using Content.Shared.GameTicking;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Squad.Tactics;

/// <summary>
///     A squad's leader, thinking for the squad. Turns what its members know into an order for each of them: hunt a
///         hostile the squad has lost - watch for it, go after it, stack up on the room it went into, go in together,
///         check everywhere it could be hiding - and, when nothing is going on, get back together.
/// </summary>
/// <remarks>
///     <para>
///         Only squads with a leader get orders, and only NPCs that can lead found squads, so a squad of followers
///             with nobody to lead it does not exist: each of them hunts on its own instead, as a squad of one, and
///             never stacks up or flanks.
///     </para>
///     <para>
///         This is the Sense half of the HTN contract (see the npc-htn skill): orders are kept on
///             <see cref="NpcOrderComponent"/>, HTN reads them through <c>GetOrderOperator</c> and friends, and a
///             changed order asks for a replan. Combat always comes first - a member that can see a hostile fights
///             it, whatever it was told - and a hostile seen again calls the hunt off.
///     </para>
/// </remarks>
public sealed partial class NpcSquadTacticsSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private Content.Shared.Power.EntitySystems.SharedPowerReceiverSystem _powerReceiverSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private NpcMeterSystem _npcMeterSystem = default!;
    [Dependency] private NpcHuntDebugSystem _npcHuntDebugSystem = default!;
    [Dependency] private NpcLineOfSightSystem _npcLineOfSightSystem = default!;
    [Dependency] private NpcPerceptionSystem _npcPerceptionSystem = default!;
    [Dependency] private NpcRecoverySystem _npcRecoverySystem = default!;
    [Dependency] private NpcSensorSystem _npcSensorSystem = default!;
    [Dependency] private NpcGridSystem _npcGridSystem = default!;
    [Dependency] private NpcSquadCoverSystem _npcSquadCoverSystem = default!;
    [Dependency] private NpcSquadSystem _npcSquadSystem = default!;
    [Dependency] private NpcDoorSystem _npcDoorSystem = default!;
    [Dependency] private NpcHandsSystem _npcHandsSystem = default!;
    [Dependency] private SharedEntityStorageSystem _entityStorageSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<NpcHuntComponent> _huntQuery = default!;
    [Dependency] private EntityQuery<NpcPendingDisturbanceComponent> _pendingDisturbanceQuery = default!;
    [Dependency] private EntityQuery<NpcOrderComponent> _orderQuery = default!;
    [Dependency] private EntityQuery<NpcSquadMemberComponent> _squadMemberQuery = default!;
    [Dependency] private EntityQuery<NpcPerceptionComponent> _perceptionQuery = default!;
    [Dependency] private EntityQuery<MapGridComponent> _mapGridQuery = default!;
    [Dependency] private EntityQuery<EntityStorageComponent> _entityStorageQuery = default!;
    [Dependency] private EntityQuery<DoorComponent> _doorQuery = default!;
    [Dependency] private EntityQuery<AirlockComponent> _airlockQuery = default!;

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(0.5);

    /// <summary>
    ///     How far an order's spot has to move, in tiles, to be a new order rather than the same one restated.
    /// </summary>
    private const float OrderMoveTolerance = 0.5f;

    /// <summary>
    ///     How far an order's facing has to turn, in degrees, to be a new order.
    /// </summary>
    private const double OrderTurnTolerance = 20;

    private TimeSpan _nextUpdate;

    private int _lastOrderId;

    /// <summary>
    ///     The members of the group being updated: a squad's, or one NPC on its own.
    /// </summary>
    private readonly List<EntityUid> _members = new();

    /// <summary>
    ///     Members of the group being updated that a plan has left out of <see cref="_members"/> while their meds work. Still
    ///         standing where they were, so still in the way of a grenade.
    /// </summary>
    private readonly List<EntityUid> _recovering = new();

    /// <summary>
    ///     Stops the periodic update, so a test can give orders by hand without them being taken straight back.
    ///         <see cref="UpdateNow()"/> still runs.
    /// </summary>
    internal bool UpdatesPaused;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _gameTiming.CurTime;
        if (UpdatesPaused || !NpcThrottle.TryConsume(ref _nextUpdate, now, UpdateInterval))
            return;

        UpdateNow(now);
    }

    /// <summary>
    ///     A test that paused the system does not leave it paused for whatever runs on the same server next.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        UpdatesPaused = false;
    }

    /// <summary>
    ///     Runs one update for every squad and lone NPC right now, whether or not one is due. For tests.
    /// </summary>
    internal void UpdateNow()
    {
        UpdateNow(_gameTiming.CurTime);
    }

    private void UpdateNow(TimeSpan now)
    {
        var squadEnumerator = EntityQueryEnumerator<NpcSquadComponent>();
        while (squadEnumerator.MoveNext(out var squadUid, out var squadComponent))
        {
            _members.Clear();
            _recovering.Clear();

            foreach (var memberUid in squadComponent.Members)
            {
                // A hunt it started on its own, before it joined: the squad's hunt is the one that counts now.
                if (_huntQuery.HasComp(memberUid))
                    RemComp<NpcHuntComponent>(memberUid);

                // And a plan, which may have it tied to a charge's wire.
                DropPlan(memberUid);

                if (_perceptionQuery.HasComp(memberUid) && _mobStateSystem.IsAlive(memberUid))
                    _members.Add(memberUid);
            }

            if (!_npcSquadSystem.TryGetLeader((squadUid, squadComponent), out var leader) ||
                _members.Count == 0)
            {
                ClearOrders(squadUid, _members);
                DropPlan(squadUid);
                continue;
            }

            UpdateGroup(squadUid, leader.Owner, leader.Comp.Tactics, now);
        }

        var lonerEnumerator = EntityQueryEnumerator<NpcSquadMemberComponent, NpcPerceptionComponent, ActiveNPCComponent>();
        while (lonerEnumerator.MoveNext(out var uid, out var squadMemberComponent, out _, out _))
        {
            if (squadMemberComponent.Squad != null || !_mobStateSystem.IsAlive(uid))
                continue;

            _members.Clear();
            _recovering.Clear();
            _members.Add(uid);
            UpdateGroup(uid, leaderUid: null, squadMemberComponent.Tactics, now);
        }
    }

    /// <summary>
    ///     Works out orders for <see cref="_members"/>.
    /// </summary>
    /// <param name="issuerUid">Whoever holds the hunt and issues the orders: the squad, or the lone NPC itself.</param>
    /// <param name="leaderUid">The squad's leader. Null for an NPC on its own.</param>
    private void UpdateGroup(EntityUid issuerUid, EntityUid? leaderUid, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        ClearForeignOrders(issuerUid);

        // A hostile nobody can get at is the one fight that needs orders, so this comes before the check below.
        if (UpdateSquadPlan(issuerUid, leaderUid, settings, now))
        {
            EndFight(issuerUid, settings, now);
            return;
        }

        // A fight on: combat comes first, and a hostile in sight is not lost. What the squad does together in it, it does
        //      over that (see NpcSquadTacticsSystem.Fight.cs).
        // The gather that finds the fight's fighters answers whether anyone is in one, rather than walking the contacts twice.
        //      A group too small to push or flank, with neither going, has no use for the fighters and only asks. One going
        //      is kept up or ended whatever the settings say now: they are the leader's, and a new leader that does neither
        //      would otherwise leave the old one's flankers on their orders for as long as the fight lasts.
        var gathering = (settings.Pushing || settings.Flanking) && _members.Count >= 2 || IsFightLive(issuerUid);
        if (gathering ? GatherFight() : AnyoneInCombat())
        {
            EndHunt(issuerUid, keepFightOrders: true);

            if (gathering)
                UpdateFight(issuerUid, leaderUid, settings, now);

            return;
        }

        // Nobody in sight of it, but a flank is on and the front only ducked a moment ago: the flankers keep going, and the
        //      hunt waits. It goes by when the hostile was lost, so waiting costs it nothing.
        if (KeepFlankWhileUnseen(issuerUid, now))
            return;

        EndFight(issuerUid, settings, now);

        // Waiting for its meds to work: it will not come, so nothing below may count on it or wait for it. Only after
        //      the check above, because one with a hostile in sight is still in the fight. Kept aside, as a plan keeps them:
        //      a hunt that softens a room goes by them too (the blast, the gas, what they last saw).
        RemoveRecovering(issuerUid, movedTo: _recovering);

        if (_members.Count == 0)
            return;

        _huntQuery.TryComp(issuerUid, out var huntComponent);
        var hunt = huntComponent?.Hunt;

        // Over once one of them knows it is dead - not the moment it dies, out of sight, where none of them could know.
        if (hunt?.TargetUid is { } huntedUid && IsHuntedGone(huntedUid))
        {
            EndHunt(issuerUid);
            hunt = null;
        }

        if (hunt == null && !TryStartHunt(issuerUid, settings, now, out hunt))
            TryStartDisturbanceHunt(issuerUid, leaderUid, settings, now, out hunt);

        if (hunt != null)
        {
            UpdateHunt(issuerUid, leaderUid, hunt, settings, now);

            // What the update just worked out, for anyone watching - and nothing built if nobody is.
            if (GetHunt(issuerUid) == hunt && _npcHuntDebugSystem.IsTracking(issuerUid, _members))
                _npcHuntDebugSystem.SendFrame(issuerUid, hunt, _members);

            return;
        }

        UpdateRegroup(issuerUid, leaderUid, settings, now);
    }

    /// <summary>
    ///     Clears the orders from somewhere else - the NPC's own, from before it joined this squad, or an old squad's.
    /// </summary>
    private void ClearForeignOrders(EntityUid issuerUid)
    {
        foreach (var memberUid in _members)
        {
            if (_orderQuery.TryComp(memberUid, out var orderComponent) &&
                orderComponent.Order is { } order &&
                order.IssuerUid != issuerUid)
                ClearOrder((memberUid, orderComponent));
        }
    }

    private bool AnyoneInCombat()
    {
        foreach (var memberUid in _members)
        {
            if (_npcPerceptionSystem.InCombatContact(memberUid))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Takes the members who are waiting for their meds to work out of <see cref="_members"/>, clearing the orders
    ///         <paramref name="issuerUid"/> gave them, and adds them to <paramref name="movedTo"/> if given. Returns how many.
    /// </summary>
    private int RemoveRecovering(EntityUid issuerUid, List<EntityUid>? movedTo)
    {
        var removed = 0;

        for (var i = _members.Count - 1; i >= 0; i--)
        {
            var memberUid = _members[i];
            if (!_npcRecoverySystem.IsRecovering(memberUid))
                continue;

            ClearOrder(memberUid, issuerUid);
            movedTo?.Add(memberUid);
            _members.RemoveAt(i);
            removed++;
        }

        return removed;
    }

    private bool AnyoneKnowsDead(EntityUid targetUid)
    {
        foreach (var memberUid in _members)
        {
            if (_npcPerceptionSystem.IsKnownDead(memberUid, targetUid))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     The hunted hostile is gone, or one of the members knows it is dead - not the moment it dies, out of sight, where none
    ///         of them could know. Not <see cref="IsHostileDown"/> (an incapacitated hostile is still hunted) nor
    ///         <see cref="IsHostileOutOfAction"/> (which does not ask what the members know): the three are kept distinct.
    /// </summary>
    private bool IsHuntedGone(EntityUid huntedUid)
    {
        return TerminatingOrDeleted(huntedUid) || AnyoneKnowsDead(huntedUid);
    }

    /// <summary>
    ///     Who walks for the group where it matters: the leader, or the first member for one on its own.
    /// </summary>
    private EntityUid GetWalker(EntityUid? leaderUid)
    {
        return leaderUid ?? _members[0];
    }

    #region Orders

    /// <summary>
    ///     The order <paramref name="uid"/> has, if any.
    /// </summary>
    public bool TryGetOrder(Entity<NpcOrderComponent?> entity, out NpcOrder order)
    {
        order = default;

        if (!_orderQuery.Resolve(entity.Owner, ref entity.Comp, false) || entity.Comp.Order is not { } current)
            return false;

        order = current;
        return true;
    }

    /// <summary>
    ///     The order <paramref name="ownerUid"/> has, if it is the one with <paramref name="orderId"/>.
    /// </summary>
    public bool TryGetCurrentOrder(EntityUid ownerUid, int orderId, out NpcOrder order)
    {
        return TryGetOrder(ownerUid, out order) && order.Id == orderId;
    }

    /// <summary>
    ///     Whether the order <paramref name="ownerUid"/> has is the one with <paramref name="orderId"/>.
    /// </summary>
    public bool IsOrderCurrent(EntityUid ownerUid, int orderId)
    {
        return TryGetCurrentOrder(ownerUid, orderId, out _);
    }

    /// <summary>
    ///     Tells the tactics that <paramref name="uid"/> has just learned of a disturbance at
    ///         <paramref name="coordinates"/>: something happened there, with nobody seen. Kept for its squad, or for it
    ///         if it is on its own, until the next update decides what to do about it. A cautious squad hunts it (see
    ///         <see cref="NpcSquadTacticsSettings.CautiousHuntThreshold"/>); otherwise nothing changes, and the squad
    ///         goes to it as it always has.
    /// </summary>
    public void NoteDisturbance(EntityUid uid, EntityCoordinates coordinates)
    {
        if (TerminatingOrDeleted(coordinates.EntityId))
            return;

        var issuerUid = _npcSquadSystem.TryGetSquad(uid, out var squadEntity) ? squadEntity.Value.Owner : uid;
        var disturbanceComponent = EnsureComp<NpcPendingDisturbanceComponent>(issuerUid);

        // Relative to the grid or map, not to whatever made the noise.
        disturbanceComponent.Coordinates = _transformSystem.GetMoverCoordinates(coordinates);
        disturbanceComponent.ReportedAt = _gameTiming.CurTime;
        disturbanceComponent.CheckedReachable = false;
    }

    /// <summary>
    ///     The hunt <paramref name="issuerUid"/> - a squad, or an NPC on its own - is on, if any.
    /// </summary>
    public NpcHunt? GetHunt(EntityUid issuerUid)
    {
        return _huntQuery.TryComp(issuerUid, out var huntComponent) ? huntComponent.Hunt : null;
    }

    /// <summary>
    ///     Gives <paramref name="memberUid"/> an order from itself, as if it had worked it out on its own. For tests;
    ///         <see cref="UpdatesPaused"/> keeps it from being cleared again.
    /// </summary>
    internal void IssueOrder(EntityUid memberUid,
        NpcOrderKind kind,
        EntityCoordinates coordinates,
        EntityUid? targetUid = null,
        EntityUid? toolUid = null,
        bool overCombat = false)
    {
        using var planOrders = GivePlanOrders(overCombat);
        SetOrder(memberUid, memberUid, kind, coordinates, Angle.Zero, OrderRange, targetUid, _gameTiming.CurTime, toolUid: toolUid);
    }

    /// <summary>
    ///     Gives <paramref name="memberUid"/> an order, and has it replan for it - unless it already has this one, in
    ///         which case nothing changes, the order keeps its id, and whatever the member is doing about it carries on.
    /// </summary>
    private void SetOrder(EntityUid memberUid,
        EntityUid issuerUid,
        NpcOrderKind kind,
        EntityCoordinates coordinates,
        Angle facing,
        float range,
        EntityUid? targetUid,
        TimeSpan now,
        EntityUid? toolUid = null)
    {
        var orderComponent = EnsureComp<NpcOrderComponent>(memberUid);

        if (orderComponent.Order is { } existing &&
            existing.Kind == kind &&
            existing.IssuerUid == issuerUid &&
            existing.TargetUid == targetUid &&
            existing.ToolUid == toolUid &&
            existing.OverCombat == _givingPlanOrders &&
            existing.Range == range && // how near is enough is part of the order: one told to come closer has to replan for it
            Math.Abs(Angle.ShortestDistance(existing.Facing, facing).Degrees) < OrderTurnTolerance &&
            existing.Coordinates.TryDistance(EntityManager, _transformSystem, coordinates, out var moved) &&
            moved < OrderMoveTolerance)
            return;

        // Forcing a door on its way, the new order waits: it would end the move, the breach with it, and the move it gives
        //      would likely start the same breach over - for good, on a search that keeps moving members on. Orders are
        //      given again every update, so it gets this one once the door is open or the breach has failed. Taking cover
        //      from a blast does not wait.
        if (orderComponent.Order != null && kind != NpcOrderKind.TakeCover && _npcDoorSystem.IsBreachingOnTheWay(memberUid))
            return;

        orderComponent.Order = new NpcOrder(++_lastOrderId, kind, issuerUid, coordinates, facing, range, targetUid, now, toolUid, _givingPlanOrders);
        _npcSensorSystem.RequestReplan(memberUid);
    }

    private void ClearOrder(Entity<NpcOrderComponent> entity)
    {
        if (entity.Comp.Order == null)
            return;

        entity.Comp.Order = null;
        _npcSensorSystem.RequestReplan(entity.Owner);
    }

    /// <summary>
    ///     Clears every order <paramref name="issuerUid"/> gave to <paramref name="memberUids"/>.
    /// </summary>
    private void ClearOrders(EntityUid issuerUid, List<EntityUid> memberUids)
    {
        foreach (var memberUid in memberUids)
        {
            ClearOrder(memberUid, issuerUid);
        }
    }

    private void ClearOrder(EntityUid memberUid, EntityUid issuerUid)
    {
        if (_orderQuery.TryComp(memberUid, out var orderComponent))
            ClearOrder((memberUid, orderComponent), issuerUid);
    }

    private void ClearOrder(Entity<NpcOrderComponent> entity, EntityUid issuerUid)
    {
        if (entity.Comp.Order?.IssuerUid == issuerUid)
            ClearOrder(entity);
    }

    /// <summary>
    ///     Marks the orders given until it is disposed as ones for a plan (<see cref="NpcOrder.OverCombat"/>), or as ordinary ones
    ///         with <paramref name="overCombat"/> false. Used with <c>using</c>, so the mark always comes off again.
    /// </summary>
    private PlanOrderScope GivePlanOrders(bool overCombat = true)
    {
        return new PlanOrderScope(this, overCombat);
    }

    private readonly ref struct PlanOrderScope
    {
        private readonly NpcSquadTacticsSystem _system;

        public PlanOrderScope(NpcSquadTacticsSystem system, bool overCombat)
        {
            _system = system;
            system._givingPlanOrders = overCombat;
        }

        public void Dispose()
        {
            _system._givingPlanOrders = false;
        }
    }

    /// <summary>
    ///     Tells <paramref name="memberUid"/> to stay where it stands, facing <paramref name="worldFacing"/>.
    /// </summary>
    private void OrderWatch(EntityUid memberUid, EntityUid issuerUid, Angle worldFacing, TimeSpan now)
    {
        var standCoordinates = _transformSystem.GetMoverCoordinates(memberUid);
        SetOrder(memberUid, issuerUid, NpcOrderKind.Watch, standCoordinates, ToLocalFacing(standCoordinates, worldFacing), OrderRange, null, now);
    }

    /// <summary>
    ///     Tells <paramref name="memberUid"/> to stay where it stands, facing <paramref name="target"/>. The angle is the plain
    ///         one from the member: see the overload with a fallback for a target that may be on another map or on the spot.
    /// </summary>
    private void OrderWatchToward(EntityUid memberUid, EntityUid issuerUid, MapCoordinates target, TimeSpan now)
    {
        var facing = (target.Position - _transformSystem.GetMapCoordinates(memberUid).Position).ToWorldAngle();
        OrderWatch(memberUid, issuerUid, facing, now);
    }

    /// <summary>
    ///     The same, but facing <paramref name="fallback"/> if <paramref name="target"/> is on another map or on the spot
    ///         (see <see cref="GetFacing"/>).
    /// </summary>
    private void OrderWatchToward(EntityUid memberUid, EntityUid issuerUid, MapCoordinates target, Angle fallback, TimeSpan now)
    {
        OrderWatch(memberUid, issuerUid, GetFacing(_transformSystem.GetMapCoordinates(memberUid), target, fallback), now);
    }

    /// <summary>
    ///     Sends <paramref name="memberUid"/> to the spot in front of its way in <paramref name="entrance"/> (or inside it with
    ///         <paramref name="useEntry"/>), facing into the room, with an order of <paramref name="kind"/>.
    /// </summary>
    private void OrderAtEntrance(EntityUid memberUid, EntityUid issuerUid, NpcHuntEntrance entrance, NpcOrderKind kind, bool useEntry, TimeSpan now)
    {
        var coordinates = useEntry ? entrance.EntryCoordinates : entrance.StageCoordinates;
        SetOrder(memberUid, issuerUid, kind, coordinates, entrance.LocalFacing, OrderRange, null, now);
    }

    /// <summary>
    ///     <see cref="OrderAtEntrance"/> for a member that has a way in in <paramref name="hunt"/>. Returns false, and orders
    ///         nothing, for one that has not.
    /// </summary>
    private bool TryOrderAtEntrance(EntityUid memberUid, EntityUid issuerUid, NpcHunt hunt, NpcOrderKind kind, bool useEntry, TimeSpan now)
    {
        if (!hunt.StagedMembers.TryGetValue(memberUid, out var staging))
            return false;

        OrderAtEntrance(memberUid, issuerUid, hunt.Entrances[staging.EntranceIndex], kind, useEntry, now);
        return true;
    }

    private bool HasOrder(EntityUid memberUid, NpcOrderKind kind)
    {
        return _orderQuery.TryComp(memberUid, out var orderComponent) && orderComponent.Order?.Kind == kind;
    }

    #endregion

    /// <summary>
    ///     <paramref name="worldFacing"/> as an order's facing: relative to whatever <paramref name="coordinates"/> are
    ///         relative to - a grid, usually - so it still points the same way into the room after a moving grid turns.
    /// </summary>
    private Angle ToLocalFacing(EntityCoordinates coordinates, Angle worldFacing)
    {
        return TerminatingOrDeleted(coordinates.EntityId)
            ? worldFacing
            : worldFacing - _transformSystem.GetWorldRotation(coordinates.EntityId);
    }

    /// <summary>
    ///     The world angle from <paramref name="from"/> to <paramref name="to"/>, or <paramref name="fallback"/> if they
    ///         are on different maps or in the same spot.
    /// </summary>
    private Angle GetFacing(MapCoordinates from, MapCoordinates to, Angle fallback)
    {
        var direction = to.Position - from.Position;
        return from.MapId != to.MapId || direction.LengthSquared() < 0.01f ? fallback : direction.ToWorldAngle();
    }
}
