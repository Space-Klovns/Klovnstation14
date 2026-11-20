#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Perks;
using Content.Shared._KS14.Perks.Damage;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Perks;

/// <summary>
///     The Damage perk's gun abilities, each on its own test perk: the ramp on bullets and on beams with its cap and
///         every way of losing it, the close-range bonus, the falloff, the magazine dropping out, and the explosive last
///         rounds that spare structures.
/// </summary>
[TestFixture]
[TestOf(typeof(KsDamageRampSystem))]
[TestOf(typeof(KsCloseRangeBonusSystem))]
[TestOf(typeof(KsRangeFalloffSystem))]
[TestOf(typeof(KsMagazineAutoDropSystem))]
[TestOf(typeof(KsExplosiveLastRoundsSystem))]
public sealed class KsPerkDamageTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _shooter = "KsTestPerkDmgShooter";
    private readonly EntProtoId _target = "KsTestPerkDmgTarget";
    private readonly EntProtoId _structure = "KsTestPerkDmgStructure";
    private readonly EntProtoId _rifle = "KsTestPerkDmgRifle";
    private readonly EntProtoId _magazine = "KsTestPerkDmgMagazine";
    private readonly EntProtoId _laser = "KsTestPerkDmgLaserGun";
    private readonly EntProtoId _rechargingLaser = "KsTestPerkDmgRechargingLaserGun";
    private readonly EntProtoId _shotgun = "KsTestPerkDmgShotgun";
    private readonly EntProtoId _bullet = "KsTestPerkDmgBullet";
    private readonly ProtoId<KsPerkPrototype> _rampPerk = "KsTestPerkDmgRamp";
    private readonly ProtoId<KsPerkPrototype> _closePerk = "KsTestPerkDmgClose";
    private readonly ProtoId<KsPerkPrototype> _falloffPerk = "KsTestPerkDmgFalloff";
    private readonly ProtoId<KsPerkPrototype> _dropPerk = "KsTestPerkDmgDrop";
    private readonly ProtoId<KsPerkPrototype> _explosivePerk = "KsTestPerkDmgExplosive";

    [TestPrototypes]
    private const string Prototypes = @"
- type: ksPerk
  id: KsTestPerkDmgRamp
  name: ks-perk-damage-name
  description: ks-perk-damage-desc
  icon: { sprite: _KS14/Interface/Perks/perks.rsi, state: damage }
  components:
  - type: KsDamageRampAbility
    bonusPerHit: 0.1
    maxBonus: 0.25

- type: ksPerk
  id: KsTestPerkDmgClose
  name: ks-perk-damage-name
  description: ks-perk-damage-desc
  icon: { sprite: _KS14/Interface/Perks/perks.rsi, state: damage }
  components:
  - type: KsCloseRangeBonusAbility
    maxBonus: 0.5
    fullBonusRange: 2.5
    noBonusRange: 6

- type: ksPerk
  id: KsTestPerkDmgFalloff
  name: ks-perk-damage-name
  description: ks-perk-damage-desc
  icon: { sprite: _KS14/Interface/Perks/perks.rsi, state: damage }
  components:
  - type: KsRangeFalloffAbility
    startRange: 2.5
    endRange: 6
    minMultiplier: 0.5

- type: ksPerk
  id: KsTestPerkDmgDrop
  name: ks-perk-damage-name
  description: ks-perk-damage-desc
  icon: { sprite: _KS14/Interface/Perks/perks.rsi, state: damage }
  components:
  - type: KsMagazineAutoDropAbility

- type: ksPerk
  id: KsTestPerkDmgExplosive
  name: ks-perk-damage-name
  description: ks-perk-damage-desc
  icon: { sprite: _KS14/Interface/Perks/perks.rsi, state: damage }
  components:
  - type: KsExplosiveLastRoundsAbility
    fraction: 0.25
    minRounds: 1
    explosionType: KsAntiPersonnelRocket
    totalIntensity: 6
    intensitySlope: 2
    maxIntensity: 2

