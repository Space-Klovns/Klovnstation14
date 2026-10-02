using Content.Server._KS14.NPC.Meters;
using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Systems;
using Content.Shared._KS14.NPC;
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
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private NpcMeterSystem _npcMeterSystem = default!;
    [Dependency] private NpcHuntDebugSystem _npcHuntDebugSystem = default!;
    [Dependency] private NpcLineOfSightSystem _npcLineOfSightSystem = default!;
    [Dependency] private NpcPerceptionSystem _npcPerceptionSystem = default!;
    [Dependency] private NpcSensorSystem _npcSensorSystem = default!;
    [Dependency] private NpcSquadCoverSystem _npcSquadCoverSystem = default!;
    [Dependency] private NpcSquadSystem _npcSquadSystem = default!;
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
    ///     Stops the periodic update, so a test can give orders by hand without them being taken straight back.
    ///         <see cref="UpdateNow()"/> still runs.
    /// </summary>
    internal bool UpdatesPaused;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _gameTiming.CurTime;
        if (UpdatesPaused || now < _nextUpdate)
            return;

        _nextUpdate = now + UpdateInterval;
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

            foreach (var memberUid in squadComponent.Members)
            {
                // A hunt it started on its own, before it joined: the squad's hunt is the one that counts now.
                if (_huntQuery.HasComp(memberUid))
                    RemComp<NpcHuntComponent>(memberUid);

                if (_perceptionQuery.HasComp(memberUid) && _mobStateSystem.IsAlive(memberUid))
                    _members.Add(memberUid);
            }

            if (squadComponent.Leader is not { } leaderUid ||
                !_squadMemberQuery.TryComp(leaderUid, out var leaderSquadMemberComponent) ||
                _members.Count == 0)
            {
                ClearOrders(squadUid, _members);
                continue;
            }

            UpdateGroup(squadUid, leaderUid, leaderSquadMemberComponent.Tactics, now);
        }

        var lonerEnumerator = EntityQueryEnumerator<NpcSquadMemberComponent, NpcPerceptionComponent, ActiveNPCComponent>();
        while (lonerEnumerator.MoveNext(out var uid, out var squadMemberComponent, out _, out _))
        {
            if (squadMemberComponent.Squad != null || !_mobStateSystem.IsAlive(uid))
                continue;

            _members.Clear();
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
        // Orders from somewhere else - the NPC's own, from before it joined this squad, or an old squad's.
        foreach (var memberUid in _members)
        {
            if (_orderQuery.TryComp(memberUid, out var orderComponent) &&
                orderComponent.Order is { } order &&
                order.IssuerUid != issuerUid)
                ClearOrder((memberUid, orderComponent));
        }

        // A fight on: combat comes first, and a hostile in sight is not lost.
        foreach (var memberUid in _members)
        {
            if (!_npcPerceptionSystem.InCombatContact(memberUid))
                continue;

            EndHunt(issuerUid);
            return;
        }

        _huntQuery.TryComp(issuerUid, out var huntComponent);
        var hunt = huntComponent?.Hunt;

        if (hunt?.TargetUid is { } huntedUid && (TerminatingOrDeleted(huntedUid) || _mobStateSystem.IsDead(huntedUid)))
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
    internal void IssueOrder(EntityUid memberUid, NpcOrderKind kind, EntityCoordinates coordinates, EntityUid? storageUid = null)
    {
        SetOrder(memberUid, memberUid, kind, coordinates, Angle.Zero, OrderRange, storageUid, _gameTiming.CurTime);
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
        EntityUid? storageUid,
        TimeSpan now)
    {
        var orderComponent = EnsureComp<NpcOrderComponent>(memberUid);

        if (orderComponent.Order is { } existing &&
            existing.Kind == kind &&
            existing.IssuerUid == issuerUid &&
            existing.StorageUid == storageUid &&
            Math.Abs(Angle.ShortestDistance(existing.Facing, facing).Degrees) < OrderTurnTolerance &&
            existing.Coordinates.TryDistance(EntityManager, _transformSystem, coordinates, out var moved) &&
            moved < OrderMoveTolerance)
            return;

        orderComponent.Order = new NpcOrder(++_lastOrderId, kind, issuerUid, coordinates, facing, range, storageUid, now);
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
        if (_orderQuery.TryComp(memberUid, out var orderComponent) && orderComponent.Order?.IssuerUid == issuerUid)
            ClearOrder((memberUid, orderComponent));
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
