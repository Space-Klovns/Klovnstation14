#nullable enable
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._KS14.PredictedSpawning;
using Content.Shared.Mech.Components;
using Content.Shared.Mech.EntitySystems;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared;
using Robust.Shared.Audio.Components;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._KS14.Mech;

/// <summary>
///     A mech pilot's client predicts the shots of the mech's selected weapon, as it does a held gun's: the bullet flies
///         on the pilot's screen at once, rather than a round trip later when the server's arrives.
/// </summary>
[TestOf(typeof(SharedMechSystem))]
public sealed class KsMechWeaponPredictionTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman"; // combat mode, to fire a gun

    private const string TestMech = "KsMechWeaponPredictionTestMech";
    private const string TestMechBattery = "KsMechWeaponPredictionTestMechBattery";
    private const string TestWeapon = "KsMechWeaponPredictionTestWeapon";

    private readonly EntProtoId _testMech = TestMech;
    private readonly EntProtoId _testWeapon = TestWeapon;

    [TestPrototypes]
    private const string Prototypes = $@"
- type: entity
  id: {TestMechBattery}
  components:
  - type: Battery
    maxCharge: 1000
    startingCharge: 1000

- type: entity
  id: {TestMech}
  components:
  - type: Physics
    bodyType: KinematicController
  - type: Appearance
  - type: Mech
  - type: ContainerFill
    containers:
      mech-battery-slot:
      - {TestMechBattery}

- type: entity
  id: {TestWeapon}
  components:
  - type: Item
  - type: MechEquipment
  - type: Gun
    fireRate: 2
    minAngle: 0
    maxAngle: 0
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
    soundGunshot:
      path: /Audio/Weapons/Guns/Gunshots/pistol.ogg
  - type: Battery
    maxCharge: 100
    startingCharge: 100
  - type: BatteryAmmoProvider
    proto: BulletPistol
    fireCost: 40
