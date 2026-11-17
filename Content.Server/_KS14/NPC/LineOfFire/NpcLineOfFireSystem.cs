using System.Numerics;
using Content.Server.Interaction;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Content.Shared.Standing;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.LineOfFire;

/// <summary>
///     Where the rounds from a gun's next trigger pull can go: see <see cref="NpcLineOfFireSystem.GetShotCone"/>.
/// </summary>
/// <param name="Spread">The whole cone they land in: the gun's own spread, plus a pellet spread.</param>
/// <param name="Projectiles">How many projectiles each round throws into it: more than one for buckshot.</param>
/// <param name="Speed">How fast they fly, in tiles a second. Infinite for a beam.</param>
/// <param name="HalfWidth">How far either side of its line a round hits things, in tiles.</param>
/// <param name="Rounds">How many rounds the trigger pull fires: the rest of a burst, or one.</param>
/// <param name="RoundInterval">The time between those rounds, in seconds.</param>
public readonly record struct NpcShotCone(Angle Spread, int Projectiles, float Speed, float HalfWidth, int Rounds, float RoundInterval)
{
    /// <summary>
    ///     How far to either side of the aim a round may go, in radians.
    /// </summary>
    public float HalfAngle => (float) Math.Abs(Spread.Theta) / 2f;

    /// <summary>
    ///     How long from the first of <see cref="Rounds"/> to the last, in seconds.
    /// </summary>
    public float BurstTime => (Rounds - 1) * RoundInterval;
}

