using Content.Server.Interaction;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.NPC.LineOfFire;

/// <summary>
///     Whether an NPC could hit something from somewhere, which is not the same as being able to see it. Sight goes by what
///         blocks light (see <see cref="Perception.NpcLineOfSightSystem"/>), and a shot goes by what that shot collides with:
///         glass stops a bullet and lets through a laser and some energy bolts, and a locker stops both and hides nothing.
///         Everything an NPC decides about shooting goes through here, so that it never waits on a shot it cannot take,
///         or walks round a window it could shoot through.
/// </summary>
public sealed partial class NpcLineOfFireSystem : EntitySystem
{
    [Dependency] private GunSystem _gunSystem = default!;
    [Dependency] private InteractionSystem _interactionSystem = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;

    [Dependency] private EntityQuery<BallisticAmmoProviderComponent> _ballisticAmmoProviderQuery = default!;
    [Dependency] private EntityQuery<BasicEntityAmmoProviderComponent> _basicEntityAmmoProviderQuery = default!;
    [Dependency] private EntityQuery<BatteryAmmoProviderComponent> _batteryAmmoProviderQuery = default!;
    [Dependency] private EntityQuery<CartridgeAmmoComponent> _cartridgeAmmoQuery = default!;
    [Dependency] private EntityQuery<MobStateComponent> _mobStateQuery = default!;
    [Dependency] private EntityQuery<RevolverAmmoProviderComponent> _revolverAmmoProviderQuery = default!;

    /// <summary>
    ///     For a gun it can tell nothing about, and an NPC with no gun at all.
    /// </summary>
    private const CollisionGroup UnknownShotMask = SharedInteractionSystem.InRangeUnobstructedMask;

    private const int MaxRoundsToFollow = 4;

    /// <summary>
    ///     By what is fired. Fixtures are not cheap to read off a prototype, and this is asked for every few ticks per NPC.
    /// </summary>
    private readonly Dictionary<EntProtoId, CollisionGroup> _shotMasks = new();

    private SharedInteractionSystem.Ignored _isMob = default!;

    public override void Initialize()
    {
        base.Initialize();

        _isMob = uid => _mobStateQuery.HasComp(uid);

        SubscribeLocalEvent<PrototypesReloadedEventArgs>(_ => _shotMasks.Clear());
    }

    /// <summary>
    ///     What the next shot from what <paramref name="shooterUid"/> is holding would collide with.
    /// </summary>
    public CollisionGroup GetShotMask(EntityUid shooterUid)
    {
        if (!_gunSystem.TryGetGun(shooterUid, out var gunEntity) ||
            !TryGetNextAmmo(gunEntity.Owner, out var ammoUid, out var ammoPrototype))
            return UnknownShotMask;

        if (ammoUid is { } uid)
        {
            if (_cartridgeAmmoQuery.TryComp(uid, out var cartridgeComponent))
                ammoPrototype = cartridgeComponent.Prototype;
            else if (MetaData(uid).EntityPrototype is { } entityPrototype)
                ammoPrototype = entityPrototype.ID;
        }

        if (ammoPrototype is not { } prototypeId)
            return UnknownShotMask;

        if (!_shotMasks.TryGetValue(prototypeId, out var shotMask))
        {
            shotMask = GetShotMask(prototypeId);
            _shotMasks[prototypeId] = shotMask;
        }

        return shotMask;
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
    ///     Whether a shot colliding with <paramref name="shotMask"/> would get from one place to the other. Mobs do not count:
    ///         one may be gone by then, and those it would rather not hit are not hit (<c>KsFactionCollisionShooter</c>).
    /// </summary>
    public bool IsClear(MapCoordinates from, MapCoordinates to, CollisionGroup shotMask)
    {
        return _interactionSystem.InRangeUnobstructed(from, to, -1f, shotMask, _isMob);
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
    ///         anything. Mobs do not count: see <see cref="IsClear"/>.
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
        foreach (var result in _physicsSystem.IntersectRayWithPredicate(from.MapId, ray, distance, _isMob.Invoke, returnOnFirstHit: false))
        {
            if (result.Distance >= nearest)
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
    /// </summary>
    private CollisionGroup GetShotMask(EntProtoId prototypeId)
    {
        for (var i = 0; i < MaxRoundsToFollow && ProtoMan.TryIndex(prototypeId, out var prototype); i++)
        {
            if (prototype.TryComp<HitscanBasicRaycastComponent>(out var raycastComponent, Factory))
                return raycastComponent.CollisionMask;

            if (prototype.TryComp<FixturesComponent>(out var fixturesComponent, Factory) &&
                fixturesComponent.Fixtures.TryGetValue(SharedProjectileSystem.ProjectileFixture, out var fixture))
                return (CollisionGroup) fixture.CollisionMask;

            if (prototype.TryComp<CartridgeAmmoComponent>(out var cartridgeComponent, Factory))
                prototypeId = cartridgeComponent.Prototype;
            else if (prototype.TryComp<ProjectileSpreadComponent>(out var spreadComponent, Factory))
                prototypeId = spreadComponent.Proto;
            else
                break;
        }

        return UnknownShotMask;
    }
}
