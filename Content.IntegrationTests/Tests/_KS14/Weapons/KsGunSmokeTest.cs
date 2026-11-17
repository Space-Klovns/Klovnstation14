#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._KS14.Weapons.GunSmoke;
using Content.Shared._KS14.Particles;
using Content.Shared._KS14.Weapons.GunSmoke;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Weapons;

/// <summary>
///     TGMC's gun smoke: a puff out of the muzzle along each shot, shown to the shooter at once; and a turret's smoke
///         after a long burst, which stops once it cools.
/// </summary>
[TestOf(typeof(KsGunFiringSmokeSystem))]
public sealed class KsGunSmokeTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman"; // combat mode and hands, to fire a gun

    private const string TestGun = "KsGunSmokeTestGun";
    private readonly EntProtoId _testHotGun = "KsGunSmokeTestHotGun";
    private readonly ProtoId<KsParticlesPrototype> _testPuff = "KsGunSmokeTestPuff";

    [TestPrototypes]
    private const string Prototypes = $@"
- type: ksParticles
  id: KsGunSmokeTestPuff
  burst: 1
  count: 1
  spawning: 0
  oneShot: true
  lifespan: 5

- type: ksParticles
  id: KsGunSmokeTestOverheat
  spawning: 10

- type: entity
  id: {TestGun}
  components:
  - type: Item
  - type: Gun
    fireRate: 2
    minAngle: 0
    maxAngle: 0
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
  - type: BasicEntityAmmoProvider
    proto: CartridgePistol
    capacity: 10
    count: 10
  - type: KsGunFiringSmoke
    prototype: KsGunSmokeTestPuff

- type: entity
  id: KsGunSmokeTestHotGun
  components:
  - type: KsParticles
    prototype: KsGunSmokeTestOverheat
    emitting: false
  - type: KsGunOverheatSmoke
    heatPerShot: 10
    coolPerSecond: 10
    smokeAt: 30
    stopBelow: 10
";

    /// <summary>
    ///     A shot puffs one smoke, played on the shooter's client at once (its own copy), flying along the shot.
    /// </summary>
    [Test]
    public async Task ShotPuffsOneSmokeAlongIt()
    {
        var shooterPosition = Vector2.Zero;
        await Server.WaitPost(() => shooterPosition = SEntMan.System<SharedTransformSystem>().GetWorldPosition(SPlayer));

        var gunNetEntity = await PlaceInHands(TestGun);
        await SetCombatMode(true);
        await RunSeconds(2f); // the pickup cooldown

        var targetCoordinates = SEntMan.GetNetCoordinates(new EntityCoordinates(MapData.MapUid, shooterPosition + new Vector2(0f, 10f)));
        await Client.WaitPost(() => CEntMan.RaisePredictiveEvent(new RequestShootEvent { Gun = gunNetEntity, Coordinates = targetCoordinates }));

        var clientSidePuffs = new Dictionary<EntityUid, Vector2>();
        for (var tick = 0; tick < 30; tick++)
        {
            await RunTicks(1);
            await Client.WaitPost(() =>
            {
                var transformSystem = CEntMan.System<SharedTransformSystem>();
                var query = CEntMan.AllEntityQueryEnumerator<KsParticlesComponent>();
                while (query.MoveNext(out var uid, out var particlesComponent))
                {
                    if (particlesComponent.Prototype != _testPuff || !CEntMan.IsClientSide(uid) || clientSidePuffs.ContainsKey(uid))
                        continue;

                    var parentUid = CEntMan.GetComponent<TransformComponent>(uid).ParentUid;
                    var parentRotation = parentUid.IsValid() ? transformSystem.GetWorldRotation(parentUid) : Angle.Zero;
                    clientSidePuffs[uid] = (parentRotation + particlesComponent.Rotation).RotateVec(new Vector2(0f, -1f));
                }
            });
        }

        Assert.Multiple(() =>
        {
            Assert.That(clientSidePuffs, Has.Count.EqualTo(1), "the shot did not puff exactly one smoke on the shooter's client");
            foreach (var travel in clientSidePuffs.Values)
                Assert.That(Vector2.Dot(travel, new Vector2(0f, 1f)), Is.GreaterThan(0.99f), $"the smoke flies {travel}, not along the shot");
        });
    }

    /// <summary>
    ///     A burst heats the gun until it smokes; left alone, it cools and stops. A shot or two does neither.
    /// </summary>
    [Test]
    [TestOf(typeof(KsGunOverheatSmokeSystem))]
    public async Task LongBurstSmokesUntilItCools()
    {
        EntityUid gunUid = default;
        await Server.WaitPost(() => gunUid = SEntMan.SpawnEntity(_testHotGun, new MapCoordinates(MapData.MapCoords.Position + new Vector2(0f, 20f), MapData.MapId)));

        bool Emitting() => SEntMan.GetComponent<KsParticlesComponent>(gunUid).Emitting;
        void Fire()
        {
            var shotEv = new GunShotEvent(gunUid, new(), new EntityCoordinates(gunUid, Vector2.Zero), new EntityCoordinates(gunUid, new Vector2(1f, 0f)));
            SEntMan.EventBus.RaiseLocalEvent(gunUid, ref shotEv);
        }

        await Server.WaitPost(() =>
        {
            Fire();
            Fire();
        });
        await RunTicks(1);
        await Server.WaitAssertion(() => Assert.That(Emitting(), Is.False, "two shots made the gun smoke"));

        await Server.WaitPost(() =>
        {
            for (var i = 0; i < 4; i++)
                Fire();
        });
        await RunTicks(1);
        await Server.WaitAssertion(() => Assert.That(Emitting(), "a burst past smokeAt did not make the gun smoke"));

        // From about 60 heat, cooling at 10 a second to stopBelow 10: about 5 s.
        await RunSeconds(7f);
        await Server.WaitAssertion(() => Assert.That(Emitting(), Is.False, "the gun kept smoking after it cooled"));
    }
}