/// <summary>
///     Whether an NPC could hit something from somewhere, which is not the same as being able to see it. Sight goes by what
///         blocks light (see <see cref="Perception.NpcLineOfSightSystem"/>), and a shot goes by what that shot collides with:
///         glass stops a bullet and lets through a laser and some energy bolts, and a locker stops both and hides nothing.
///         Everything an NPC decides about shooting goes through here, so that it never waits on a shot it cannot take,
///         or walks round a window it could shoot through.
/// </summary>
public sealed partial class NpcLineOfFireSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private GunSystem _gunSystem = default!;
    [Dependency] private InteractionSystem _interactionSystem = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private RayCastSystem _rayCastSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<BallisticAmmoProviderComponent> _ballisticAmmoProviderQuery = default!;
    [Dependency] private EntityQuery<BasicEntityAmmoProviderComponent> _basicEntityAmmoProviderQuery = default!;
    [Dependency] private EntityQuery<BatteryAmmoProviderComponent> _batteryAmmoProviderQuery = default!;
    [Dependency] private EntityQuery<BroadphaseComponent> _broadphaseQuery = default!;
    [Dependency] private EntityQuery<CartridgeAmmoComponent> _cartridgeAmmoQuery = default!;
    [Dependency] private EntityQuery<MobStateComponent> _mobStateQuery = default!;
    [Dependency] private EntityQuery<RevolverAmmoProviderComponent> _revolverAmmoProviderQuery = default!;
    [Dependency] private EntityQuery<StandingStateComponent> _standingStateQuery = default!;

    /// <summary>
    ///     For a gun it can tell nothing about, and an NPC with no gun at all.
    /// </summary>
    private const CollisionGroup UnknownShotMask = SharedInteractionSystem.InRangeUnobstructedMask;

    private const int MaxRoundsToFollow = 4;

    /// <summary>
    ///     How near a shot's start the middle of what it hits must be for that to be the shooter: see
    ///         <see cref="TryGetFirstHit"/>. Squared, in tiles.
    /// </summary>
    private const float ShooterMiddleDistanceSquared = 0.0001f;

    /// <summary>
    ///     How far either side of its line a round hits things, for one whose fixture tells nothing: a bullet's.
    /// </summary>
    private const float DefaultHalfWidth = 0.1f;

    /// <summary>
    ///     What a round fires, as far as a shot goes: what it collides with, whether it is a beam, how wide it is, and the
    ///         pellet spread, if it splits into several.
    /// </summary>
    private readonly record struct NpcShotInfo(CollisionGroup Mask, bool Hitscan, float HalfWidth, Angle? PelletSpread, int PelletCount);

    private static readonly NpcShotInfo UnknownShot = new(UnknownShotMask, false, DefaultHalfWidth, null, 1);

    /// <summary>
    ///     What each round fires, by the round's prototype. Fixtures are not cheap to read off a prototype, and this is asked
    ///         for every few ticks per NPC, and on every shot.
    /// </summary>
    private readonly Dictionary<EntProtoId, NpcShotInfo> _shots = new();

    private SharedInteractionSystem.Ignored _isMob = default!;

    /// <summary>
    ///     For a sight check by collision mask that no mob ends, standing or not: every mob is on the
    ///         <see cref="CollisionGroup.Opaque"/> layer. Made once, not on every cast.
    /// </summary>
    public SharedInteractionSystem.Ignored IsMob => _isMob;

    /// <summary>
    ///     <see cref="_isMob"/> as the ray query takes it: made once, not on every cast.
    /// </summary>
    private Func<EntityUid, bool> _isMobPredicate = default!;

    /// <summary>
    ///     For a sight check by collision mask, which a body on the floor must not end: whether this is a mob lying down or
    ///         dead. Every mob is on the <see cref="CollisionGroup.Opaque"/> layer, so lasers hit those standing, but one on
    ///         the floor hides nothing, and rounds and beams pass over it unless aimed at it
    ///         (<c>RequireProjectileTargetComponent</c>, active while it is down). Made once, not on every cast.
    /// </summary>
    public SharedInteractionSystem.Ignored IsLyingMob { get; private set; } = default!;

    /// <summary>
    ///     What <see cref="CanRoundReach"/>'s ray passes through: any mob, the shooter and the target. Made once, not on every
    ///         cast; the two it reads are set just before each.
    /// </summary>
    private Func<EntityUid, bool> _isIgnoredByRound = default!;

    private EntityUid _roundShooterUid;
    private EntityUid _roundTargetUid;

    /// <summary>
    ///     <see cref="CanRoundReach"/>'s hit, if any. Reused, so that it stops allocating once it has held one.
    /// </summary>
    private RayResult _roundRayResult = new();

    public override void Initialize()
    {
        base.Initialize();

        _isIgnoredByRound = uid => uid == _roundShooterUid || uid == _roundTargetUid || _mobStateQuery.HasComp(uid);

        _isMob = uid => _mobStateQuery.HasComp(uid);
        _isMobPredicate = uid => _mobStateQuery.HasComp(uid);
        IsLyingMob = uid => _mobStateQuery.TryComp(uid, out var mobState) &&
            (mobState.CurrentState == MobState.Dead ||
                (_standingStateQuery.TryComp(uid, out var standingState) && !standingState.Standing));

        SubscribeLocalEvent<PrototypesReloadedEventArgs>(_ => _shots.Clear());
    }

    /// <summary>
    ///     What the next shot from what <paramref name="shooterUid"/> is holding would collide with.
    /// </summary>
    public CollisionGroup GetShotMask(EntityUid shooterUid)
    {
        return TryGetNextShot(shooterUid, out var shot) ? shot.Mask : UnknownShotMask;
    }

    /// <summary>
    ///     What the next round from what <paramref name="shooterUid"/> is holding would collide with, if it can tell: what
    ///         walls, windows and the like stop it. A laser goes through glass; a bullet does not.
    /// </summary>
    public bool TryGetShotMask(EntityUid shooterUid, out CollisionGroup mask)
    {
        mask = default;
        if (!TryGetNextShot(shooterUid, out var shot))
            return false;

        mask = shot.Mask;
        return true;
    }

    /// <summary>
    ///     Whether the next shot from what <paramref name="shooterUid"/> is holding is hitscan: a beam, not a projectile. Only a
    ///         projectile passes through those its shooter would rather not hit (<c>KsFactionCollisionShooter</c>).
    /// </summary>
    public bool FiresHitscan(EntityUid shooterUid)
    {
        return TryGetNextShot(shooterUid, out var shot) && shot.Hitscan;
    }

    private bool TryGetNextShot(EntityUid shooterUid, out NpcShotInfo shot)
    {
        shot = default;
        return _gunSystem.TryGetGun(shooterUid, out var gunEntity) && TryGetGunShot(gunEntity.Owner, out shot);
    }

    private bool TryGetGunShot(EntityUid gunUid, out NpcShotInfo shot)
    {
        shot = default;

        if (!TryGetNextAmmo(gunUid, out var ammoUid, out var ammoPrototype))
            return false;

        if (ammoUid is { } uid)
        {
            if (_cartridgeAmmoQuery.TryComp(uid, out var cartridgeComponent))
                ammoPrototype = cartridgeComponent.Prototype;
            else if (MetaData(uid).EntityPrototype is { } entityPrototype)
                ammoPrototype = entityPrototype.ID;
        }

        if (ammoPrototype is not { } prototypeId)
            return false;

        if (!_shots.TryGetValue(prototypeId, out shot))
        {
            shot = GetShot(prototypeId);
            _shots[prototypeId] = shot;
        }

        return true;
    }

    /// <summary>
    ///     Where the rounds of <paramref name="gun"/>'s next trigger pull can go. The gun's spread is what
    ///         <c>SharedGunSystem.GetRecoilAngle</c> will make of it, round by round, at the widest it gets over the rest of a
    ///         burst; or, with <paramref name="worstCase"/>, the widest it ever gets. A pellet spread, if the round splits,
    ///         goes on top, narrowed as it is when fired (a choke). With no ammo to look at: the gun's spread, one bullet.
    /// </summary>
    public NpcShotCone GetShotCone(Entity<GunComponent> gun, bool worstCase = false)
    {
        if (!TryGetGunShot(gun.Owner, out var shot))
            shot = UnknownShot;

        var burst = gun.Comp.SelectedMode == SelectiveFire.Burst;
        var rounds = burst
            ? Math.Max(1, gun.Comp.ShotsPerBurstModified - (gun.Comp.BurstActivated ? gun.Comp.BurstShotsCount : 0))
            : 1;
        var roundInterval = burst && gun.Comp.BurstFireRate > 0f ? 1f / gun.Comp.BurstFireRate : 0f;

        var gunSpread = worstCase ? gun.Comp.MaxAngleModified : GetPredictedSpread(gun.Comp, rounds, roundInterval);

        var pelletSpread = Angle.Zero;
        var projectiles = 1;

        if (shot.PelletSpread is { } spread)
        {
            var spreadEvent = new GunGetAmmoSpreadEvent(spread);
            RaiseLocalEvent(gun.Owner, ref spreadEvent);
            pelletSpread = spreadEvent.Spread;
            projectiles = Math.Max(shot.PelletCount, 1);
        }

        return new NpcShotCone(gunSpread + pelletSpread,
            projectiles,
            shot.Hitscan ? float.PositiveInfinity : gun.Comp.ProjectileSpeedModified,
            shot.Hitscan ? 0f : shot.HalfWidth,
            rounds,
            roundInterval);
    }

    /// <summary>
    ///     The widest the gun's spread gets over its next <paramref name="rounds"/> rounds: <c>GetRecoilAngle</c>'s working,
    ///         which widens it by each round and lets it settle back between them, without its side effects.
    /// </summary>
    private Angle GetPredictedSpread(GunComponent gunComponent, int rounds, float roundInterval)
    {
        var now = _gameTiming.CurTime;
        var nextFire = gunComponent.NextFire > now ? gunComponent.NextFire : now;
        var settle = Math.Max(0.0, (nextFire - gunComponent.LastFire).TotalSeconds);
        var theta = gunComponent.CurrentAngle.Theta;
        var widest = 0.0;

        for (var round = 0; round < rounds; round++)
        {
            theta = Math.Clamp(theta + gunComponent.AngleIncreaseModified.Theta - gunComponent.AngleDecayModified.Theta * settle,
                gunComponent.MinAngleModified.Theta,
                gunComponent.MaxAngleModified.Theta);
            widest = Math.Max(widest, theta);
            settle = roundInterval;
        }

        return new Angle(widest);
    }

    /// <summary>
    ///     Whether <paramref name="shooterUid"/> could hit <paramref name="targetUid"/> from where it stands.
    /// </summary>
    public bool HasLineOfFire(EntityUid shooterUid, EntityUid targetUid, float range)
    {
        return _interactionSystem.InRangeUnobstructed(shooterUid, targetUid, range, GetShotMask(shooterUid), _isMob);
    }

    /// <inheritdoc cref="HasLineOfFire(EntityUid, EntityUid, float)"/>
    public bool HasLineOfFire(EntityUid shooterUid, EntityCoordinates coordinates, float range)
    {
        return _interactionSystem.InRangeUnobstructed(shooterUid, coordinates, range, GetShotMask(shooterUid), _isMob);
    }

    /// <summary>
    ///     Whether the next round from what <paramref name="shooterUid"/> is holding would get from it to
    ///         <paramref name="targetUid"/>, by what that round collides with: glass stops a bullet and not a laser. Mobs do not
    ///         count (one may be gone by then; friends in the way are for <c>NpcSquadFireLaneSystem</c>), nor do the shooter's
    ///         own body or the target's. One ray from middle to middle, with no allocation once warm: for asking of every
    ///         candidate target while planning, and every few ticks in a fight. A turret's hold fire
    ///         (<c>NPCRangedCombatComponent.KsTargetInLineOfFire</c>) and its choice of target
    ///         (<c>KsTargetInLineOfFireCon</c>) both ask this, so the two never disagree.
    /// </summary>
    /// <remarks>
    ///     Main thread only: the ray's state is kept on the system. NPC systems and planning never run anywhere else.
    /// </remarks>
    public bool CanRoundReach(EntityUid shooterUid, EntityUid targetUid)
    {
        var from = _transformSystem.GetMapCoordinates(shooterUid);
        var to = _transformSystem.GetMapCoordinates(targetUid);
        if (from.MapId != to.MapId || from.MapId == MapId.Nullspace)
            return false;

        var translation = to.Position - from.Position;
        if (translation.LengthSquared() < 0.0001f)
            return true;

        if (!_mapSystem.TryGetMap(from.MapId, out var mapUid))
            return false;

        _roundShooterUid = shooterUid;
        _roundTargetUid = targetUid;
        _roundRayResult.Results.Clear();

        var state = new RoundRayState(this,
            from.Position,
            translation,
            new QueryFilter
            {
                MaskBits = (int) GetShotMask(shooterUid),
                IsIgnored = _isIgnoredByRound,
            });

        if (_broadphaseQuery.TryComp(mapUid.Value, out var mapBroadphaseComponent))
            CastRoundRay((mapUid.Value, mapBroadphaseComponent), ref state);

        if (!_roundRayResult.Hit)
        {
            var bounds = new Box2(Vector2.Min(from.Position, to.Position), Vector2.Max(from.Position, to.Position));
            _mapSystem.FindGridsIntersecting(mapUid.Value,
                bounds,
                ref state,
                static (EntityUid gridUid, MapGridComponent _, ref RoundRayState rayState) =>
                {
                    var system = rayState.System;
                    if (system._broadphaseQuery.TryComp(gridUid, out var broadphaseComponent))
                        system.CastRoundRay((gridUid, broadphaseComponent), ref rayState);

                    return !system._roundRayResult.Hit;
                },
                approx: true,
                includeMap: false);
        }

        _roundShooterUid = EntityUid.Invalid;
        _roundTargetUid = EntityUid.Invalid;
        return !_roundRayResult.Hit;
    }

    /// <summary>
    ///     <see cref="CanRoundReach"/>'s ray against one broadphase, the map's or a grid's, in that broadphase's frame, as
    ///         <c>RayCastSystem.CastRayClosest(MapId, ...)</c> does it, but into a result kept between calls.
    /// </summary>
    private void CastRoundRay(Entity<BroadphaseComponent> broadphase, ref RoundRayState state)
    {
        var transform = _physicsSystem.GetPhysicsTransform(broadphase.Owner);
        var localOrigin = Robust.Shared.Physics.Transform.InvTransformPoint(transform, state.Origin);
        var localTranslation = Robust.Shared.Physics.Transform.InvTransformPoint(transform, state.Origin + state.Translation) - localOrigin;
        _rayCastSystem.CastRayClosest((broadphase.Owner, broadphase.Comp), ref _roundRayResult, localOrigin, localTranslation, state.Filter);
    }

    private readonly struct RoundRayState(NpcLineOfFireSystem system, Vector2 origin, Vector2 translation, QueryFilter filter)
    {
        public readonly NpcLineOfFireSystem System = system;
        public readonly Vector2 Origin = origin;
        public readonly Vector2 Translation = translation;
        public readonly QueryFilter Filter = filter;
    }

    /// <summary>
    ///     Whether a shot colliding with <paramref name="shotMask"/> would get from one place to the other. Mobs do not count:
    ///         one may be gone by then. Friends in the way are for <c>NpcSquadFireLaneSystem.IsFriendInLineOfFire</c>, when the
    ///         shot is fired: whether they are hit depends on the shot (<c>KsFactionCollisionShooter</c>, hitscan).
    /// </summary>
    public bool IsClear(MapCoordinates from, MapCoordinates to, CollisionGroup shotMask)
    {
        return _interactionSystem.InRangeUnobstructed(from, to, -1f, shotMask, _isMob);
    }

    /// <summary>
    ///     How far either side of its line a thrown grenade hits things, in tiles: the radius of a hand grenade's fixture
    ///         (<c>GrenadeBase</c>). Asked before there is a grenade to look at, while choosing what to throw at.
    /// </summary>
    public const float GrenadeRadius = 0.2f;

    /// <summary>
    ///     Whether something thrown from one place would get to the other: <see cref="IsClear"/> for what a thrown item
    ///         collides with, along the line and along either side of it, <paramref name="radius"/> out, as a grenade is not a
    ///         point. Glass can be seen through and stops a throw; a doorway taken at a slant clips its frame.
    /// </summary>
    public bool IsThrowClear(MapCoordinates from, MapCoordinates to, float radius = GrenadeRadius)
    {
        if (!IsClear(from, to, CollisionGroup.ItemMask))
            return false;

        var offset = to.Position - from.Position;
        var length = offset.Length();
        if (radius <= 0f || length < 0.01f)
            return true;

        var side = new Vector2(-offset.Y, offset.X) * (radius / length);
        return IsClear(from.Offset(side), to.Offset(side), CollisionGroup.ItemMask) &&
            IsClear(from.Offset(-side), to.Offset(-side), CollisionGroup.ItemMask);
    }

    /// <summary>
    ///     How far along the way to <paramref name="to"/> a shot from <paramref name="shooterUid"/> would get.
    /// </summary>
    public float GetClearDistance(EntityUid shooterUid, MapCoordinates from, MapCoordinates to)
    {
        return TryGetFirstHit(from, to, GetShotMask(shooterUid), out var hit) ? hit.Distance : (to.Position - from.Position).Length();
    }

    /// <summary>
    ///     What a shot colliding with <paramref name="shotMask"/> would hit first on its way from one place to the other, if
    ///         anything. Mobs do not count: see <see cref="IsClear"/>. Nor does whatever has its middle where the shot starts:
    ///         the shooter itself, a turret's own body, which its rounds leave and never hit, and which would otherwise hide
    ///         everything from it.
    /// </summary>
    public bool TryGetFirstHit(MapCoordinates from, MapCoordinates to, CollisionGroup shotMask, out RayCastResults hit)
    {
        hit = default;

        var direction = to.Position - from.Position;
        if (from.MapId != to.MapId || direction.LengthSquared() < 0.0001f)
            return false;

        var distance = direction.Length();
        var ray = new CollisionRay(from.Position, direction / distance, (int) shotMask);
        var nearest = float.MaxValue;

        // Every hit, not the first: the first is the first the broadphase came across, not the nearest.
        foreach (var result in _physicsSystem.IntersectRayWithPredicate(from.MapId, ray, distance, _isMobPredicate, returnOnFirstHit: false))
        {
            if (result.Distance >= nearest ||
                Vector2.DistanceSquared(_transformSystem.GetWorldPosition(result.HitEntity), from.Position) < ShooterMiddleDistanceSquared)
                continue;

            nearest = result.Distance;
            hit = result;
        }

        return nearest < float.MaxValue;
    }

    /// <summary>
    ///     The round <paramref name="gunUid"/> will fire next, either as a spawned entity or, where it has not been
    ///         spawned yet, as a prototype. Covers the common providers; others report nothing.
    /// </summary>
    public bool TryGetNextAmmo(EntityUid gunUid, out EntityUid? ammoUid, out EntProtoId? ammoPrototype)
    {
        ammoUid = null;
        ammoPrototype = null;

        // Anything chambered goes first.
        if (_gunSystem.GetChamberEntity(gunUid) is { } chamberedUid)
        {
            ammoUid = chamberedUid;
            return true;
        }

        if (_batteryAmmoProviderQuery.TryComp(gunUid, out var batteryComponent))
        {
            ammoPrototype = batteryComponent.Prototype;
            return true;
        }

        if (_basicEntityAmmoProviderQuery.TryComp(gunUid, out var basicEntityComponent))
        {
            ammoPrototype = basicEntityComponent.Proto;
            return true;
        }

        if (_revolverAmmoProviderQuery.TryComp(gunUid, out var revolverComponent) &&
            revolverComponent.CurrentIndex < revolverComponent.Chambers.Length)
        {
            if (revolverComponent.CurrentIndex < revolverComponent.AmmoSlots.Count &&
                revolverComponent.AmmoSlots[revolverComponent.CurrentIndex] is { } slotUid)
                ammoUid = slotUid;
            else if (revolverComponent.Chambers[revolverComponent.CurrentIndex] == true)
                ammoPrototype = revolverComponent.FillPrototype;

            return ammoUid != null || ammoPrototype != null;
        }

        // A detachable magazine, or ammo held by the gun itself (a pump shotgun's tube).
        var providerUid = _containerSystem.TryGetContainer(gunUid, SharedGunSystem.MagazineSlot, out var container) &&
            container is ContainerSlot { ContainedEntity: { } magazineUid }
                ? magazineUid
                : gunUid;

        if (!_ballisticAmmoProviderQuery.TryComp(providerUid, out var ballisticComponent))
            return false;

        // Spawned rounds are fired from the end of the list before any unspawned ones are made.
        if (ballisticComponent.Entities.Count > 0)
            ammoUid = ballisticComponent.Entities[^1];
        else if (ballisticComponent.UnspawnedCount > 0)
            ammoPrototype = ballisticComponent.Proto;

        return ammoUid != null || ammoPrototype != null;
    }

    /// <summary>
    ///     Looks through a round to what it actually fires: a beam, or a projectile, maybe by way of a cartridge or a spread.
    ///         A projectile that spreads (buckshot) is itself the first of the pellets.
    /// </summary>
    private NpcShotInfo GetShot(EntProtoId prototypeId)
    {
        Angle? pelletSpread = null;
        var pelletCount = 1;

        for (var i = 0; i < MaxRoundsToFollow && ProtoMan.TryIndex(prototypeId, out var prototype); i++)
        {
            if (prototype.TryComp<HitscanBasicRaycastComponent>(out var raycastComponent, Factory))
                return new NpcShotInfo(raycastComponent.CollisionMask, true, 0f, pelletSpread, pelletCount);

            var spreads = prototype.TryComp<ProjectileSpreadComponent>(out var spreadComponent, Factory);
            if (spreads)
            {
                pelletSpread = spreadComponent!.Spread;
                pelletCount = spreadComponent.Count;
            }

            if (prototype.TryComp<FixturesComponent>(out var fixturesComponent, Factory) &&
                fixturesComponent.Fixtures.TryGetValue(SharedProjectileSystem.ProjectileFixture, out var fixture))
                return new NpcShotInfo((CollisionGroup) fixture.CollisionMask, false, GetHalfWidth(fixture.Shape), pelletSpread, pelletCount);

            if (prototype.TryComp<CartridgeAmmoComponent>(out var cartridgeComponent, Factory))
                prototypeId = cartridgeComponent.Prototype;
            else if (spreads)
                prototypeId = spreadComponent!.Proto;
            else
                break;
        }

        return UnknownShot with { PelletSpread = pelletSpread, PelletCount = pelletCount };
    }

    /// <summary>
    ///     Half the narrow side of a projectile's shape: how far to either side of its line it hits things, flying along its
    ///         long side.
    /// </summary>
    private static float GetHalfWidth(IPhysShape shape)
    {
        if (shape is PhysShapeCircle circle)
            return circle.Radius;

        var bounds = shape.ComputeAABB(Robust.Shared.Physics.Transform.Empty, 0);
        var halfWidth = MathF.Min(bounds.Width, bounds.Height) / 2f;
        return halfWidth > 0f ? halfWidth : DefaultHalfWidth;
    }
}
