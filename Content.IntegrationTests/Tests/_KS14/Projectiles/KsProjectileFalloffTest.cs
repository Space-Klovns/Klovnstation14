#nullable enable
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Projectiles;
using Content.Shared._Trauma.Projectiles;
using Content.Shared.Damage.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Projectiles;

[TestFixture]
[TestOf(typeof(KsProjectileFalloffSystem))]
public sealed class KsProjectileFalloffTest : GameTest
{
    private static readonly EntProtoId PistolBullet = "BulletPistol";
    private static readonly EntProtoId LaserBullet = "BulletLaser";
    private static readonly EntProtoId RocketBullet = "BulletRocket";
    private static readonly EntProtoId HristovSniper = "WeaponSniperHristov";
    private static readonly EntProtoId MosinSniper = "WeaponSniperMosin";
    private static readonly EntProtoId OtherLightRifle = "WeaponRifleAk";

    // The Field Commander bots' guns live in the private module. Instance fields: the YAML linter checks static ones, and
    //     has to pass without the module.
    private readonly EntProtoId[] _botGuns = ["KsWeaponPistolMk1FieldCommander", "KsWeaponShotgunBulldogFieldCommander", "KsWeaponRifleEstocFieldCommander"];

    private readonly EntProtoId _bullet = "KsFalloffTestBullet";
    private readonly EntProtoId _target = "KsFalloffTestTarget";
    private readonly EntProtoId _plainBullet = "KsFalloffTestPlainBullet";
    private readonly EntProtoId _plainGun = "KsFalloffTestGun";
    private readonly EntProtoId _scaledGun = "KsFalloffTestScaledGun";
    private readonly EntProtoId _noFalloffGun = "KsFalloffTestNoFalloffGun";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsFalloffTestBullet
  components:
  - type: Physics
    bodyType: Dynamic
  - type: Projectile
    damage:
      types:
        Blunt: 100
  - type: KsProjectileFalloff
    fullDamageRange: 6
    minDamageRange: 16
    minMultiplier: 0.25

- type: entity
  id: KsFalloffTestPlainBullet
  components:
  - type: Physics
    bodyType: Dynamic
  - type: Projectile
    damage:
      types:
        Blunt: 100

- type: entity
  id: KsFalloffTestGun

- type: entity
  id: KsFalloffTestScaledGun
  components:
  - type: KsGunProjectileFalloff
    rangeScale: 2

- type: entity
  id: KsFalloffTestNoFalloffGun
  components:
  - type: KsGunProjectileFalloff
    minMultiplier: 1

- type: entity
  id: KsFalloffTestTarget
  components:
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      body:
        shape: !type:PhysShapeCircle
          radius: 0.4
        hard: true
        layer:
        - MobLayer
  - type: Damageable
  - type: Injurable
";

    [Test]
    public void MultiplierIsFullThenLerpsThenClamps()
    {
        var component = new KsProjectileFalloffComponent
        {
            FullDamageRange = 8f,
            MinDamageRange = 20f,
            MinMultiplier = 0.5f,
        };

        Assert.Multiple(() =>
        {
            Assert.That(KsProjectileFalloff.GetMultiplier(0f, component), Is.EqualTo(1f), "point blank");
            Assert.That(KsProjectileFalloff.GetMultiplier(8f, component), Is.EqualTo(1f), "edge of full damage");
            Assert.That(KsProjectileFalloff.GetMultiplier(14f, component), Is.EqualTo(0.75f).Within(0.001f), "halfway");
            Assert.That(KsProjectileFalloff.GetMultiplier(20f, component), Is.EqualTo(0.5f).Within(0.001f), "end of falloff");
            Assert.That(KsProjectileFalloff.GetMultiplier(200f, component), Is.EqualTo(0.5f), "far clamp");
        });
    }

