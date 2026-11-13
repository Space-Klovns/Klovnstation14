#nullable enable
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Projectiles;
using Content.Shared._Trauma.Projectiles;
using Content.Shared.Damage.Systems;
using Content.Shared.Projectiles;
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

    private readonly EntProtoId _bullet = "KsFalloffTestBullet";
    private readonly EntProtoId _target = "KsFalloffTestTarget";

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
    private async Task<float> DamageDealtAt(MapId mapId, float distance)
    {
        var server = Pair.Server;
        var damage = 0f;

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var transformSystem = entMan.System<SharedTransformSystem>();
            var bulletUid = entMan.SpawnEntity(_bullet, new MapCoordinates(Vector2.Zero, mapId));

            // The same event the gun raises when it fires, which is what stamps the origin.
            var shotEvent = new PlayerShotProjectileEvent(bulletUid, bulletUid);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref shotEvent);

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
        var testMap = await Pair.CreateTestMap();

        float nearDamage = 0f, farDamage = 0f, midDamage = 0f;
        KsProjectileFalloffComponent? falloff = null;
        await Pair.Server.WaitPost(() =>
        {
            var probe = Pair.Server.EntMan.SpawnEntity(_bullet, new MapCoordinates(Vector2.Zero, testMap.MapId));
            falloff = Pair.Server.EntMan.GetComponent<KsProjectileFalloffComponent>(probe);
            Pair.Server.EntMan.DeleteEntity(probe);
        });

        nearDamage = await DamageDealtAt(testMap.MapId, 4f);
        midDamage = await DamageDealtAt(testMap.MapId, 11f);
        farDamage = await DamageDealtAt(testMap.MapId, 30f);

        Assert.That(nearDamage, Is.GreaterThan(0f));
        Assert.Multiple(() =>
        {
            Assert.That(farDamage / nearDamage, Is.EqualTo(falloff!.MinMultiplier).Within(0.01f), "far hit does the minimum fraction");
            Assert.That(midDamage / nearDamage, Is.EqualTo(0.625f).Within(0.01f), "mid hit is on the line between");
        });
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
