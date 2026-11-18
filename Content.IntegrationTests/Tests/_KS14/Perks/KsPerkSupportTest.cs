#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Companions;
using Content.Server._KS14.Perks.Support;
using Content.Shared._KS14.Perks;
using Content.Shared._KS14.Perks.Support;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Power.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Perks;

/// <summary>
///     The Support perk's abilities, each on its own test perk.
/// </summary>
[TestFixture]
[TestOf(typeof(KsAmmoDropSystem))]
[TestOf(typeof(KsCompanionSystem))]
public sealed class KsPerkSupportTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _body = "KsTestPerkSupportBody";
    private readonly EntProtoId _foe = "KsTestPerkSupportFoe";
    private readonly EntProtoId _rifle = "KsTestPerkSupportRifle";
    private readonly EntProtoId _laserGun = "KsTestPerkSupportLaserGun";
    private readonly ProtoId<KsPerkPrototype> _ammoDropPerk = "KsTestPerkSupportAmmoDrop";
    private readonly ProtoId<KsPerkPrototype> _turretPerk = "KsTestPerkSupportShoulderTurret";

    private static readonly ProtoId<DamageTypePrototype> Blunt = "Blunt";

    [TestPrototypes]
    private const string Prototypes = @"
- type: npcFaction
  id: KsTestPerkSupportSideA
  hostile:
  - KsTestPerkSupportSideB

- type: npcFaction
  id: KsTestPerkSupportSideB
  hostile:
  - KsTestPerkSupportSideA

- type: entity
  id: KsTestPerkSupportPickup
  components:
  - type: KsAmmoPickup
    radius: 0.6
    checkInterval: 0.1

- type: ksPerk
  id: KsTestPerkSupportAmmoDrop
  name: ks-perk-support-name
  description: ks-perk-support-desc
  icon: { sprite: _KS14/Interface/Perks/perks.rsi, state: support }
  components:
  - type: KsAmmoDropAbility
    chance: 1
    pickup: KsTestPerkSupportPickup

- type: entity
  id: KsTestPerkSupportBody
  components:
  - type: Physics
    bodyType: KinematicController
  - type: Fixtures
    fixtures:
      fix1:
        shape: !type:PhysShapeCircle
          radius: 0.35
        density: 185
        mask:
        - MobMask
        layer:
        - MobLayer
  - type: Hands
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      100: Dead
  - type: Damageable
  - type: Injurable
    damageContainer: Biological
  - type: NpcFactionMember
    factions:
    - KsTestPerkSupportSideA

- type: entity
  parent: KsTestPerkSupportBody
  id: KsTestPerkSupportFoe
  components:
  - type: NpcFactionMember
    factions:
    - KsTestPerkSupportSideB

- type: entity
  parent: BaseBullet
  id: KsTestPerkSupportBullet
  categories: [ HideSpawnMenu ]
  components:
  - type: Projectile
    damage:
      types:
        Blunt: 5

- type: entity
  id: KsTestPerkSupportTurret
  components:
  - type: Physics
    bodyType: KinematicController
  - type: Fixtures
    fixtures:
      fix1:
        shape: !type:PhysShapeCircle
          radius: 0.15
        density: 10
        layer:
        - BulletImpassable
  - type: Damageable
  - type: Injurable
    damageContainer: Inorganic
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      10000: Dead
  - type: CombatMode
    toggleMouseRotator: false
  - type: NoRotateOnInteract
  - type: NoRotateOnMove
  - type: Gun
    fireRate: 4
    minAngle: 0
    maxAngle: 0
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
  - type: BasicEntityAmmoProvider
    proto: KsTestPerkSupportBullet
    capacity: 3
  - type: RechargeBasicEntityAmmo
    rechargeCooldown: 0.5
  - type: HTN
    rootTask:
      task: KsShoulderTurretCompound
    blackboard:
      RotateSpeed: !type:Single
        6.28
      RangedRange: !type:Single
        9

