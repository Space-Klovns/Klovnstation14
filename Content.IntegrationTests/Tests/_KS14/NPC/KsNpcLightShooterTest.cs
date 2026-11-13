#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Lights;
using Content.Server._KS14.NPC.Perception;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Shared._KS14.NPC;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     Shooting out lights (<c>NpcLightShooterSystem</c>): a shooter that opts in puts out the light on a hostile who cannot
///         see in the dark, and the lit lights of the room it stages at, by an ordinary shot at the light; never when it has a
///         shot at the hostile, nor with a friend in the lane; and one that did not opt in does nothing.
/// </summary>
public sealed class KsNpcLightShooterTest : GameTest
{
    public override PoolSettings PoolSettings => new() { ServerLightTree = true };

    private const string Shooter = "KsLightShooterTestShooter";
    private const string Plain = "KsLightShooterTestPlain";
    private const string Hostile = "KsLightShooterTestHostile";
    private const string Light = "KsLightShooterTestLight";
    private const string Goggles = "KsLightShooterTestGoggles";
    private const string Pistol = "KsLightShooterTestGun";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsLightShooterTestPlain
  components:
  - type: Hands
  - type: CombatMode

- type: entity
  parent: KsLightShooterTestPlain
  id: KsLightShooterTestShooter
  components:
  - type: NpcLightShooter

- type: entity
  parent: KsSquadTestMobNanoTrasen
  id: KsLightShooterTestHostile
  components:
  - type: Inventory
    templateId: human
  - type: ContainerContainer

- type: entity
  parent: ClothingEyesBase
  id: KsLightShooterTestGoggles
  components:
  - type: NpcDarkVision

# A gun with real rounds in it: what a round does to a light is the game's own.
- type: entity
  id: KsLightShooterTestGun
  components:
  - type: Item
  - type: Gun
    fireRate: 4
  - type: BallisticAmmoProvider
    proto: CartridgePistol
    capacity: 10

- type: entity
  parent: Poweredlight
  id: KsLightShooterTestLight
  components:
  - type: ApcPowerReceiver
    needsPower: false
";

    private sealed record Scene(
        IEntityManager EntManager,
        EntityUid GridUid,
        EntityUid ShooterUid,
        EntityUid GunUid,
        EntityUid HostileUid,
        EntityUid LightUid);

    /// <summary>
    ///     The whole of it, live: the shooter has lost a hostile that stands by a lit light, picks the light, and the shot it
    ///         takes with the pistol in its hand puts the light out.
    /// </summary>
    [Test]
    public async Task TestShootsOutTheLightByTheHostile()
    {
        var scene = await SetUp(Shooter);
        var entManager = scene.EntManager;
        var shooterSystem = entManager.System<NpcLightShooterSystem>();

        await LoseHostile(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            shooterSystem.UpdateNow(scene.ShooterUid);

            Assert.That(shooterSystem.TryGetPick(scene.ShooterUid, out var pick), "it should have picked a light: " + Why(scene));
            Assert.That(pick, Is.EqualTo(scene.LightUid), "the one by the hostile");
            Assert.That(entManager.GetComponent<NpcLightShooterComponent>(scene.ShooterUid).Reason, Is.EqualTo(NpcLightShotReason.Fight));

            // What the operator does, and everything after it is ordinary ranged combat.
            entManager.EnsureComponent<NPCRangedCombatComponent>(scene.ShooterUid).Target = scene.LightUid;
        });

        var ammoAtFirst = AmmoCount(entManager, scene.GunUid);
        await RunUntilOut(scene, 300);