    /// <summary>
    /// Fires a bullet from the origin, moves it <paramref name="distance"/> tiles along, hits a fresh target and
    /// returns the damage it dealt.
    /// </summary>
    private async Task<float> DamageDealtAt(MapId mapId, float distance, EntProtoId bullet, EntProtoId? gun = null)
    {
        var server = Pair.Server;
        var damage = 0f;

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var transformSystem = entMan.System<SharedTransformSystem>();
            var bulletUid = entMan.SpawnEntity(bullet, new MapCoordinates(Vector2.Zero, mapId));

            // The real fire path: it stamps the origin and applies the gun's tuning.
            var gunUid = entMan.SpawnEntity(gun ?? _plainGun, new MapCoordinates(Vector2.Zero, mapId));
            entMan.System<SharedGunSystem>().ShootProjectile(bulletUid, Vector2.UnitX, Vector2.Zero, gunUid, gunUid);

            transformSystem.SetMapCoordinates(bulletUid, new MapCoordinates(new Vector2(distance, 0f), mapId));
            var targetUid = entMan.SpawnEntity(_target, new MapCoordinates(new Vector2(distance, 0f), mapId));

            entMan.System<PredictedProjectileSystem>().DoHit(bulletUid, targetUid);
            damage = (float)entMan.System<DamageableSystem>().GetTotalDamage(targetUid);
        });

        return damage;
    }

    [Test]
    public async Task HitDamageFallsOffWithDistance()
    {
        await OverrideCVar(Side.Server, KsCCVars.ProjectileDamageFalloffEverywhere, true);
        var testMap = await Pair.CreateTestMap();

        float nearDamage = 0f, farDamage = 0f, midDamage = 0f;
        KsProjectileFalloffComponent? falloff = null;
        await Pair.Server.WaitPost(() =>
        {
            var probe = Pair.Server.EntMan.SpawnEntity(_bullet, new MapCoordinates(Vector2.Zero, testMap.MapId));
            falloff = Pair.Server.EntMan.GetComponent<KsProjectileFalloffComponent>(probe);
            Pair.Server.EntMan.DeleteEntity(probe);
        });

        nearDamage = await DamageDealtAt(testMap.MapId, 4f, _bullet);
        midDamage = await DamageDealtAt(testMap.MapId, 11f, _bullet);
        farDamage = await DamageDealtAt(testMap.MapId, 30f, _bullet);

        Assert.That(nearDamage, Is.GreaterThan(0f));
        Assert.Multiple(() =>
        {
            Assert.That(farDamage / nearDamage, Is.EqualTo(falloff!.MinMultiplier).Within(0.01f), "far hit does the minimum fraction");
            Assert.That(midDamage / nearDamage, Is.EqualTo(0.625f).Within(0.01f), "mid hit is on the line between");
        });
    }

    [Test]
    public async Task GunTuningChangesTheFalloffOfItsBullets()
    {
        await OverrideCVar(Side.Server, KsCCVars.ProjectileDamageFalloffEverywhere, true);
        var testMap = await Pair.CreateTestMap();
        var mapId = testMap.MapId;

        // The bullet's own numbers are 6 / 16 / 0.25.
        var near = await DamageDealtAt(mapId, 2f, _bullet);
        var plainMid = await DamageDealtAt(mapId, 11f, _bullet, _plainGun);
        var plainFar = await DamageDealtAt(mapId, 30f, _bullet, _plainGun);
        var scaledMid = await DamageDealtAt(mapId, 11f, _bullet, _scaledGun);
        var scaledFar = await DamageDealtAt(mapId, 30f, _bullet, _scaledGun);
        var noFalloffFar = await DamageDealtAt(mapId, 30f, _bullet, _noFalloffGun);

        Assert.That(near, Is.GreaterThan(0f));
        Assert.Multiple(() =>
        {
            Assert.That(plainMid / near, Is.EqualTo(0.625f).Within(0.01f), "a plain gun keeps the bullet's values");
            Assert.That(plainFar / near, Is.EqualTo(0.25f).Within(0.01f), "a plain gun keeps the bullet's values far out");
            Assert.That(scaledMid / near, Is.EqualTo(1f).Within(0.01f), "double range: 11 tiles is inside the 12 of full damage");
            Assert.That(scaledFar / near, Is.EqualTo(0.325f).Within(0.01f), "double range: 30 tiles is 90% of the way from 12 to 32");
            Assert.That(noFalloffFar / near, Is.EqualTo(1f).Within(0.01f), "a min multiplier override of 1 turns falloff off");
        });
    }

    [Test]
    public async Task MosinRoundDoesFullDamageAtRangeButOtherLightRiflesFallOff()
    {
        await OverrideCVar(Side.Server, KsCCVars.ProjectileDamageFalloffEverywhere, true);
        // The Mosin's real round, which other guns fire too.
        Assert.That(SProtoMan.Index(MosinSniper).TryGetComponent<Content.Shared.Weapons.Ranged.Components.BallisticAmmoProviderComponent>(out var provider, Server.EntMan.ComponentFactory));
        Assert.That(SProtoMan.Index(provider!.Proto!.Value).TryGetComponent<Content.Shared.Weapons.Ranged.Components.CartridgeAmmoComponent>(out var cartridgeAmmo, Server.EntMan.ComponentFactory));
        var lightRifleBullet = cartridgeAmmo!.Prototype;

        var testMap = await Pair.CreateTestMap();
        var near = await DamageDealtAt(testMap.MapId, 4f, lightRifleBullet, MosinSniper);
        var mosinFar = await DamageDealtAt(testMap.MapId, 30f, lightRifleBullet, MosinSniper);
        var akFar = await DamageDealtAt(testMap.MapId, 30f, lightRifleBullet, OtherLightRifle);

        Assert.That(near, Is.GreaterThan(0f));
        Assert.Multiple(() =>
        {
            Assert.That(mosinFar, Is.EqualTo(near).Within(0.01f), "the Mosin does not fall off");
            Assert.That(akFar, Is.LessThan(near), "the same cartridge from another rifle still falls off");
        });
    }

    [Test]
    public async Task FalloffCanBeSwitchedOffWithTheCVar()
    {
        await OverrideCVar(Side.Server, KsCCVars.ProjectileDamageFalloffEverywhere, true);
        var testMap = await Pair.CreateTestMap();
        var near = await DamageDealtAt(testMap.MapId, 2f, _bullet);
        var farOn = await DamageDealtAt(testMap.MapId, 30f, _bullet);

        await OverrideCVar(Side.Server, KsCCVars.ProjectileDamageFalloff, false);
        var farOff = await DamageDealtAt(testMap.MapId, 30f, _bullet);

        Assert.That(near, Is.GreaterThan(0f));
        Assert.Multiple(() =>
        {
            Assert.That(farOn / near, Is.EqualTo(0.25f).Within(0.01f), "falls off while on");
            Assert.That(farOff, Is.EqualTo(near).Within(0.01f), "full damage while off");
        });
    }

    [Test]
    public async Task OnlyOptedInGunsMakeBulletsFallOffWhenNotEverywhere()
    {
        var testMap = await Pair.CreateTestMap();
        var mapId = testMap.MapId;

        // Only a gun with the component qualifies. Set explicitly, whatever an earlier test on this pair left.
        await OverrideCVar(Side.Server, KsCCVars.ProjectileDamageFalloff, true);
        await OverrideCVar(Side.Server, KsCCVars.ProjectileDamageFalloffEverywhere, false);
        var near = await DamageDealtAt(mapId, 2f, _bullet);
        var dataBulletPlainGun = await DamageDealtAt(mapId, 30f, _bullet, _plainGun);
        var dataBulletOptedInGun = await DamageDealtAt(mapId, 30f, _bullet, _scaledGun);
        var bareBulletPlainGun = await DamageDealtAt(mapId, 30f, _plainBullet, _plainGun);
        var bareBulletOptedInGun = await DamageDealtAt(mapId, 30f, _plainBullet, _scaledGun);
        var bareBulletOptedInGunNear = await DamageDealtAt(mapId, 2f, _plainBullet, _scaledGun);

        Assert.That(near, Is.GreaterThan(0f));
        Assert.Multiple(() =>
        {
            Assert.That(dataBulletPlainGun, Is.EqualTo(near).Within(0.01f), "a plain gun's bullet does full damage");
            Assert.That(dataBulletOptedInGun / near, Is.EqualTo(0.325f).Within(0.01f), "an opted-in gun's bullet falls off");
            Assert.That(bareBulletPlainGun, Is.EqualTo(near).Within(0.01f), "a bullet with no data from a plain gun does full damage");
            // 100 damage, default 8 / 20 / 0.5 doubled in range by the gun: 16 / 40, so 30 tiles is 0.5833 of the way.
            Assert.That(bareBulletOptedInGun / bareBulletOptedInGunNear, Is.EqualTo(1f - 0.5f * (14f / 24f)).Within(0.01f), "a gun adds falloff to a bullet that has none");
        });
    }

    [Test]
    public async Task TheMasterCVarTurnsOffEverything()
    {
        var testMap = await Pair.CreateTestMap();
        await OverrideCVar(Side.Server, KsCCVars.ProjectileDamageFalloffEverywhere, true);
        await OverrideCVar(Side.Server, KsCCVars.ProjectileDamageFalloff, false);

        var near = await DamageDealtAt(testMap.MapId, 2f, _bullet);
        var plainFar = await DamageDealtAt(testMap.MapId, 30f, _bullet, _plainGun);
        var optedInFar = await DamageDealtAt(testMap.MapId, 30f, _bullet, _scaledGun);

        Assert.That(near, Is.GreaterThan(0f));
        Assert.Multiple(() =>
        {
            Assert.That(plainFar, Is.EqualTo(near).Within(0.01f));
            Assert.That(optedInFar, Is.EqualTo(near).Within(0.01f));
        });
    }

    [Test]
    public void FieldCommanderBotGunsOptIn()
    {
        if (!SProtoMan.HasIndex(_botGuns[0]))
            Assert.Ignore("_KsModule prototypes are not present.");

        foreach (var botGun in _botGuns)
        {
            Assert.That(SProtoMan.Index(botGun).TryGetComponent<KsGunProjectileFalloffComponent>(out _, Server.EntMan.ComponentFactory), $"{botGun} opts in");
            Assert.That(CProtoMan.Index(botGun).TryGetComponent<KsGunProjectileFalloffComponent>(out _, Client.EntMan.ComponentFactory), $"{botGun} opts in on the client");
        }

        // An ordinary rifle does not.
        Assert.That(SProtoMan.Index(OtherLightRifle).TryGetComponent<KsGunProjectileFalloffComponent>(out _, Server.EntMan.ComponentFactory), Is.False);
    }

    [Test]
    public async Task SniperRoundDoesFullDamageAtRange()
    {
        await OverrideCVar(Side.Server, KsCCVars.ProjectileDamageFalloffEverywhere, true);
        // The Hristov's real round: gun -> cartridge -> bullet.
        var gun = SProtoMan.Index(HristovSniper);
        Assert.That(gun.TryGetComponent<Content.Shared.Weapons.Ranged.Components.BallisticAmmoProviderComponent>(out var provider, Server.EntMan.ComponentFactory));
        var cartridge = SProtoMan.Index(provider!.Proto!.Value);
        Assert.That(cartridge.TryGetComponent<Content.Shared.Weapons.Ranged.Components.CartridgeAmmoComponent>(out var cartridgeAmmo, Server.EntMan.ComponentFactory));
        var sniperBullet = cartridgeAmmo!.Prototype;

        var testMap = await Pair.CreateTestMap();
        var near = await DamageDealtAt(testMap.MapId, 4f, sniperBullet);
        var far = await DamageDealtAt(testMap.MapId, 30f, sniperBullet);

        Assert.That(near, Is.GreaterThan(0f));
        Assert.That(far, Is.EqualTo(near).Within(0.01f), "a sniper round does not fall off");
    }

    [Test]
    public async Task UnstampedProjectileDoesFullDamage()
    {
        var testMap = await Pair.CreateTestMap();
        var server = Pair.Server;
        var damages = new float[2];

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            for (var i = 0; i < 2; i++)
            {
                var distance = i == 0 ? 2f : 40f;
                var bulletUid = entMan.SpawnEntity(_bullet, new MapCoordinates(Vector2.Zero, testMap.MapId));
                entMan.System<SharedTransformSystem>().SetMapCoordinates(bulletUid, new MapCoordinates(new Vector2(distance, 0f), testMap.MapId));
                var targetUid = entMan.SpawnEntity(_target, new MapCoordinates(new Vector2(distance, 0f), testMap.MapId));
                entMan.System<PredictedProjectileSystem>().DoHit(bulletUid, targetUid);
                damages[i] = (float)entMan.System<DamageableSystem>().GetTotalDamage(targetUid);
            }
        });

        Assert.That(damages[0], Is.GreaterThan(0f));
        Assert.That(damages[1], Is.EqualTo(damages[0]), "no origin, no falloff");
    }

    [Test]
    public void BallisticBulletsGetTheComponentFromThePartial()
    {
        // Both sides: the client predicts the hit, so it must have the component too.
        foreach (var protoMan in new[] { SProtoMan, CProtoMan })
        {
            var pistol = protoMan.Index(PistolBullet);
            Assert.That(pistol.TryGetComponent<KsProjectileFalloffComponent>(out _, Server.EntMan.ComponentFactory), "partial applied");
            Assert.That(pistol.TryGetComponent<ProjectileComponent>(out var projectile, Server.EntMan.ComponentFactory), "upstream projectile kept");
            Assert.That(projectile!.Damage.GetTotal(), Is.GreaterThan(Content.Shared.FixedPoint.FixedPoint2.Zero), "upstream damage kept");

            Assert.That(protoMan.Index(LaserBullet).TryGetComponent<KsProjectileFalloffComponent>(out _, Server.EntMan.ComponentFactory), Is.False, "lasers do not fall off");
            Assert.That(protoMan.Index(RocketBullet).TryGetComponent<KsProjectileFalloffComponent>(out _, Server.EntMan.ComponentFactory), Is.False, "rockets do not fall off");
        }
    }
}