- type: ksPerk
  id: KsTestPerkSupportShoulderTurret
  name: ks-perk-support-name
  description: ks-perk-support-desc
  icon: { sprite: _KS14/Interface/Perks/perks.rsi, state: support }
  components:
  - type: KsCompanions
    companions:
    - prototype: KsTestPerkSupportTurret
      mode: Attached
      offset: 0.7, 0.7
      respawnDelay: 60
      materialiseDuration: 0.1

- type: entity
  id: KsTestPerkSupportCartridge
  categories: [ HideSpawnMenu ]
  components:
  - type: CartridgeAmmo
    proto: KsTestPerkSupportBullet
    deleteOnSpawn: true

- type: entity
  parent: BaseItem
  id: KsTestPerkSupportMagazine
  components:
  - type: BallisticAmmoProvider
    proto: KsTestPerkSupportCartridge
    capacity: 8
    whitelist:
      components:
      - CartridgeAmmo
  - type: ContainerContainer
    containers:
      ballistic-ammo: !type:Container

- type: entity
  parent: BaseItem
  id: KsTestPerkSupportRifle
  components:
  - type: Gun
    fireRate: 100
    minAngle: 0
    maxAngle: 0
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
  - type: MagazineAmmoProvider
  - type: ItemSlots
    slots:
      gun_magazine:
        name: Magazine
        startingItem: KsTestPerkSupportMagazine
        whitelist:
          components:
          - BallisticAmmoProvider
  - type: ContainerContainer
    containers:
      gun_magazine: !type:ContainerSlot

- type: entity
  parent: BaseItem
  id: KsTestPerkSupportLaserGun
  components:
  - type: Gun
    fireRate: 100
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
  - type: BatteryAmmoProvider
    proto: KsTestPerkSupportBullet
    fireCost: 100
  - type: Battery
    maxCharge: 1000
    startingCharge: 1000