";

    /// <summary>
    ///     Spawns the mech with the weapon fitted and selected, and puts the player in it.
    /// </summary>
    private async Task<(EntityUid MechUid, EntityUid WeaponUid)> BoardArmedMech()
    {
        var mechUid = EntityUid.Invalid;
        var weaponUid = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            var mechSystem = SEntMan.System<SharedMechSystem>();
            var playerCoordinates = SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates;
            mechUid = SEntMan.SpawnEntity(_testMech, playerCoordinates);
            weaponUid = SEntMan.SpawnEntity(_testWeapon, playerCoordinates);
            mechSystem.InsertEquipment(mechUid, weaponUid);
            mechSystem.CycleEquipment(mechUid);
            Assert.That(mechSystem.TryInsert(mechUid, SPlayer), "the player could not board the mech");
        });

        await SetCombatMode(true);
        await RunSeconds(1f); // the weapon, the mech and the pilot reach the client

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<MechComponent>(mechUid).CurrentSelectedEquipment, Is.EqualTo(weaponUid),
                "the weapon is not the mech's selected equipment");
        });

        return (mechUid, weaponUid);
    }

    private async Task Fire(EntityUid mechUid, EntityUid weaponUid)
    {
        var aimCoordinates = NetCoordinates.Invalid;
        await Server.WaitPost(() =>
        {
            var mechPosition = SEntMan.System<SharedTransformSystem>().GetWorldPosition(mechUid);
            aimCoordinates = SEntMan.GetNetCoordinates(new EntityCoordinates(MapData.MapUid, mechPosition + new Vector2(10f, 0f)));
        });

        var weaponNetEntity = SEntMan.GetNetEntity(weaponUid);
        await Client.WaitPost(() =>
        {
            CEntMan.RaisePredictiveEvent(new RequestShootEvent
            {
                Gun = weaponNetEntity,
                Coordinates = aimCoordinates,
            });
        });
    }

    /// <summary>
    ///     The pilot's client fires the mech weapon's bullet itself, keeping its own copy, as it does a held gun's. The
    ///         client cancelled every mech shot it predicted: the weapon's owning mech, which the shot checks it is in,
    ///         was never sent to it.
    /// </summary>
    [Test]
    public async Task PilotsClientFiresTheMechWeaponsBulletItself()
    {
        // The client predicts well past the last server state, as a real client with some ping does.
        var clientConfigurationManager = Client.ResolveDependency<IConfigurationManager>();
        await Client.WaitPost(() => clientConfigurationManager.SetCVar(CVars.NetPredictTickBias, 4));

        var (mechUid, weaponUid) = await BoardArmedMech();
        await Fire(mechUid, weaponUid);

        var clientWeaponUid = ToClient(weaponUid);
        var keptBullet = false;
        for (var tick = 0; tick < 10 && !keptBullet; tick++)
        {
            await RunTicks(1);
            await Client.WaitPost(() =>
            {
                var predictedSpawnSystem = CEntMan.System<KsSharedPredictedSpawnSystem>();
                var query = CEntMan.AllEntityQueryEnumerator<ProjectileComponent>();
                while (query.MoveNext(out var projectileUid, out var projectileComponent))
                {
                    keptBullet |= projectileComponent.Weapon == clientWeaponUid && predictedSpawnSystem.IsKeptLocally(projectileUid);
                }
            });
        }

        Assert.That(keptBullet, "the pilot's client never fired the mech weapon's bullet itself: the shot was not predicted");

        await RunTicks(10); // the client ran ahead of the server: the server's shot is behind it

        // The server fired it too, so the shot was real, and the pilot is not sent the server's gunshot on top of the one
        //      their client played.
        await Server.WaitAssertion(() =>
        {
            var serverBullets = 0;
            var bulletQuery = SEntMan.AllEntityQueryEnumerator<ProjectileComponent>();
            while (bulletQuery.MoveNext(out _, out var projectileComponent))
            {
                if (projectileComponent.Weapon == weaponUid)
                    serverBullets++;
            }

            Assert.That(serverBullets, Is.EqualTo(1), "the server did not fire the mech weapon's bullet");

            var gunshots = 0;
            var audioQuery = SEntMan.AllEntityQueryEnumerator<AudioComponent, TransformComponent>();
            while (audioQuery.MoveNext(out _, out var audioComponent, out var transformComponent))
            {
                if (transformComponent.ParentUid != weaponUid)
                    continue;

                gunshots++;
                Assert.That(audioComponent.ExcludedEntity, Is.EqualTo(SPlayer),
                    "the server's gunshot is not kept from the pilot, whose client already played its own");
            }

            Assert.That(gunshots, Is.EqualTo(1), "the server played no gunshot to check");
        });
    }

    /// <summary>
    ///     A mech weapon's battery is topped up from the mech's energy as it fires, and the pilot's client predicts that
    ///         too: on a second shot its predicted charge ends where the server's does. Unpredicted, the client took the
    ///         shot's cost from a battery the server had refilled first, and ran dry shots early that the server fired.
    /// </summary>
    [Test]
    public async Task PilotsClientPredictsTheWeaponsTopUp()
    {
        var clientConfigurationManager = Client.ResolveDependency<IConfigurationManager>();
        await Client.WaitPost(() => clientConfigurationManager.SetCVar(CVars.NetPredictTickBias, 4));

        var (mechUid, weaponUid) = await BoardArmedMech();
        var clientWeaponUid = ToClient(weaponUid);
        var clientMechUid = ToClient(mechUid);
        var serverTiming = Server.ResolveDependency<IGameTiming>();

        // The first shot, then long enough for its cooldown and for the client to have the server's result of it.
        await Fire(mechUid, weaponUid);
        await RunSeconds(1.5f);

        var afterOneShot = 0f;
        await Server.WaitPost(() => afterOneShot = SEntMan.System<SharedBatterySystem>().GetCharge(weaponUid));

        await Fire(mechUid, weaponUid);

        // Each tick until the server's result of the second shot is surely in: the client's charge never drops below
        //      what the server leaves, which, the battery having been topped up before the shot, is what it left after the
        //      first.
        var lowestClientCharge = float.MaxValue;
        var ticks = (int)serverTiming.TickRate;
        for (var tick = 0; tick < ticks; tick++)
        {
            await RunTicks(1);
            await Client.WaitPost(() =>
            {
                lowestClientCharge = MathF.Min(lowestClientCharge, CEntMan.System<SharedBatterySystem>().GetCharge(clientWeaponUid));
            });
        }

        var serverCharge = 0f;
        var serverEnergy = 0f;
        await Server.WaitPost(() =>
        {
            serverCharge = SEntMan.System<SharedBatterySystem>().GetCharge(weaponUid);
            serverEnergy = (float)SEntMan.GetComponent<MechComponent>(mechUid).Energy;
        });

        Assert.That(serverCharge, Is.EqualTo(afterOneShot), "the server did not top the weapon up before its second shot");
        Assert.That(lowestClientCharge, Is.EqualTo(serverCharge),
            "the client predicted the second shot from a battery the server had not topped up");

        await Client.WaitAssertion(() =>
        {
            Assert.That((float)CEntMan.GetComponent<MechComponent>(clientMechUid).Energy, Is.EqualTo(serverEnergy),
                "the client's mech energy did not end on the server's");
        });
    }
}