- type: entity
  id: KsTestPerkDmgShooter
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
  - type: Damageable
  - type: Injurable
    damageContainer: Biological

- type: entity
  id: KsTestPerkDmgTarget
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
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      100000: Critical
      200000: Dead
  - type: Damageable
  - type: Injurable
    damageContainer: Biological

# What the anti-personnel blast spares: tagged a structure, and damageable.
- type: entity
  id: KsTestPerkDmgStructure
  components:
  - type: Tag
    tags:
    - Structure
  - type: Damageable
  - type: Injurable
    damageContainer: StructuralInorganic

- type: entity
  parent: BaseBullet
  id: KsTestPerkDmgBullet
  categories: [ HideSpawnMenu ]
  components:
  - type: Projectile
    damage:
      types:
        Blunt: 10

- type: entity
  id: KsTestPerkDmgCartridge
  categories: [ HideSpawnMenu ]
  components:
  - type: CartridgeAmmo
    proto: KsTestPerkDmgBullet
    deleteOnSpawn: true

- type: entity
  parent: BaseItem
  id: KsTestPerkDmgMagazine
  components:
  - type: BallisticAmmoProvider
    proto: KsTestPerkDmgCartridge
    capacity: 6
    whitelist:
      components:
      - CartridgeAmmo
  - type: ContainerContainer
    containers:
      ballistic-ammo: !type:Container

- type: entity
  parent: BaseItem
  id: KsTestPerkDmgRifle
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
        startingItem: KsTestPerkDmgMagazine
        whitelist:
          components:
          - BallisticAmmoProvider
  - type: ContainerContainer
    containers:
      gun_magazine: !type:ContainerSlot

- type: entity
  parent: BasicHitscan
  id: KsTestPerkDmgLaser
  categories: [ HideSpawnMenu ]
  components:
  - type: HitscanBasicDamage
    damage:
      types:
        Heat: 10

- type: entity
  parent: BaseItem
  id: KsTestPerkDmgLaserGun
  components:
  - type: Gun
    fireRate: 100
    minAngle: 0
    maxAngle: 0
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
  - type: BasicEntityAmmoProvider
    proto: KsTestPerkDmgLaser
    capacity: 5

- type: entity
  parent: KsTestPerkDmgLaserGun
  id: KsTestPerkDmgRechargingLaserGun
  components:
  - type: RechargeBasicEntityAmmo
    rechargeCooldown: 0.1
    rechargeSound: null

# A shell of several pellets, all flying straight.
- type: entity
  parent: KsTestPerkDmgBullet
  id: KsTestPerkDmgPelletSpread
  categories: [ HideSpawnMenu ]
  components:
  - type: ProjectileSpread
    proto: KsTestPerkDmgBullet
    count: 4
    spread: 0

- type: entity
  id: KsTestPerkDmgShell
  categories: [ HideSpawnMenu ]
  components:
  - type: CartridgeAmmo
    proto: KsTestPerkDmgPelletSpread
    deleteOnSpawn: true

- type: entity
  parent: BaseItem
  id: KsTestPerkDmgShotgun
  components:
  - type: Gun
    fireRate: 100
    minAngle: 0
    maxAngle: 0
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
  - type: BallisticAmmoProvider
    proto: KsTestPerkDmgShell
    capacity: 6
  - type: ContainerContainer
    containers:
      ballistic-ammo: !type:Container