";

    private int Ticks(float seconds) => (int)MathF.Ceiling(seconds * (float)Pair.Server.ResolveDependency<IGameTiming>().TickRate);

    private EntityUid SpawnBody(EntProtoId prototype, TestMapData map, float x, EntProtoId? gun = null)
    {
        var server = Pair.Server;
        var bodyUid = server.EntMan.SpawnAtPosition(prototype, new EntityCoordinates(map.Grid, x, 0.5f));
        var handsSystem = server.System<SharedHandsSystem>();
        handsSystem.AddHand(bodyUid, "hand", HandLocation.Middle);
        if (gun is { } gunId)
        {
            var gunUid = server.EntMan.SpawnAtPosition(gunId, new EntityCoordinates(map.Grid, x, 0.5f));
            Assert.That(handsSystem.TryPickupAnyHand(bodyUid, gunUid, checkActionBlocker: false), "the body should hold the gun");
        }

        return bodyUid;
    }

    private void Hurt(EntityUid uid, float amount, EntityUid? origin)
    {
        var damage = new DamageSpecifier(Pair.Server.ProtoMan.Index(Blunt), FixedPoint2.New(amount));
        Pair.Server.System<DamageableSystem>().TryChangeDamage(uid, damage, ignoreResistances: true, origin: origin);
    }

    private List<EntityUid> Pickups()
    {
        var pickups = new List<EntityUid>();
        var query = Pair.Server.EntMan.AllEntityQueryEnumerator<KsAmmoPickupComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (!Pair.Server.EntMan.IsQueuedForDeletion(uid))
                pickups.Add(uid);
        }

        return pickups;
    }

    private BallisticAmmoProviderComponent Magazine(EntityUid holderUid)
    {
        var entMan = Pair.Server.EntMan;
        var gunUid = Pair.Server.System<SharedHandsSystem>().GetActiveItem(holderUid)!.Value;
        var slot = (ContainerSlot)Pair.Server.System<SharedContainerSystem>().GetContainer(gunUid, SharedGunSystem.MagazineSlot);
        return entMan.GetComponent<BallisticAmmoProviderComponent>(slot.ContainedEntity!.Value);
    }

    private static int Rounds(BallisticAmmoProviderComponent magazine) => magazine.Entities.Count + magazine.UnspawnedCount;

    /// <summary>
    ///     A kill drops a pickup. A foe or a friend with nothing to fill leaves it; a friend with a half-empty magazine
    ///         takes it and leaves with a full one of the same rounds; another, with a drained battery gun, is recharged.
    /// </summary>
    [Test]
    public async Task KillDropsAPickupThatRefillsAFriendsGun()
    {
        var server = Pair.Server;
        var entMan = server.EntMan;
        var testMap = await Pair.CreateTestMap();
        var gunSystem = server.System<SharedGunSystem>();
        EntityUid ownerUid = default;
        EntityUid pickupUid = default;
        EntityUid foeUid = default;
        EntityUid emptyHandedUid = default;
        EntityUid friendUid = default;

        await server.WaitAssertion(() =>
        {
            ownerUid = SpawnBody(_body, testMap, 0.5f);
            Assert.That(server.System<SharedKsPerkSystem>().TryAddPerk(ownerUid, _ammoDropPerk), "the test perk should be given");

            // Hurt by someone else: nothing.
            var otherVictimUid = SpawnBody(_foe, testMap, 8.5f);
            Hurt(otherVictimUid, 200, origin: null);
            Assert.That(server.System<MobStateSystem>().IsDead(otherVictimUid));
            Assert.That(Pickups(), Is.Empty, "a kill that isn't the holder's should drop nothing");

            var victimUid = SpawnBody(_foe, testMap, 5.5f);
            Hurt(victimUid, 200, origin: ownerUid);
            Assert.That(server.System<MobStateSystem>().IsDead(victimUid));
            Assert.That(Pickups(), Has.Count.EqualTo(1), "the holder's kill should drop a pickup");
            pickupUid = Pickups()[0];

            // On the spot: a foe with a half-empty gun, and a friend with nothing in hand.
            foeUid = SpawnBody(_foe, testMap, 5.5f, _rifle);
            var foeMagazine = Magazine(foeUid);
            gunSystem.SetBallisticUnspawned((foeMagazine.Owner, foeMagazine), foeMagazine.Capacity / 2);
            emptyHandedUid = SpawnBody(_body, testMap, 5.5f);
        });

        await Pair.RunTicksSync(Ticks(0.5f));
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(pickupUid) && !entMan.IsQueuedForDeletion(pickupUid),
                "a foe, or a friend with no gun, should leave the pickup");
            var foeMagazine = Magazine(foeUid);
            Assert.That(Rounds(foeMagazine), Is.EqualTo(foeMagazine.Capacity / 2), "a foe's gun should not be filled");

            entMan.DeleteEntity(foeUid);
            entMan.DeleteEntity(emptyHandedUid);

            friendUid = SpawnBody(_body, testMap, 5.5f, _rifle);
            var magazine = Magazine(friendUid);
            gunSystem.SetBallisticUnspawned((magazine.Owner, magazine), 1);
        });

        await Pair.RunTicksSync(Ticks(0.5f));
        await server.WaitAssertion(() =>
        {
            var magazine = Magazine(friendUid);
            Assert.That(Rounds(magazine), Is.EqualTo(magazine.Capacity), "a friend's magazine should be filled");
            Assert.That(!entMan.EntityExists(pickupUid) || entMan.IsQueuedForDeletion(pickupUid), "the pickup should be used up");

            // Again, for a battery gun.
            var victimUid = SpawnBody(_foe, testMap, 10.5f);
            Hurt(victimUid, 200, origin: ownerUid);
            Assert.That(Pickups(), Has.Count.EqualTo(1));

            var laserFriendUid = SpawnBody(_body, testMap, 10.5f, _laserGun);
            var gunUid = server.System<SharedHandsSystem>().GetActiveItem(laserFriendUid)!.Value;
            var batterySystem = server.System<SharedBatterySystem>();
            batterySystem.SetCharge((gunUid, entMan.GetComponent<BatteryComponent>(gunUid)), 0f);
            friendUid = laserFriendUid;
        });

        await Pair.RunTicksSync(Ticks(0.5f));
        await server.WaitAssertion(() =>
        {
            var gunUid = server.System<SharedHandsSystem>().GetActiveItem(friendUid)!.Value;
            var battery = entMan.GetComponent<BatteryComponent>(gunUid);
            Assert.That(server.System<SharedBatterySystem>().GetCharge((gunUid, battery)), Is.EqualTo(battery.MaxCharge),
                "a friend's battery gun should be recharged");
            Assert.That(Pickups(), Is.Empty, "the second pickup should be used up");
        });
    }

    /// <summary>
    ///     A turret riding on its owner's shoulder (an attached companion) still turns, fires and hits a foe, and is
    ///         hit by a foe's bullets itself.
    /// </summary>
    [Test]
    public async Task ShoulderTurretAimsFiresAndIsHit()
    {
        var server = Pair.Server;
        var entMan = server.EntMan;
        var testMap = await Pair.CreateTestMap();
        var damageableSystem = server.System<DamageableSystem>();
        EntityUid ownerUid = default;
        EntityUid turretUid = default;
        EntityUid foeUid = default;

        await server.WaitAssertion(() =>
        {
            ownerUid = SpawnBody(_body, testMap, 0.5f);
            Assert.That(server.System<SharedKsPerkSystem>().TryAddPerk(ownerUid, _turretPerk), "the test perk should be given");
            Assert.That(server.System<KsCompanionSystem>().TryGetCompanion(ownerUid, 0, out var companionUid), "the turret should be out");
            turretUid = companionUid!.Value;
            Assert.That(entMan.GetComponent<TransformComponent>(turretUid).ParentUid, Is.EqualTo(ownerUid), "the turret should ride on its owner");

            foeUid = SpawnBody(_foe, testMap, 5.5f);
        });

        // It turns to the foe and shoots it.
        var hit = false;
        for (var i = 0; i < 20 && !hit; i++)
        {
            await Pair.RunTicksSync(Ticks(0.5f));
            await server.WaitPost(() => hit = damageableSystem.GetTotalDamage(foeUid) > FixedPoint2.Zero);
        }

        Assert.That(hit, "the shoulder turret should shoot the foe");

        // A foe straight above it shoots down at it, clear of the owner.
        EntityUid shooterUid = default;
        await server.WaitPost(() =>
        {
            entMan.DeleteEntity(foeUid);
            var turretPosition = server.System<SharedTransformSystem>().GetWorldPosition(turretUid);
            shooterUid = SpawnBody(_foe, testMap, 0f, _rifle);
            server.System<SharedTransformSystem>().SetWorldPosition(shooterUid, turretPosition + new System.Numerics.Vector2(0f, 4f));
        });
        await Pair.RunTicksSync(1);

        await server.WaitPost(() =>
        {
            var gunUid = server.System<SharedHandsSystem>().GetActiveItem(shooterUid)!.Value;
            var turretCoordinates = server.System<SharedTransformSystem>().GetMapCoordinates(turretUid);
            var target = server.System<SharedTransformSystem>().ToCoordinates(turretCoordinates);
            Assert.That(server.System<SharedGunSystem>().AttemptShoot(shooterUid, (gunUid, entMan.GetComponent<GunComponent>(gunUid)), target),
                "the foe should fire");
        });

        var turretHit = false;
        for (var i = 0; i < 20 && !turretHit; i++)
        {
            await Pair.RunTicksSync(2);
            await server.WaitPost(() => turretHit = damageableSystem.GetTotalDamage(turretUid) > FixedPoint2.Zero);
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(turretHit, "a foe's bullet should hit the turret");
            Assert.That(damageableSystem.GetTotalDamage(ownerUid), Is.EqualTo(FixedPoint2.Zero), "the bullet should have hit the turret, not its owner");
        });
    }
}