        await Pair.Server.WaitAssertion(() =>
        {
            var status = entManager.TryGetComponent<NPCRangedCombatComponent>(scene.ShooterUid, out var ranged)
                ? $"{ranged.Status}, in line {ranged.TargetInLOS}, acc {ranged.ShootAccumulator}/{ranged.ShootDelay}, rot {entManager.System<SharedTransformSystem>().GetWorldRotation(scene.ShooterUid).Degrees}, "
                  + $"clear {entManager.System<Content.Server._KS14.NPC.Squad.NpcSquadFireLaneSystem>().TryGetClearShot(scene.ShooterUid, entManager.System<SharedTransformSystem>().GetMapCoordinates(scene.ShooterUid), new MapCoordinates(shooterSystem.GetAimPoint(scene.LightUid), entManager.System<SharedTransformSystem>().GetMapCoordinates(scene.ShooterUid).MapId), 0.25f, out _)}, "
                  + $"aim {shooterSystem.GetAimPoint(scene.LightUid)} light {entManager.System<SharedTransformSystem>().GetWorldPosition(scene.LightUid)}, canshoot {entManager.System<Content.Server.Weapons.Ranged.Systems.GunSystem>().TryGetGun(scene.ShooterUid, out var g) && entManager.System<Content.Server.Weapons.Ranged.Systems.GunSystem>().CanShoot(g)}"
                : "no ranged combat";
            Assert.That(AmmoCount(entManager, scene.GunUid), Is.LessThan(ammoAtFirst), $"it should have shot ({status})");
            Assert.That(entManager.GetComponent<PoweredLightComponent>(scene.LightUid).CurrentLit, Is.False, "the light should be out");
            Assert.That(entManager.System<NpcLightShooterSystem>().TryGetPick(scene.ShooterUid, out _), Is.False, "and the pick is over with it");
        });
    }

    /// <summary>
    ///     What it leaves alone: a hostile that sees in the dark, one it has a shot at, and one it has not lost. Nothing is picked
    ///         for any of them, and the light stays on.
    /// </summary>
    [TestCase("hostile with goggles")]
    [TestCase("hostile in sight and in the line of fire")]
    [TestCase("no hostile")]
    [TestCase("out of range")]
    [TestCase("friend in the way")]
    public async Task TestLeavesTheLightAlone(string how)
    {
        var scene = await SetUp(Shooter, lightY: how == "out of range" ? 7 : 2);
        var entManager = scene.EntManager;
        var shooterSystem = entManager.System<NpcLightShooterSystem>();

        await Pair.Server.WaitPost(() =>
        {
            var hostileCoordinates = entManager.GetComponent<TransformComponent>(scene.HostileUid).Coordinates;

            switch (how)
            {
                case "hostile with goggles":
                    Assert.That(entManager.System<InventorySystem>().TryEquip(scene.HostileUid,
                        entManager.SpawnEntity(Goggles, hostileCoordinates), "eyes", silent: true, force: true));
                    SetContact(entManager, scene, NpcContactState.Lost);
                    break;
                case "hostile in sight and in the line of fire":
                    // Right in front of it: the shot it has is at the hostile.
                    entManager.System<SharedTransformSystem>().SetCoordinates(scene.HostileUid,
                        entManager.GetComponent<TransformComponent>(scene.ShooterUid).Coordinates.Offset(new Vector2(0f, 3f)));
                    SetContact(entManager, scene, NpcContactState.Visible);
                    break;
                case "friend in the way":
                    SpawnAt(entManager, Plain, scene.GridUid, 0, -3);
                    SetContact(entManager, scene, NpcContactState.Lost);
                    break;
                case "no hostile":
                    break;
                default:
                    SetContact(entManager, scene, NpcContactState.Lost);
                    break;
            }
        });

        await Pair.RunTicksSync(2);

        await Pair.Server.WaitAssertion(() =>
        {
            shooterSystem.UpdateNow(scene.ShooterUid);
            Assert.That(shooterSystem.TryGetPick(scene.ShooterUid, out _), Is.False, "nothing should be picked");
        });

        // And the shot is not taken at a light with a friend in its lane, even when it is told to: it goes through the fire lane
        //   check like every other.
        if (how != "friend in the way")
            return;

        var ammoAtFirst = AmmoCount(entManager, scene.GunUid);
        await Pair.Server.WaitPost(() => entManager.EnsureComponent<NPCRangedCombatComponent>(scene.ShooterUid).Target = scene.LightUid);
        await Pair.RunTicksSync(120);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(AmmoCount(entManager, scene.GunUid), Is.EqualTo(ammoAtFirst), "it should hold its fire");
            Assert.That(entManager.GetComponent<PoweredLightComponent>(scene.LightUid).CurrentLit, "so the light stays on");
        });
    }

    /// <summary>
    ///     A shooter that did not opt in does nothing, for the same scene as the one that does.
    /// </summary>
    [Test]
    public async Task TestOrdinaryOperativeDoesNotShootLights()
    {
        var scene = await SetUp(Plain);
        var entManager = scene.EntManager;
        var shooterSystem = entManager.System<NpcLightShooterSystem>();

        await LoseHostile(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            shooterSystem.UpdateNow(scene.ShooterUid);

            Assert.That(entManager.HasComponent<NpcLightShooterComponent>(scene.ShooterUid), Is.False);
            Assert.That(shooterSystem.TryGetPick(scene.ShooterUid, out _), Is.False);
        });
    }

    /// <summary>
    ///     One light at a time and a pause between: after one goes out the next waits for its cooldown, and one that
    ///         is not shot out in time is given up on for longer.
    /// </summary>
    [Test]
    public async Task TestOneLightAtATimeWithACooldown()
    {
        var scene = await SetUp(Shooter);
        var entManager = scene.EntManager;
        var shooterSystem = entManager.System<NpcLightShooterSystem>();
        EntityUid secondLightUid = default;
        var first0 = EntityUid.Invalid;

        await LoseHostile(scene);

        await Pair.Server.WaitPost(() =>
        {
            secondLightUid = SpawnAt(entManager, Light, scene.GridUid, 1, 2);
            entManager.System<SharedPowerReceiverSystem>().SetNeedsPower(secondLightUid, false);
        });

        await Pair.RunTicksSync(5);

        await Pair.Server.WaitAssertion(() =>
        {
            SetContact(entManager, scene, NpcContactState.Lost);
            shooterSystem.UpdateNow(scene.ShooterUid);
            Assert.That(shooterSystem.TryGetPick(scene.ShooterUid, out var first),
                $"the first (lit: {entManager.GetComponent<PoweredLightComponent>(scene.LightUid).CurrentLit}, {entManager.GetComponent<PoweredLightComponent>(secondLightUid).CurrentLit}; "
                + $"target {entManager.GetComponent<NpcLightShooterComponent>(scene.ShooterUid).Target})");

            first0 = first;
            // Put out by something else: the pick is over, and the next is not at once.
            Assert.That(entManager.System<SharedPoweredLightSystem>().TryDestroyBulb(first));
            SetContact(entManager, scene, NpcContactState.Lost);
            shooterSystem.UpdateNow(scene.ShooterUid);
            SetContact(entManager, scene, NpcContactState.Lost);
            shooterSystem.UpdateNow(scene.ShooterUid);
            Assert.That(shooterSystem.TryGetPick(scene.ShooterUid, out _), Is.False, "it waits");
        });

        // The success cooldown is under a second: then the second.
        await Pair.RunTicksSync(30);

        await Pair.Server.WaitAssertion(() =>
        {
            SetContact(entManager, scene, NpcContactState.Lost);
            shooterSystem.UpdateNow(scene.ShooterUid);
            Assert.That(shooterSystem.TryGetPick(scene.ShooterUid, out var second), "then the next: " + Why(scene) + $" second lit {entManager.GetComponent<PoweredLightComponent>(secondLightUid).CurrentLit}, first {first0}, comp {System.Text.Json.JsonSerializer.Serialize(new { t = entManager.GetComponent<NpcLightShooterComponent>(scene.ShooterUid).Target?.ToString(), now = Pair.Server.ResolveDependency<IGameTiming>().CurTime.ToString() })}");
            Assert.That(second, Is.Not.EqualTo(first0), "not the one that is out");
        });

        // Not got to within its time: given up, and the longer pause.
        await Pair.RunTicksSync(90);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(shooterSystem.TryGetPick(scene.ShooterUid, out _), Is.False, "out of time");
            SetContact(entManager, scene, NpcContactState.Lost);
            shooterSystem.UpdateNow(scene.ShooterUid);
            SetContact(entManager, scene, NpcContactState.Lost);
            shooterSystem.UpdateNow(scene.ShooterUid);
            Assert.That(shooterSystem.TryGetPick(scene.ShooterUid, out _), Is.False, "and in no hurry to try again");
        });
    }

    /// <summary>
    ///     Staging: only a light on a tile of the room, which it has a shot at from where it stands, is picked.
    /// </summary>
    [Test]
    public async Task TestStagingPicksTheRoomsLights()
    {
        var scene = await SetUp(Shooter);
        var entManager = scene.EntManager;
        var shooterSystem = entManager.System<NpcLightShooterSystem>();

        await Pair.Server.WaitAssertion(() =>
        {
            var gridComponent = entManager.GetComponent<MapGridComponent>(scene.GridUid);
            var mapSystem = entManager.System<SharedMapSystem>();
            var lightTile = mapSystem.TileIndicesFor(scene.GridUid, gridComponent, entManager.GetComponent<TransformComponent>(scene.LightUid).Coordinates);
            var from = entManager.System<SharedTransformSystem>().GetMapCoordinates(scene.ShooterUid);
            var entity = new Entity<NpcLightShooterComponent>(scene.ShooterUid, entManager.GetComponent<NpcLightShooterComponent>(scene.ShooterUid));

            Assert.That(shooterSystem.TryPickInRoom(entity, from, scene.GridUid, new HashSet<Vector2i> { lightTile }, out var pick), "a light of the room");
            Assert.That(pick, Is.EqualTo(scene.LightUid));

            Assert.That(shooterSystem.TryPickInRoom(entity, from, scene.GridUid, new HashSet<Vector2i> { lightTile + new Vector2i(5, 0) }, out _), Is.False,
                "not one of another room");
        });
    }

    private string Why(Scene scene)
    {
        var e = scene.EntManager;
        var gun = e.System<Content.Server.Weapons.Ranged.Systems.GunSystem>();
        var perception = e.GetComponent<NpcPerceptionComponent>(scene.ShooterUid);
        var states = string.Join(",", perception.Contacts.Select(c => $"{c.Key}:{c.Value.State}"));
        return $"hostile {scene.HostileUid}; contacts [{states}]; gun {gun.TryGetGun(scene.ShooterUid, out var g)}; ammo {AmmoCount(e, scene.GunUid)}; "
            + $"alive {e.System<Content.Shared.Mobs.Systems.MobStateSystem>().IsAlive(scene.ShooterUid)}; lit {e.GetComponent<PoweredLightComponent>(scene.LightUid).CurrentLit}; "
            + $"point {e.GetComponent<Robust.Server.GameObjects.PointLightComponent>(scene.LightUid).Enabled}; sight {e.System<Content.Server._KS14.NPC.LineOfFire.NpcLineOfFireSystem>().HasLineOfFire(scene.ShooterUid, scene.LightUid, 20f)}; "
            + $"hostile dark {perceptionHostile(e, scene)}";
    }

    private static float perceptionHostile(IEntityManager e, Scene scene) => e.GetComponent<NpcPerceptionComponent>(scene.HostileUid).DarkVision;

    private void SetContact(IEntityManager entManager, Scene scene, NpcContactState state)
    {
        var now = Pair.Server.ResolveDependency<IGameTiming>().CurTime;
        var coordinates = entManager.GetComponent<TransformComponent>(scene.HostileUid).Coordinates;
        entManager.System<NpcPerceptionSystem>().SetContact(scene.ShooterUid, scene.HostileUid,
            new NpcContact(state, now, now, now, coordinates, Vector2.Zero, null, true));
    }

    private async Task LoseHostile(Scene scene)
    {
        await Pair.Server.WaitPost(() => SetContact(scene.EntManager, scene, NpcContactState.Lost));
        await Pair.RunTicksSync(2);
    }

    private async Task RunUntilOut(Scene scene, int maxTicks)
    {
        var lit = true;
        for (var i = 0; i < maxTicks && lit; i++)
        {
            await Pair.RunTicksSync(1);
            await Pair.Server.WaitPost(() => lit = scene.EntManager.GetComponent<PoweredLightComponent>(scene.LightUid).CurrentLit);
        }
    }

    private static int AmmoCount(IEntityManager entManager, EntityUid gunUid)
    {
        var ev = new GetAmmoCountEvent();
        entManager.EventBus.RaiseLocalEvent(gunUid, ref ev);
        return ev.Count;
    }

    /// <summary>
    ///     A shooter with a pistol, five tiles south of a lit light at the middle of the grid, and a hostile standing by it.
    /// </summary>
    private async Task<Scene> SetUp(string shooter, int lightY = 2, string gun = Pistol)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        Scene scene = default!;

        await server.WaitPost(() =>
        {
            var gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-10, -8), new Vector2i(10, 10)).Owner;

            var lightUid = SpawnAt(entManager, Light, gridUid, 0, lightY);
            entManager.System<SharedPowerReceiverSystem>().SetNeedsPower(lightUid, false);

            var hostileUid = SpawnAt(entManager, Hostile, gridUid, 3, lightY);
            var shooterUid = SpawnAt(entManager, shooter, gridUid, 0, -5);

            var handsSystem = entManager.System<SharedHandsSystem>();
            handsSystem.AddHand(shooterUid, "right", HandLocation.Right);
            var gunUid = entManager.SpawnEntity(gun, entManager.GetComponent<TransformComponent>(shooterUid).Coordinates);
            Assert.That(handsSystem.TryPickup(shooterUid, gunUid, "right"));

            // What it believes is the test's to say: its own look, every 0.2s from a random start, would see the hostile three
            //      tiles off in plain view and undo a staged loss, sometimes before the pick and sometimes after.
            entManager.System<NpcPerceptionSystem>().HoldUpdates(shooterUid);

            scene = new Scene(entManager, gridUid, shooterUid, gunUid, hostileUid, lightUid);
        });

        // Powered within a tick or two.
        await Pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
            Assert.That(entManager.GetComponent<PoweredLightComponent>(scene.LightUid).CurrentLit, "the test light should be lit"));

        return scene;
    }
}