";

    private sealed record Scene(TestMapData Map, EntityUid ShooterUid, EntityUid GunUid);

    private async Task<Scene> SetUp(ProtoId<KsPerkPrototype>? perk, EntProtoId gun)
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        EntityUid shooterUid = default;
        EntityUid gunUid = default;

        await server.WaitPost(() =>
        {
            shooterUid = server.EntMan.SpawnAtPosition(_shooter, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            gunUid = server.EntMan.SpawnAtPosition(gun, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            server.System<SharedHandsSystem>().AddHand(shooterUid, "hand", HandLocation.Middle);
            Assert.That(server.System<SharedHandsSystem>().TryPickupAnyHand(shooterUid, gunUid, checkActionBlocker: false), "the shooter should hold the gun");
            if (perk is { } perkId)
                Assert.That(server.System<SharedKsPerkSystem>().TryAddPerk(shooterUid, perkId), "the test perk should be given");
        });

        await Pair.RunTicksSync(2);
        return new Scene(testMap, shooterUid, gunUid);
    }

    private async Task<EntityUid> SpawnTarget(Scene scene, float x)
    {
        EntityUid targetUid = default;
        await Pair.Server.WaitPost(() =>
            targetUid = Pair.Server.EntMan.SpawnAtPosition(_target, new EntityCoordinates(scene.Map.Grid, x, 0.5f)));
        return targetUid;
    }

    /// <summary>
    ///     Fires one shot at a point and waits until whatever it fired has landed. Returns the bullets as they left the
    ///         gun (empty for a beam), with their damage then.
    /// </summary>
    private async Task<List<(EntityUid Uid, FixedPoint2 Damage, bool Explosive)>> Fire(Scene scene, Vector2 at)
    {
        var server = Pair.Server;
        var entMan = server.EntMan;
        var fired = new List<(EntityUid Uid, FixedPoint2 Damage, bool Explosive)>();

        await server.WaitPost(() =>
        {
            var before = new HashSet<EntityUid>();
            var projectiles = entMan.EntityQueryEnumerator<ProjectileComponent>();
            while (projectiles.MoveNext(out var uid, out _))
                before.Add(uid);

            var gun = new Entity<GunComponent>(scene.GunUid, entMan.GetComponent<GunComponent>(scene.GunUid));
            Assert.That(server.System<SharedGunSystem>().AttemptShoot(scene.ShooterUid, gun, new EntityCoordinates(scene.Map.Grid, at.X, at.Y)),
                "the gun failed to fire");

            projectiles = entMan.EntityQueryEnumerator<ProjectileComponent>();
            while (projectiles.MoveNext(out var uid, out var projectileComponent))
            {
                if (!before.Contains(uid) && projectileComponent.Shooter == scene.ShooterUid)
                    fired.Add((uid, projectileComponent.Damage.GetTotal(), entMan.HasComponent<KsExplosiveShotComponent>(uid)));
            }
        });

        for (var i = 0; i < 120 && fired.Any(shot => !entMan.Deleted(shot.Uid)); i++)
            await Pair.RunTicksSync(1);

        // Let the gun's fire rate pass and anything the hit queued (an explosion) run.
        await Pair.RunTicksSync(5);
        return fired;
    }

    private FixedPoint2 TotalDamage(EntityUid uid) => Pair.Server.System<DamageableSystem>().GetTotalDamage(uid);

    private KsDamageRampAbilityComponent Ramp(Scene scene) => Pair.Server.EntMan.GetComponent<KsDamageRampAbilityComponent>(scene.ShooterUid);

    private FixedPoint2 Expected(FixedPoint2 baseDamage, KsDamageRampAbilityComponent ramp, int hits)
    {
        return baseDamage * (1f + MathF.Min(hits * ramp.BonusPerHit, ramp.MaxBonus));
    }

    private FixedPoint2 BulletDamage()
    {
        Pair.Server.ProtoMan.Index(_bullet).TryGetComponent<ProjectileComponent>(out var projectile, Pair.Server.EntMan.ComponentFactory);
        return projectile!.Damage.GetTotal();
    }

    [Test]
    public async Task RampGrowsPerBulletHitUpToItsCapAndIsLostOnReloadAndDrop()
    {
        var server = Pair.Server;
        var scene = await SetUp(_rampPerk, _rifle);
        var targetUid = await SpawnTarget(scene, 3.5f);
        var baseDamage = BulletDamage();
        var ramp = Ramp(scene);
        var target = new Vector2(3.5f, 0.5f);

        // Four rounds: each lands, each later one carries one more hit's bonus, the last past the cap.
        for (var hits = 0; hits < 4; hits++)
        {
            var shots = await Fire(scene, target);
            Assert.That(shots, Has.Count.EqualTo(1));
            Assert.That(shots[0].Damage, Is.EqualTo(Expected(baseDamage, ramp, hits)),
                $"the bullet fired after {hits} hits should carry their bonus, capped");
            Assert.That(ramp.Hits, Is.EqualTo(Math.Min(hits + 1, (int)MathF.Ceiling(ramp.MaxBonus / ramp.BonusPerHit))),
                "a bullet that hit a living mob should add to the ramp, until the cap");
        }

        Assert.That(TotalDamage(targetUid), Is.GreaterThan(baseDamage * 4), "the bonus should have reached the target");
        Assert.That(ramp.Hits * ramp.BonusPerHit, Is.GreaterThanOrEqualTo(ramp.MaxBonus), "the ramp should be at its cap");

        // A fresh magazine: the next round has no bonus.
        await server.WaitPost(() =>
        {
            var itemSlots = server.System<ItemSlotsSystem>();
            Assert.That(itemSlots.TryEject(scene.GunUid, SharedGunSystem.MagazineSlot, null, out _), "the magazine should come out");
            var magazineUid = server.EntMan.SpawnAtPosition(_magazine, new EntityCoordinates(scene.Map.Grid, 0.5f, 0.5f));
            Assert.That(itemSlots.TryInsert(scene.GunUid, SharedGunSystem.MagazineSlot, magazineUid, null), "the new magazine should go in");
        });

        var afterReload = await Fire(scene, target);
        Assert.That(afterReload[0].Damage, Is.EqualTo(baseDamage), "a reload should start the ramp over");
        Assert.That(ramp.Hits, Is.EqualTo(1), "and the round after it starts it again");

        // A miss adds nothing.
        await Fire(scene, new Vector2(0.5f, 6f));
        Assert.That(ramp.Hits, Is.EqualTo(1), "a round that hit nothing should not add to the ramp");

        // Dropping the gun loses it.
        await server.WaitPost(() =>
            Assert.That(server.System<SharedHandsSystem>().TryDrop(scene.ShooterUid, scene.GunUid, checkActionBlocker: false), "the gun should drop"));
        await Pair.RunTicksSync(1);
        Assert.That(ramp.Hits, Is.Zero, "dropping the gun should lose the ramp");
    }

    [Test]
    public async Task RampGrowsPerBeamHitUpToItsCapAndIsLostWhenTheGunRunsDry()
    {
        var server = Pair.Server;
        var scene = await SetUp(_rampPerk, _laser);
        var targetUid = await SpawnTarget(scene, 3.5f);
        var ramp = Ramp(scene);
        var target = new Vector2(3.5f, 0.5f);
        var capacity = server.System<SharedGunSystem>().GetAmmoCapacity(scene.GunUid);
        var capHits = (int)MathF.Ceiling(ramp.MaxBonus / ramp.BonusPerHit);
        Assert.That(capacity, Is.GreaterThan(capHits + 1), "the test gun needs enough charge to pass the cap");

        FixedPoint2? firstHit = null;
        for (var shot = 0; shot < capacity; shot++)
        {
            var before = TotalDamage(targetUid);
            await Fire(scene, target);
            var dealt = TotalDamage(targetUid) - before;
            firstHit ??= dealt;

            Assert.That(dealt, Is.EqualTo(Expected(firstHit.Value, ramp, Math.Min(shot, capHits))),
                $"beam {shot + 1} should carry the bonus of the hits before it, capped");

            if (shot < capacity - 1)
                Assert.That(ramp.Hits, Is.EqualTo(Math.Min(shot + 1, capHits)), "a beam that hit a living mob should add to the ramp");
        }

        Assert.That(server.System<SharedGunSystem>().GetAmmoCount(scene.GunUid), Is.Zero, "the gun should be empty");
        Assert.That(ramp.Hits, Is.Zero, "running the gun dry should lose the ramp");
    }

    /// <summary>
    ///     A shot of several pellets adds to the ramp once, however many of them land.
    /// </summary>
    [Test]
    public async Task RampCountsAShotOnceHoweverManyPelletsLand()
    {
        var scene = await SetUp(_rampPerk, _shotgun);
        var targetUid = await SpawnTarget(scene, 3.5f);
        var ramp = Ramp(scene);
        var target = new Vector2(3.5f, 0.5f);
        Assert.That((int)MathF.Ceiling(ramp.MaxBonus / ramp.BonusPerHit), Is.GreaterThan(2), "the test ramp needs room for two shots");

        var before = TotalDamage(targetUid);
        var pellets = await Fire(scene, target);
        Assert.That(pellets, Has.Count.GreaterThan(1), "the test shell should fire several pellets");
        Assert.That(TotalDamage(targetUid) - before, Is.EqualTo(BulletDamage() * pellets.Count), "every pellet should have landed");
        Assert.That(ramp.Hits, Is.EqualTo(1), "one shot should add to the ramp once, however many of its pellets land");

        await Fire(scene, target);
        Assert.That(ramp.Hits, Is.EqualTo(2), "the next shot should add once more");
    }

    /// <summary>
    ///     A gun that charges back up by itself has more shots than at the last one without being reloaded: the ramp
    ///         stays.
    /// </summary>
    [Test]
    public async Task RampKeepsBuildingOnAGunThatRechargesItself()
    {
        var server = Pair.Server;
        var gunSystem = server.System<SharedGunSystem>();
        var scene = await SetUp(_rampPerk, _rechargingLaser);
        var targetUid = await SpawnTarget(scene, 3.5f);
        var ramp = Ramp(scene);
        var target = new Vector2(3.5f, 0.5f);
        var capacity = gunSystem.GetAmmoCapacity(scene.GunUid);

        var before = TotalDamage(targetUid);
        await Fire(scene, target);
        var firstHit = TotalDamage(targetUid) - before;
        Assert.That(ramp.Hits, Is.EqualTo(1), "the first beam should add to the ramp");

        for (var i = 0; i < 600 && gunSystem.GetAmmoCount(scene.GunUid) < capacity; i++)
            await Pair.RunTicksSync(1);
        Assert.That(gunSystem.GetAmmoCount(scene.GunUid), Is.EqualTo(capacity), "the test gun should charge back to full");

        before = TotalDamage(targetUid);
        await Fire(scene, target);
        Assert.That(TotalDamage(targetUid) - before, Is.EqualTo(Expected(firstHit, ramp, 1)), "a gun that recharged itself should keep its ramp");
        Assert.That(ramp.Hits, Is.EqualTo(2), "and the beam after the recharge should add to it");
    }

    /// <summary>
    ///     The perks' networked state never names a gun or magazine that is gone: generating it would log an error.
    /// </summary>
    [Test]
    public async Task PerkStateSurvivesItsGunAndMagazineBeingDeleted()
    {
        var server = Pair.Server;
        var entMan = server.EntMan;
        var scene = await SetUp(_rampPerk, _rifle);
        await server.WaitPost(() =>
            Assert.That(server.System<SharedKsPerkSystem>().TryAddPerk(scene.ShooterUid, _explosivePerk), "the second test perk should be given"));
        await Fire(scene, new Vector2(0.5f, 6f));

        void GetStates()
        {
            entMan.GetComponentState(entMan.EventBus, entMan.GetComponent<KsDamageRampAbilityComponent>(scene.ShooterUid), null, GameTick.Zero);
            entMan.GetComponentState(entMan.EventBus, entMan.GetComponent<KsExplosiveLastRoundsAbilityComponent>(scene.ShooterUid), null, GameTick.Zero);
        }

        // The magazine taken out and destroyed, the gun still held.
        await server.WaitPost(() =>
        {
            Assert.That(server.System<KsPerkGunSystem>().TryGetMagazine(scene.GunUid, out var magazineUid), "the gun should be loaded");
            Assert.That(server.System<ItemSlotsSystem>().TryEject(scene.GunUid, SharedGunSystem.MagazineSlot, null, out _), "the magazine should come out");
            entMan.DeleteEntity(magazineUid);
        });
        await Pair.RunTicksSync(1);
        await server.WaitPost(GetStates);

        // Then the gun, in the hand.
        await server.WaitPost(() => entMan.DeleteEntity(scene.GunUid));
        await Pair.RunTicksSync(1);
        await server.WaitPost(GetStates);
        await Pair.RunTicksSync(2);
    }

    [Test]
    public async Task CloseRangeBonusIsFullUpCloseAndGoneFarAway()
    {
        var scene = await SetUp(_closePerk, _rifle);
        var bonus = Pair.Server.EntMan.GetComponent<KsCloseRangeBonusAbilityComponent>(scene.ShooterUid);
        var baseDamage = BulletDamage();

        // The distance is the bullet's own flight, which ends at the target's edge: a little short of its centre.
        var nearUid = await SpawnTarget(scene, 0.5f + bonus.FullBonusRange - 0.2f);
        await Fire(scene, new Vector2(0.5f + bonus.FullBonusRange - 0.2f, 0.5f));
        Assert.That(TotalDamage(nearUid), Is.EqualTo(baseDamage * (1f + bonus.MaxBonus)), "a point-blank hit should get the whole bonus");
        await Pair.Server.WaitPost(() => Pair.Server.EntMan.DeleteEntity(nearUid));

        var midDistance = (bonus.FullBonusRange + bonus.NoBonusRange) / 2f;
        var midUid = await SpawnTarget(scene, 0.5f + midDistance);
        await Fire(scene, new Vector2(0.5f + midDistance, 0.5f));
        Assert.That(TotalDamage(midUid), Is.GreaterThan(baseDamage).And.LessThan(baseDamage * (1f + bonus.MaxBonus)),
            "a hit between the two ranges should get part of the bonus");
        await Pair.Server.WaitPost(() => Pair.Server.EntMan.DeleteEntity(midUid));

        var farUid = await SpawnTarget(scene, 0.5f + bonus.NoBonusRange + 2f);
        await Fire(scene, new Vector2(0.5f + bonus.NoBonusRange + 2f, 0.5f));
        Assert.That(TotalDamage(farUid), Is.EqualTo(baseDamage), "a hit past the far range should get none");
    }

    [Test]
    public async Task FalloffLeavesCloseHitsAloneAndFloorsFarOnes()
    {
        var scene = await SetUp(_falloffPerk, _rifle);
        var falloff = Pair.Server.EntMan.GetComponent<KsRangeFalloffAbilityComponent>(scene.ShooterUid);
        var baseDamage = BulletDamage();

        var nearUid = await SpawnTarget(scene, 0.5f + falloff.StartRange - 0.5f);
        await Fire(scene, new Vector2(0.5f + falloff.StartRange - 0.5f, 0.5f));
        Assert.That(TotalDamage(nearUid), Is.EqualTo(baseDamage), "a hit inside the start range should lose nothing");
        await Pair.Server.WaitPost(() => Pair.Server.EntMan.DeleteEntity(nearUid));

        var midDistance = (falloff.StartRange + falloff.EndRange) / 2f;
        var midUid = await SpawnTarget(scene, 0.5f + midDistance);
        await Fire(scene, new Vector2(0.5f + midDistance, 0.5f));
        Assert.That(TotalDamage(midUid), Is.LessThan(baseDamage).And.GreaterThan(baseDamage * falloff.MinMultiplier),
            "a hit between the two ranges should lose part of its damage");
        await Pair.Server.WaitPost(() => Pair.Server.EntMan.DeleteEntity(midUid));

        var farUid = await SpawnTarget(scene, 0.5f + falloff.EndRange + 2f);
        await Fire(scene, new Vector2(0.5f + falloff.EndRange + 2f, 0.5f));
        Assert.That(TotalDamage(farUid), Is.EqualTo(baseDamage * falloff.MinMultiplier), "a hit past the end range should be at the floor");
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task EmptyMagazineDropsOutOfTheGun(bool withPerk)
    {
        var server = Pair.Server;
        var scene = await SetUp(withPerk ? _dropPerk : (ProtoId<KsPerkPrototype>?)null, _rifle);
        var gunSystem = server.System<SharedGunSystem>();
        var perkGunSystem = server.System<KsPerkGunSystem>();
        EntityUid magazineUid = default;

        await server.WaitPost(() => Assert.That(perkGunSystem.TryGetMagazine(scene.GunUid, out magazineUid), "the gun should come loaded"));

        var rounds = gunSystem.GetAmmoCount(scene.GunUid);
        for (var i = 0; i < rounds; i++)
        {
            await Fire(scene, new Vector2(0.5f, 6f));
            if (i < rounds - 1)
                Assert.That(perkGunSystem.TryGetMagazine(scene.GunUid, out _), "a magazine with rounds left should stay in");
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(perkGunSystem.TryGetMagazine(scene.GunUid, out _), Is.EqualTo(!withPerk),
                withPerk ? "the empty magazine should drop out" : "without the perk the empty magazine stays in");
            Assert.That(server.EntMan.Deleted(magazineUid), Is.False, "the magazine is dropped, not deleted");
        });
    }

    [Test]
    public async Task LastRoundsOfAFullMagazineExplodeAndSpareStructures()
    {
        var server = Pair.Server;
        var scene = await SetUp(_explosivePerk, _rifle);
        var targetUid = await SpawnTarget(scene, 3.5f);
        EntityUid structureUid = default;
        await server.WaitPost(() =>
            structureUid = server.EntMan.SpawnAtPosition(_structure, new EntityCoordinates(scene.Map.Grid, 3.5f, 1.5f)));

        var ability = server.EntMan.GetComponent<KsExplosiveLastRoundsAbilityComponent>(scene.ShooterUid);
        var capacity = server.System<SharedGunSystem>().GetAmmoCount(scene.GunUid);
        var explosiveRounds = KsExplosiveLastRoundsSystem.GetExplosiveRounds(ability, capacity);
        Assert.That(explosiveRounds, Is.LessThan(capacity), "the test magazine should have ordinary rounds before the explosive ones");

        var target = new Vector2(3.5f, 0.5f);
        var baseDamage = BulletDamage();
        for (var round = 0; round < capacity; round++)
        {
            var before = TotalDamage(targetUid);
            var shots = await Fire(scene, target);
            var explosive = round >= capacity - explosiveRounds;
            Assert.That(shots[0].Explosive, Is.EqualTo(explosive),
                explosive ? $"round {round + 1} of {capacity} should be explosive" : $"round {round + 1} of {capacity} should be ordinary");

            var dealt = TotalDamage(targetUid) - before;
            if (explosive)
                Assert.That(dealt, Is.GreaterThan(baseDamage), "an explosive round should hurt more than its bullet");
            else
                Assert.That(dealt, Is.EqualTo(baseDamage), "an ordinary round deals its bullet's damage");
        }

        Assert.That(TotalDamage(structureUid), Is.EqualTo(FixedPoint2.Zero), "the blast should spare the structure beside the target");

        // A magazine that goes in partly spent gets no explosive rounds.
        await server.WaitPost(() =>
        {
            var itemSlots = server.System<ItemSlotsSystem>();
            itemSlots.TryEject(scene.GunUid, SharedGunSystem.MagazineSlot, null, out _);
            var magazineUid = server.EntMan.SpawnAtPosition(_magazine, new EntityCoordinates(scene.Map.Grid, 0.5f, 0.5f));
            var ballistic = server.EntMan.GetComponent<BallisticAmmoProviderComponent>(magazineUid);
            server.System<SharedGunSystem>().SetBallisticUnspawned((magazineUid, ballistic), ballistic.UnspawnedCount - 1);
            Assert.That(itemSlots.TryInsert(scene.GunUid, SharedGunSystem.MagazineSlot, magazineUid, null), "the new magazine should go in");
        });

        var partialRounds = server.System<SharedGunSystem>().GetAmmoCount(scene.GunUid);
        for (var round = 0; round < partialRounds; round++)
        {
            var shots = await Fire(scene, target);
            Assert.That(shots[0].Explosive, Is.False, "a magazine that went in partly spent should have no explosive rounds");
        }
    }
}
