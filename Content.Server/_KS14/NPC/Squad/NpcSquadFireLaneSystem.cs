using System.Numerics;
using Content.Server._KS14.NPC.Components;
using Content.Server._KS14.NPC.LineOfFire;
using Content.Server._KS14.NPC.Systems;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.FactionCollision;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Physics;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Squad;

/// <summary>
///     A line an NPC is (or will be) shooting along: from where it stands to what it covers.
/// </summary>
public readonly record struct NpcFireLane(Vector2 From, Vector2 To);

/// <summary>
///     Keeps NPCs' rounds off their friends, gated behind <see cref="KsCCVars.NpcSquadFireLanes"/>.
///     <list type="bullet">
///         <item>Choosing where to stand, both ways: a spot is penalised if its own line to what it covers passes through a
///             teammate, and if it sits on a teammate's line. A teammate on top of the spot is in both.</item>
///         <item>Shooting: a round goes where the gun sends it, not down a line. Each friend it could hit blocks the
///             directions it could be hit in (see <see cref="GatherFriends"/>): as wide as the friend, and the gun's spread on
///             top (<see cref="NpcShotCone"/>); where the friend will be by the time a round gets there, or the last of a
///             burst; and anywhere along the way, short of a wall, as a miss flies on past what it was aimed at. A friend
///             right by the shooter blocks the directions a round starts inside it in. <see cref="TryGetClearShot"/> takes
///             the aim if that is clear, or else the nearest clear direction to it that still does the job: a round within
///             a tolerance of the aim, which for a target is its body and for suppression is where the hostile could be.
///             With none, it holds fire. Any friend, not only squadmates: an NPC fights before it gets round to joining a
///             squad. Held for long enough where it stands (<see cref="NoteHeldFire"/>), it gives up its spot for another.</item>
///         <item>And every round as it is fired (<see cref="IsShotClear"/>), however it was fired: the rest of a burst
///             goes off by itself, after the friend it was clear of may have stepped in.</item>
///     </list>
/// </summary>
public sealed partial class NpcSquadFireLaneSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private GunSystem _gunSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private NpcFactionSystem _npcFactionSystem = default!;
    [Dependency] private NpcLineOfFireSystem _npcLineOfFireSystem = default!;
    [Dependency] private NpcSquadSystem _npcSquadSystem = default!;
    [Dependency] private NpcTacticalPositionClaimSystem _npcTacticalPositionClaimSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<KsNpcAimTrackingComponent> _aimTrackingQuery = default!;
    [Dependency] private EntityQuery<FixturesComponent> _fixturesQuery = default!;
    [Dependency] private EntityQuery<KsFactionCollisionShooterComponent> _factionCollisionShooterQuery = default!;
    [Dependency] private EntityQuery<NpcFactionMemberComponent> _factionMemberQuery = default!;
    [Dependency] private EntityQuery<NpcHoldingFireComponent> _holdingFireQuery = default!;
    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;
    [Dependency] private EntityQuery<Content.Server.NPC.Components.NPCSteeringComponent> _steeringQuery = default!;

    /// <summary>
    ///     Score multiplier for a spot that crosses a line of fire. Soft rather than zero: a crossed line beats
    ///         having nowhere to stand at all.
    /// </summary>
    public const float CrossedLanePenalty = 0.2f;

    /// <summary>
    ///     How close to a line of fire, in tiles, counts as standing in it. Roughly a mob's width plus spread.
    /// </summary>
    public const float LaneRadius = 0.6f;

    /// <summary>
    ///     A miss carries on past what was aimed at, so a line extends this far, in tiles, beyond its end.
    /// </summary>
    public const float LaneOverrun = 2f;

    /// <summary>
    ///     How far off a friend can be, in tiles, and still be hit: a miss flies on until something stops it, which is usually
    ///         a wall, and those cut it short friend by friend. Across the biggest room a fight happens in: a squadmate at the far
    ///         side of a hall from a crossfire, 18 tiles on, was hit at 16.
    /// </summary>
    public const float MissReach = 32f;

    /// <summary>
    ///     How far behind the shooter's middle, in tiles, a round reaches as it starts: it is spawned there, and is long. Measured
    ///         (<c>KsNpcFriendlyFireTest.TestMapWhereAShotStarts</c>): a friend 0.6 tiles straight behind is hit, one 0.7 behind
    ///         is not, nor one 0.5 to the side.
    /// </summary>
    public const float ShotBackReach = 0.2f;

    /// <summary>
    ///     A little to spare, in tiles, on how near a friend's edge a round may pass: one that grazes it hits it.
    /// </summary>
    public const float ShotClearance = 0.03f;

    /// <summary>
    ///     How fast a friend may go off the way it was going while a round is on its way, in tiles a second: it starts, stops
    ///         and turns at the corners of its path, which its velocity as the round is fired does not tell. Seen live: a friend
    ///         7 tiles off moved 0.8 tiles across a round's way that its velocity had it clear of, and friends 6 tiles off
    ///         closed 0.6 tiles in 0.15s, turning at the end of a juke: for one already on the move,
    ///         <see cref="MovingFriendManeuverSpeed"/>.
    /// </summary>
    public const float FriendManeuverSpeed = 2.5f;

    /// <summary>
    ///     The same for a friend already on the move: turning about at the end of a juke goes from one way to the other, which
    ///         can be twice one starting from standing.
    /// </summary>
    public const float MovingFriendManeuverSpeed = 4f;

    /// <summary>
    ///     How fast a friend has to be going, in tiles a second, to count as on the move: one that is may turn off its way
    ///         before a round fired this tick gets to it.
    /// </summary>
    private const float MovingSpeed = 0.1f;

    /// <summary>
    ///     How far from its middle a mob is hit, in tiles, for one with no round fixture to read it from: a humanoid's.
    /// </summary>
    public const float DefaultBodyRadius = 0.35f;

    /// <summary>
    ///     How far past the edge of the directions a friend blocks, in radians, a direction to shoot in goes: just clear of it.
    /// </summary>
    private const float EdgeClearance = 0.01f;

    /// <summary>
    ///     How long a member holds fire where it stands before it gives up its spot for another.
    /// </summary>
    public static readonly TimeSpan HoldFireTime = TimeSpan.FromSeconds(0.5);

    /// <summary>
    ///     How long after the last shot held it still counts as holding fire: for planning, which comes after the shooting.
    /// </summary>
    private static readonly TimeSpan HoldFireMemory = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     How far a member holding fire may move and still be holding it where it was.
    /// </summary>
    private const float HoldFireDrift = 0.5f;

    public bool Enabled { get; private set; }

    /// <summary>
    ///     A friend a round from where the shooter stands could hit, by the directions it would be hit in, in radians.
    /// </summary>
    private struct NpcFriendInTheWay
    {
        public Vector2 Position;
        public float Distance;

        /// <summary>
        ///     The directions a round hits it in on its way, <see cref="HalfWidth"/> either side of this: over where it is now
        ///         and where it will be when a round gets there.
        /// </summary>
        public float Bearing;

        public float HalfWidth;

        /// <summary>
        ///     The directions a round starts inside it in, it being just behind: <see cref="RearHalfWidth"/> either side of
        ///         this, straight away from it. None, at 0.
        /// </summary>
        public float RearBearing;

        public float RearHalfWidth;

        /// <summary>
        ///     Whether a round could get to it at all, or would hit a wall first: 1 or -1, once worked out; 0 until then.
        /// </summary>
        public sbyte Reachable;
    }

    private readonly List<NpcFriendInTheWay> _friends = new();

    // What _friends is for: this shooter, standing here, this tick. The check before a shot and the one as it goes off
    //      come in the same tick, and share one lookup.
    private EntityUid _friendsShooterUid;
    private GameTick _friendsTick;
    private MapCoordinates _friendsFrom;
    private bool _friendAtMuzzle;
    private CollisionGroup _friendsShotMask;

    private readonly List<float> _scratchAngles = new();
    private readonly List<NpcFireLane> _scratchLanes = new();
    private readonly HashSet<Entity<MobStateComponent>> _scratchMobs = new();
    private readonly List<Vector2> _scratchPositions = new();

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_configurationManager, KsCCVars.NpcSquadFireLanes, value => Enabled = value, true);

        SubscribeLocalEvent<ActiveNPCComponent, ShotAttemptedEvent>(OnShotAttempted);
    }

    /// <summary>
    ///     Score multiplier in (0, 1] for <paramref name="ownerUid"/> standing at <paramref name="candidate"/> and
    ///         covering <paramref name="aim"/>, against where its squadmates are standing and what they cover.
    ///         1 when disabled or when the owner has no squad.
    /// </summary>
    public float GetFireLanePenalty(EntityUid ownerUid, MapCoordinates candidate, MapCoordinates? aim)
    {
        if (!Enabled || !_npcSquadSystem.TryGetSquad(ownerUid, out var squadEntity))
            return 1f;

        _scratchLanes.Clear();
        _scratchPositions.Clear();

        var squadComponent = squadEntity.Value.Comp;
        var threatMap = squadComponent.ThreatCoordinates is { } threatCoordinates
            ? _transformSystem.ToMapCoordinates(threatCoordinates)
            : (MapCoordinates?)null;

        foreach (var memberUid in _npcSquadSystem.GetOtherMembers(squadEntity.Value, ownerUid))
        {
            // Where a teammate is headed matters more than where it happens to be right now.
            var memberMap = _npcTacticalPositionClaimSystem.TryGetClaim(memberUid, out var claimCoordinates)
                ? _transformSystem.ToMapCoordinates(claimCoordinates)
                : _transformSystem.GetMapCoordinates(memberUid);

            if (memberMap.MapId != candidate.MapId)
                continue;

            _scratchPositions.Add(memberMap.Position);

            MapCoordinates? memberAim = null;
            if (squadComponent.CoverPlan is { HasRoom: true } plan &&
                plan.Assignments.TryGetValue(memberUid, out var assignment))
                memberAim = _transformSystem.ToMapCoordinates(new EntityCoordinates(plan.GridUid, plan.Thresholds[assignment.ThresholdIndex].AimPoint));
            else
                memberAim = threatMap;

            if (memberAim is { } memberAimMap && memberAimMap.MapId == candidate.MapId)
                _scratchLanes.Add(new NpcFireLane(memberMap.Position, memberAimMap.Position));
        }

        Vector2? aimPosition = aim is { } aimMap && aimMap.MapId == candidate.MapId ? aimMap.Position : null;
        return GetPenalty(candidate.Position, aimPosition, _scratchPositions, _scratchLanes);
    }

    /// <summary>
    ///     The penalty for standing at <paramref name="position"/> and covering <paramref name="aim"/>, given
    ///         where teammates stand and the lanes they cover. All in one coordinate space.
    /// </summary>
    public static float GetPenalty(Vector2 position, Vector2? aim, IReadOnlyList<Vector2> teammatePositions, IReadOnlyList<NpcFireLane> teammateLanes)
    {
        // Our line would go through a teammate...
        if (aim is { } aimPosition)
        {
            foreach (var teammatePosition in teammatePositions)
            {
                if (IsInLane(new NpcFireLane(position, aimPosition), teammatePosition))
                    return CrossedLanePenalty;
            }
        }

        // ...or we would stand in a teammate's line.
        foreach (var lane in teammateLanes)
        {
            if (IsInLane(lane, position))
                return CrossedLanePenalty;
        }

        return 1f;
    }

    /// <summary>
    ///     Whether <paramref name="point"/> is in <paramref name="lane"/>: within <see cref="LaneRadius"/> of the line, in
    ///         front, up to <see cref="LaneOverrun"/> past its end; or within <see cref="LaneRadius"/> of where it starts.
    /// </summary>
    public static bool IsInLane(NpcFireLane lane, Vector2 point)
    {
        if (Vector2.DistanceSquared(point, lane.From) < LaneRadius * LaneRadius)
            return true;

        var direction = lane.To - lane.From;
        var length = direction.Length();

        if (length < 0.01f)
            return false;

        direction /= length;

        var along = Vector2.Dot(point - lane.From, direction);
        if (along <= 0f || along > length + LaneOverrun)
            return false;

        return Vector2.DistanceSquared(point, lane.From + direction * along) < LaneRadius * LaneRadius;
    }

    /// <summary>
    ///     How far from its middle <paramref name="uid"/> is hit: the radius of its round hard fixture, or
    ///         <see cref="DefaultBodyRadius"/>.
    /// </summary>
    public float GetBodyRadius(EntityUid uid)
    {
        if (!_fixturesQuery.TryComp(uid, out var fixturesComponent))
            return DefaultBodyRadius;

        var radius = 0f;
        foreach (var fixture in fixturesComponent.Fixtures.Values)
        {
            if (fixture.Hard && fixture.Shape is PhysShapeCircle circle)
                radius = MathF.Max(radius, circle.Radius);
        }

        return radius > 0f ? radius : DefaultBodyRadius;
    }

    /// <summary>
    ///     A direction for a round from <paramref name="shooterUid"/> at <paramref name="from"/> that hits no friend of it:
    ///         <paramref name="aim"/> if that is clear; else the nearest clear direction to it whose round still lands, or
    ///         passes, within <paramref name="tolerance"/> tiles of it: a target's body, or where a hostile out of sight
    ///         could be. A round into the wall beside the aim counts, if that is within it. False if there is none: hold fire.
    ///     <para>
    ///         Friends that are down do not count: a round goes over them. Nor do those it passes through
    ///             (<see cref="PassesThrough"/>), or those behind a wall. Always clear when disabled. A lookup round the
    ///             shooter, shared with the check as the round goes off (<see cref="IsShotClear"/>): for when it is about
    ///             to fire.
    ///     </para>
    /// </summary>
    public bool TryGetClearShot(EntityUid shooterUid, MapCoordinates from, MapCoordinates aim, float tolerance, out MapCoordinates shot)
    {
        shot = aim;

        if (!Enabled || from.MapId != aim.MapId || IgnoresFriendlyBlocking(shooterUid))
            return true;

        GatherFriends(shooterUid, from);

        if (_friendAtMuzzle)
            return false;

        var direction = aim.Position - from.Position;
        var distance = direction.Length();

        // Nowhere to shoot along, and the gun will not fire either.
        if (distance < 0.01f)
            return true;

        var aimAngle = MathF.Atan2(direction.Y, direction.X);

        if (!IsBlocked(aimAngle))
            return true;

        if (tolerance <= 0f)
            return false;

        // Just clear of each friend in the way, within as far off the aim as still does the job, nearest the aim first.
        var window = MathF.Asin(MathF.Min(1f, tolerance / distance));
        _scratchAngles.Clear();

        foreach (var friend in _friends)
        {
            AddCandidate(friend.Bearing - friend.HalfWidth - EdgeClearance, aimAngle, window);
            AddCandidate(friend.Bearing + friend.HalfWidth + EdgeClearance, aimAngle, window);

            if (friend.RearHalfWidth <= 0f)
                continue;

            AddCandidate(friend.RearBearing - friend.RearHalfWidth - EdgeClearance, aimAngle, window);
            AddCandidate(friend.RearBearing + friend.RearHalfWidth + EdgeClearance, aimAngle, window);
        }

        _scratchAngles.Sort(static (a, b) => MathF.Abs(a).CompareTo(MathF.Abs(b)));

        foreach (var offset in _scratchAngles)
        {
            var angle = aimAngle + offset;

            if (IsBlocked(angle) || !Lands(from, aim.Position, distance, offset, angle, tolerance))
                continue;

            shot = new MapCoordinates(from.Position + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance, from.MapId);
            return true;
        }

        return false;
    }

    /// <summary>
    ///     Whether a round fired now by <paramref name="shooterUid"/> from <paramref name="from"/> at <paramref name="aim"/>
    ///         would hit no friend of it, as <see cref="TryGetClearShot"/> has it, with no other direction to try. Always clear
    ///         when disabled.
    /// </summary>
    public bool IsShotClear(EntityUid shooterUid, MapCoordinates from, MapCoordinates aim)
    {
        if (!Enabled || from.MapId != aim.MapId || IgnoresFriendlyBlocking(shooterUid))
            return true;

        GatherFriends(shooterUid, from);

        if (_friendAtMuzzle)
            return false;

        var direction = aim.Position - from.Position;
        return direction.LengthSquared() < 0.0001f || !IsBlocked(MathF.Atan2(direction.Y, direction.X));
    }

    private bool IgnoresFriendlyBlocking(EntityUid shooterUid)
    {
        return _aimTrackingQuery.TryComp(shooterUid, out var aimTrackingComponent) &&
            aimTrackingComponent.IgnoreFriendlyBlocking;
    }

    /// <summary>
    ///     Every round an NPC fires, however it fires it, is checked as it goes: the rest of a burst goes off by itself, a
    ///         tick or more after the friend it was clear of may have stepped into it.
    /// </summary>
    private void OnShotAttempted(Entity<ActiveNPCComponent> entity, ref ShotAttemptedEvent args)
    {
        // Raised on every try, before the gun checks it is ready: a burst tries every tick between its rounds, and no round
        //      goes off on those, so there is nothing to check.
        if (args.Cancelled ||
            args.Used.Comp.NextFire > _gameTiming.CurTime ||
            args.Used.Comp.ShootCoordinates is not { } coordinates)
            return;

        if (!IsShotClear(entity.Owner, _transformSystem.GetMapCoordinates(entity.Owner), _transformSystem.ToMapCoordinates(coordinates)))
            args.Cancel();
    }

    private void AddCandidate(float edge, float aimAngle, float window)
    {
        var offset = WrapAngle(edge - aimAngle);
        if (MathF.Abs(offset) <= window)
            _scratchAngles.Add(offset);
    }

    /// <summary>
    ///     Whether a round fired at <paramref name="angle"/>, <paramref name="offset"/> off the aim, still does its job: lands,
    ///         or passes, within <paramref name="tolerance"/> of the aim, rather than in a wall short of it.
    /// </summary>
    private bool Lands(MapCoordinates from, Vector2 aim, float distance, float offset, float angle, float tolerance)
    {
        var heading = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        var end = new MapCoordinates(from.Position + heading * (distance + tolerance), from.MapId);

        if (!_npcLineOfFireSystem.TryGetFirstHit(from, end, _friendsShotMask, out var hit))
            return true;

        // It gets past where it is nearest the aim, which the window keeps within tolerance of it.
        if (hit.Distance >= distance * MathF.Cos(offset))
            return true;

        return Vector2.Distance(from.Position + heading * hit.Distance, aim) <= tolerance;
    }

    /// <summary>
    ///     Whether a round at <paramref name="angle"/> would hit a friend in <see cref="_friends"/>.
    /// </summary>
    private bool IsBlocked(float angle)
    {
        for (var i = 0; i < _friends.Count; i++)
        {
            var friend = _friends[i];

            if (friend.RearHalfWidth > 0f && MathF.Abs(WrapAngle(angle - friend.RearBearing)) < friend.RearHalfWidth)
                return true;

            if (MathF.Abs(WrapAngle(angle - friend.Bearing)) < friend.HalfWidth && IsReachable(i))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Whether a round could get to friend <paramref name="index"/> at all, rather than hit a wall on the way: a ray, the
    ///         first time it is asked, and only for a friend in a direction a round might go.
    /// </summary>
    private bool IsReachable(int index)
    {
        var friend = _friends[index];

        if (friend.Reachable == 0)
        {
            var walled = _npcLineOfFireSystem.TryGetFirstHit(_friendsFrom,
                    new MapCoordinates(friend.Position, _friendsFrom.MapId),
                    _friendsShotMask,
                    out var hit) &&
                hit.Distance < friend.Distance - DefaultBodyRadius;

            friend.Reachable = walled ? (sbyte) -1 : (sbyte) 1;
            _friends[index] = friend;
        }

        return friend.Reachable > 0;
    }

    /// <summary>
    ///     Fills <see cref="_friends"/> with every standing friend of <paramref name="shooterUid"/> a round fired now from
    ///         <paramref name="from"/> could hit, by the directions it would hit them in; or notes one where a round starts,
    ///         in every direction (<see cref="_friendAtMuzzle"/>). Once per shooter, spot and tick.
    ///     <para>
    ///         A friend is hit if a round passes within its body radius and the round's half-width of its middle, and
    ///             <see cref="ShotClearance"/>. On the way,
    ///             that is a direction within <c>asin(reach / distance)</c> of it, widened by the gun's spread; and over where
    ///             it is now and where it will be when a round gets to it, or when the last of a burst does, as the shooter
    ///             sees it: a projectile carries the shooter's own velocity, so a friend moves by it as the shooter does; and
    ///             as far again as it may go off that way meanwhile (<see cref="FriendManeuverSpeed"/>). As the round
    ///             starts, which reaches <see cref="ShotBackReach"/> behind the shooter, a friend just behind is hit too, by a
    ///             round going near enough straight away from it.
    ///     </para>
    /// </summary>
    private void GatherFriends(EntityUid shooterUid, MapCoordinates from)
    {
        var tick = _gameTiming.CurTick;
        if (_friendsShooterUid == shooterUid && _friendsTick == tick && _friendsFrom.MapId == from.MapId &&
            Vector2.DistanceSquared(_friendsFrom.Position, from.Position) < 0.0001f)
            return;

        _friendsShooterUid = shooterUid;
        _friendsTick = tick;
        _friendsFrom = from;
        _friendAtMuzzle = false;
        _friends.Clear();

        var cone = _gunSystem.TryGetGun(shooterUid, out var gunEntity)
            ? _npcLineOfFireSystem.GetShotCone(gunEntity)
            : new NpcShotCone(Angle.Zero, 1, float.PositiveInfinity, 0f, 1, 0f);
        _friendsShotMask = _npcLineOfFireSystem.GetShotMask(shooterUid);
        var hitscan = float.IsPositiveInfinity(cone.Speed);

        // A projectile flies at its own speed plus the shooter's velocity: straight out from the shooter as the shooter sees
        //      it, with everything else moving by it the other way. A strafing shooter's rounds drift as far aside as it went.
        var shooterVelocity = _physicsQuery.TryComp(shooterUid, out var shooterPhysicsComponent) ? shooterPhysicsComponent.LinearVelocity : Vector2.Zero;
        var shooterSpeed = shooterVelocity.Length();
        var tickTime = (float) _gameTiming.TickPeriod.TotalSeconds;

        _scratchMobs.Clear();
        _entityLookupSystem.GetEntitiesInRange(from, MissReach, _scratchMobs);

        foreach (var mob in _scratchMobs)
        {
            if (mob.Owner == shooterUid ||
                !_mobStateSystem.IsAlive(mob.Owner, mob.Comp) ||
                !_npcFactionSystem.IsEntityFriendly(shooterUid, mob.Owner) ||
                PassesThrough(shooterUid, mob.Owner, hitscan))
                continue;

            var position = _transformSystem.GetWorldPosition(mob.Owner);
            var offset = position - from.Position;
            var distance = offset.Length();
            var reach = GetBodyRadius(mob.Owner) + cone.HalfWidth + ShotClearance;
            var friendVelocity = _physicsQuery.TryComp(mob.Owner, out var physicsComponent) ? physicsComponent.LinearVelocity : Vector2.Zero;

            // A friend on the move may go off the way it was going this very tick: its steering may set it a new velocity after
            //      this has read the old one, and a round gets to a friend under a tile away within that tick. So it gets a
            //      tick's way about it from the start, where the round starts included. Seen live: friends 0.7 tiles off, just
            //      clear by their velocity, beside and behind the shooter, stepped a few hundredths in and were hit. One
            //      standing still is where it is: KsNpcFriendlyFireTest.TestMapWhereAShotStarts. Where the round starts is as
            //      far off again as the shooter goes in a tick: seen live, a friend 0.7 tiles behind a shooter juking about,
            //      outside where a still one's round starts, was hit.
            // On the move, or about to be: one being steered somewhere sets off whenever its steering says, this tick
            //      included. Seen live: a flanker standing among its squad set off the tick a squadmate fired past it.
            var moving = friendVelocity.LengthSquared() > MovingSpeed * MovingSpeed || _steeringQuery.HasComp(mob.Owner);
            var maneuverSpeed = moving ? MovingFriendManeuverSpeed : FriendManeuverSpeed;
            var nearReach = (moving ? reach + maneuverSpeed * tickTime : reach) + shooterSpeed * tickTime;

            if (distance <= nearReach)
            {
                _friendAtMuzzle = true;
                return;
            }

            // How long a round takes to get to it, or the last of the burst does; and how far off its way it may go meanwhile:
            //      past the first tick for one standing still, which is too soon for it to start.
            var flight = (hitscan ? 0f : distance / cone.Speed) + cone.BurstTime;
            var wayReach = reach + maneuverSpeed * (moving ? MathF.Max(flight, tickTime) : MathF.Max(0f, flight - tickTime));

            var bearing = MathF.Atan2(offset.Y, offset.X);
            var halfWidth = MathF.Asin(MathF.Min(1f, wayReach / distance));
            var rearBearing = WrapAngle(bearing + MathF.PI);
            var rearHalfWidth = GetRearHalfWidth(distance, nearReach);

            // Where it will be by the time a round gets to it, or the last of the burst does, as the shooter sees it; and,
            //      for one just behind, by the physics step after the round is spawned, which is when it is hit.
            var velocity = friendVelocity - shooterVelocity;
            if (velocity.LengthSquared() > 0.0001f)
            {
                var later = offset + velocity * MathF.Max(flight, tickTime);
                var laterDistance = later.Length();

                if (laterDistance <= nearReach)
                {
                    _friendAtMuzzle = true;
                    return;
                }

                var laterBearing = MathF.Atan2(later.Y, later.X);
                Cover(ref bearing, ref halfWidth, laterBearing, MathF.Asin(MathF.Min(1f, wayReach / laterDistance)));

                var stepped = offset + velocity * tickTime;
                var steppedDistance = stepped.Length();

                if (steppedDistance <= nearReach)
                {
                    _friendAtMuzzle = true;
                    return;
                }

                var steppedRearHalfWidth = GetRearHalfWidth(steppedDistance, nearReach);
                if (steppedRearHalfWidth > 0f)
                {
                    var steppedRearBearing = WrapAngle(MathF.Atan2(stepped.Y, stepped.X) + MathF.PI);

                    if (rearHalfWidth > 0f)
                        Cover(ref rearBearing, ref rearHalfWidth, steppedRearBearing, steppedRearHalfWidth);
                    else
                        (rearBearing, rearHalfWidth) = (steppedRearBearing, steppedRearHalfWidth);
                }
            }

            _friends.Add(new NpcFriendInTheWay
            {
                Position = position,
                Distance = distance,
                Bearing = bearing,
                HalfWidth = halfWidth + cone.HalfAngle,
                RearBearing = rearBearing,
                RearHalfWidth = rearHalfWidth > 0f ? rearHalfWidth + cone.HalfAngle : 0f,
            });
        }
    }

    /// <summary>
    ///     How far either side of straight away from a friend <paramref name="distance"/> tiles from the shooter's middle a round
    ///         starts inside it, in radians: 0 for none. The round reaches <see cref="ShotBackReach"/> behind the shooter's
    ///         middle, so its back end, and the stretch from there to the middle, is near a friend just behind.
    /// </summary>
    private static float GetRearHalfWidth(float distance, float reach)
    {
        // The back end, ShotBackReach behind along the round's way, is within reach of the friend where the round's way is
        //      more than this far off the friend's direction (law of cosines).
        var cosine = (reach * reach - distance * distance - ShotBackReach * ShotBackReach) / (2f * ShotBackReach * distance);
        var rear = cosine <= -1f ? MathF.PI : MathF.Acos(MathF.Min(cosine, 1f));

        // The stretch between the back end and the middle, side on to the friend.
        var side = MathF.PI - MathF.Asin(reach / distance);
        if (side < MathF.Acos(-ShotBackReach / distance))
            rear = MathF.Min(rear, side);

        return MathF.PI - rear;
    }

    /// <summary>
    ///     Widens the directions <paramref name="halfWidth"/> either side of <paramref name="bearing"/> to cover those
    ///         <paramref name="otherHalfWidth"/> either side of <paramref name="otherBearing"/> too, and all in between.
    /// </summary>
    private static void Cover(ref float bearing, ref float halfWidth, float otherBearing, float otherHalfWidth)
    {
        var delta = WrapAngle(otherBearing - bearing);
        var low = MathF.Min(-halfWidth, delta - otherHalfWidth);
        var high = MathF.Max(halfWidth, delta + otherHalfWidth);

        bearing = WrapAngle(bearing + (low + high) / 2f);
        halfWidth = (high - low) / 2f;
    }

    private static float WrapAngle(float angle)
    {
        angle %= MathF.Tau;

        if (angle > MathF.PI)
            angle -= MathF.Tau;
        else if (angle <= -MathF.PI)
            angle += MathF.Tau;

        return angle;
    }

    /// <summary>
    ///     Whether a shot by <paramref name="shooterUid"/> goes through <paramref name="friendUid"/> without hitting it: a
    ///         projectile from a <see cref="KsFactionCollisionShooterComponent"/> at one of its own factions. A beam hits anyone.
    /// </summary>
    private bool PassesThrough(EntityUid shooterUid, EntityUid friendUid, bool hitscan)
    {
        return !hitscan &&
            _factionCollisionShooterQuery.HasComp(shooterUid) &&
            _factionMemberQuery.TryComp(shooterUid, out var shooterFactionComponent) &&
            _npcFactionSystem.IsMemberOfAny(friendUid, shooterFactionComponent.Factions);
    }

    /// <summary>
    ///     Notes that <paramref name="shooterUid"/> held a shot for a friend in its way. Returns whether it has held fire for
    ///         <see cref="HoldFireTime"/> where it stands, and should give up its spot for another.
    /// </summary>
    public bool NoteHeldFire(EntityUid shooterUid)
    {
        var now = _gameTiming.CurTime;
        var coordinates = _transformSystem.GetMoverCoordinates(shooterUid);
        var holdingFireComponent = EnsureComp<NpcHoldingFireComponent>(shooterUid);

        // A new hold: none going, one gone stale, or one from somewhere it has since moved on from.
        if (now - holdingFireComponent.LastHeldAt > HoldFireMemory ||
            !coordinates.TryDistance(EntityManager, _transformSystem, holdingFireComponent.Coordinates, out var drift) ||
            drift > HoldFireDrift)
        {
            holdingFireComponent.Since = now;
            holdingFireComponent.Coordinates = coordinates;
        }

        holdingFireComponent.LastHeldAt = now;
        return now - holdingFireComponent.Since >= HoldFireTime;
    }

    /// <summary>
    ///     Notes that <paramref name="shooterUid"/> had a clear shot: whatever fire it held, it is not holding it now.
    /// </summary>
    public void NoteClearShot(EntityUid shooterUid)
    {
        if (_holdingFireQuery.HasComp(shooterUid))
            RemComp<NpcHoldingFireComponent>(shooterUid);
    }

    /// <summary>
    ///     Whether <paramref name="uid"/> is holding fire for a friend, and has been for <see cref="HoldFireTime"/> where it
    ///         stands: for the planner, to find it somewhere else to shoot from.
    /// </summary>
    public bool IsHoldingFire(EntityUid uid)
    {
        if (!_holdingFireQuery.TryComp(uid, out var holdingFireComponent))
            return false;

        var now = _gameTiming.CurTime;
        return now - holdingFireComponent.LastHeldAt <= HoldFireMemory &&
            holdingFireComponent.LastHeldAt - holdingFireComponent.Since >= HoldFireTime;
    }
}
