using System.Numerics;
using Content.Server._KS14.NPC.Components;
using Content.Server._KS14.NPC.LineOfFire;
using Content.Server._KS14.NPC.Systems;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.FactionCollision;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Squad;

/// <summary>
///     A line an NPC is (or will be) shooting along: from where it stands to what it covers.
/// </summary>
public readonly record struct NpcFireLane(Vector2 From, Vector2 To);

/// <summary>
///     Keeps squad members out of each other's lines of fire, gated behind <see cref="KsCCVars.NpcSquadFireLanes"/>.
///     <list type="bullet">
///         <item>Choosing where to stand, both ways: a spot is penalised if its own line to what it covers passes through a
///             teammate, and if it sits on a teammate's line. A teammate on top of the spot is in both.</item>
///         <item>Shooting: an NPC holds fire while a standing friend is in the way of the shot, or overlapping it, which a
///             shot from there would start inside (<see cref="IsFriendInLineOfFire"/>). Any friend, not only squadmates: an
///             NPC fights before it gets round to joining a squad. Nothing else stops it: a shot hits a friend like anyone
///             else, and the line of fire looks past mobs (see <c>NpcLineOfFireSystem</c>). Held for long enough where it
///             stands (<see cref="NoteHeldFire"/>), it gives up its spot for another.</item>
///     </list>
/// </summary>
public sealed partial class NpcSquadFireLaneSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private NpcFactionSystem _npcFactionSystem = default!;
    [Dependency] private NpcLineOfFireSystem _npcLineOfFireSystem = default!;
    [Dependency] private NpcSquadSystem _npcSquadSystem = default!;
    [Dependency] private NpcTacticalPositionClaimSystem _npcTacticalPositionClaimSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<KsFactionCollisionShooterComponent> _factionCollisionShooterQuery = default!;
    [Dependency] private EntityQuery<NpcFactionMemberComponent> _factionMemberQuery = default!;
    [Dependency] private EntityQuery<NpcHoldingFireComponent> _holdingFireQuery = default!;

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
    ///     How close to a shot's line, in tiles, a squadmate has to be for it to be held: a mob's own width, and a little for
    ///         spread. Tighter than <see cref="LaneRadius"/>, which is for choosing where to stand.
    /// </summary>
    public const float ShotRadius = 0.45f;

    /// <summary>
    ///     How far past what is shot at, in tiles, a squadmate still holds the shot: right behind it, where a miss goes.
    /// </summary>
    public const float ShotOverrun = 1f;

    /// <summary>
    ///     How near where a shot starts, in tiles, a friend is hit by it whichever way it is fired: a mob's width, and a
    ///         bullet's length, which reaches back behind where it is spawned (0.3 tiles, for the usual bullet).
    /// </summary>
    public const float ShotStartRadius = 0.75f;

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

    private readonly List<NpcFireLane> _scratchLanes = new();
    private readonly HashSet<Entity<MobStateComponent>> _scratchMobs = new();
    private readonly List<Vector2> _scratchPositions = new();

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_configurationManager, KsCCVars.NpcSquadFireLanes, value => Enabled = value, true);
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
    ///     Whether <paramref name="point"/> is in <paramref name="lane"/>: see <see cref="IsInLine"/>, with
    ///         <see cref="LaneRadius"/> and <see cref="LaneOverrun"/>.
    /// </summary>
    public static bool IsInLane(NpcFireLane lane, Vector2 point)
    {
        return IsInLine(lane.From, lane.To, point, LaneRadius, LaneOverrun, LaneRadius);
    }

    /// <summary>
    ///     Whether <paramref name="point"/> is within <paramref name="radius"/> of the line from <paramref name="from"/> to
    ///         <paramref name="to"/>, in front, up to <paramref name="overrun"/> past its end; or within
    ///         <paramref name="startRadius"/> of where it starts, which a shot from there is in whichever way it goes. Mobs that
    ///         may walk through each other often stand so, and just behind is no safer then than in front.
    /// </summary>
    public static bool IsInLine(Vector2 from, Vector2 to, Vector2 point, float radius, float overrun, float startRadius)
    {
        if (Vector2.DistanceSquared(point, from) < startRadius * startRadius)
            return true;

        var radiusSquared = radius * radius;

        var direction = to - from;
        var length = direction.Length();

        if (length < 0.01f)
            return false;

        direction /= length;

        var along = Vector2.Dot(point - from, direction);
        if (along <= 0f || along > length + overrun)
            return false;

        var closest = from + direction * along;
        return Vector2.DistanceSquared(point, closest) < radiusSquared;
    }

    /// <summary>
    ///     Whether a shot by <paramref name="shooterUid"/> from <paramref name="from"/> at <paramref name="to"/> would hit a
    ///         standing friend of it first: one in the way, right behind what it is shot at, or overlapping the shooter. False
    ///         when disabled. Friends that are down do not count: a shot goes over them. Nor do those it passes through
    ///         (<see cref="PassesThrough"/>). One lookup, over the box round the shot: for when it is about to be fired.
    /// </summary>
    public bool IsFriendInLineOfFire(EntityUid shooterUid, MapCoordinates from, MapCoordinates to)
    {
        if (!Enabled || from.MapId != to.MapId)
            return false;

        var direction = to.Position - from.Position;
        var length = direction.Length();
        var end = length > 0.01f ? to.Position + direction / length * ShotOverrun : to.Position;
        var bounds = Box2.FromTwoPoints(from.Position, end).Enlarged(MathF.Max(ShotRadius, ShotStartRadius));

        _scratchMobs.Clear();
        _entityLookupSystem.GetEntitiesIntersecting(from.MapId, bounds, _scratchMobs);

        bool? hitscan = null;

        foreach (var mob in _scratchMobs)
        {
            if (mob.Owner == shooterUid ||
                !_mobStateSystem.IsAlive(mob.Owner, mob.Comp) ||
                !IsInLine(from.Position, to.Position, _transformSystem.GetWorldPosition(mob.Owner), ShotRadius, ShotOverrun, ShotStartRadius) ||
                !_npcFactionSystem.IsEntityFriendly(shooterUid, mob.Owner))
                continue;

            hitscan ??= _npcLineOfFireSystem.FiresHitscan(shooterUid);

            if (!PassesThrough(shooterUid, mob.Owner, hitscan.Value))
                return true;
        }

        return false;
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
