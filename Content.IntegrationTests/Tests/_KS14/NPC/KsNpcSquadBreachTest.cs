#nullable enable
using System;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Squad;
using Content.Server._KS14.NPC.Squad.Tactics;
using Content.Shared.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared._KS14.NPC;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     A squad breaking through to a hostile it cannot otherwise get at, through <see cref="NpcSquadTacticsSystem"/>: a locker
///         nobody can open that the hostile is believed to be in, and the glass between them and one in sight, somewhere too
///         big to be taken for a room.
/// </summary>
/// <remarks>
///     Tactics are paused and driven with <see cref="NpcSquadTacticsSystem.UpdateNow"/>, and contacts are staged. The walls,
///         glass, locker and breaching charge are the real ones: whether a charge goes on something is up to its whitelist.
/// </remarks>
public sealed class KsNpcSquadBreachTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string BreachMob = "KsBreachTestMob";
    private const string Gun = "KsBreachTestGun";

    // Real prototypes, so the test goes by what the game has: the charge's whitelist, the lock.
    private const string Wall = "WallReinforced";
    private const string Window = "ReinforcedWindow";
    private const string Grille = "Grille";
    private const string Charge = "KsWireEndBreachingCharge";
    private const string Activator = "KsWireStart";
    private const string LockedLocker = "LockerSecurity";

    /// <summary>
    ///     Long enough for a search for a path to come back.
    /// </summary>
    private const int ProbeTicks = 30;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsBreachTestMob
  components:
  - type: Hands
  - type: DoAfter
  - type: NpcSquadMember
    tactics:
      watchTime: 0
      planning: true
      planGrace: 0s
      stageTimeout: 0s
      maxStageDistance: 24
      planChargeMinPressure: 0 # a test map has no air in it
      searchBreakLockers: true

- type: entity
  id: KsBreachTestBullet
  components:
  - type: Physics
  - type: Fixtures
    fixtures:
      projectile:
        shape: !type:PhysShapeCircle
          radius: 0.1
        mask:
        - Impassable
        - BulletImpassable

- type: entity
  id: KsBreachTestGun
  components:
  - type: Item
  - type: Gun
  - type: BallisticAmmoProvider
    proto: KsBreachTestBullet
    capacity: 10
";

    /// <summary>
    ///     A hostile seen to get into a locker nobody can open, locked to all of them, is shot open by whoever is sent to it and
    ///         has a clear shot: over combat, as glass is. Not sent to open it to no end. With the setting off, it is sent to
    ///         open it all the same, as before.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestALockedLockerItIsInIsShotOpen(bool breaks)
    {
        var scene = await SetUp(width: 7, height: 6, window: false);

        await Pair.Server.WaitAssertion(() =>
        {
            if (!breaks)
                ConfigureSquad(scene, settings => settings.SearchBreakLockers = false);

            var lockerUid = HideInLocker(scene);
            scene.TacticsSystem.UpdateNow();

            var hunt = scene.TacticsSystem.GetHunt(scene.SquadUid);
            Assert.That(hunt?.Phase, Is.EqualTo(NpcHuntPhase.Search), "a squad already inside should start searching");
            Assert.That(hunt!.SearchPoints.Any(point => point.StorageUid == lockerUid), "the locker it went into should be searched");
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.MemberUid, out var order), "the member nearest the locker should be sent to it");
            Assert.That(order.TargetUid, Is.EqualTo(lockerUid));

            if (breaks)
            {
                Assert.That(order.Kind, Is.EqualTo(NpcOrderKind.BreakBarrier), "it should shoot it open");
                Assert.That(order.OverCombat, "over combat, as glass is shot out");
            }
            else
            {
                Assert.That(order.Kind, Is.EqualTo(NpcOrderKind.Search), "turned off, it is sent to open it");
            }
        });
    }

    /// <summary>
    ///     A locker that is not worth shooting is not shot: one that takes more than <c>searchBreakLockerDamage</c> to
    ///         break, or that NPCs have already put <c>searchBreakLockerRounds</c> into. It is sent to be opened, as before,
    ///         and given up on.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestALockerNotWorthShootingIsNotShot(bool tooSturdy)
    {
        var scene = await SetUp(width: 7, height: 6, window: false);

        await Pair.Server.WaitAssertion(() =>
        {
            // An ordinary locker breaks at 100; none has had a round from them yet.
            if (tooSturdy)
                ConfigureSquad(scene, settings => settings.SearchBreakLockerDamage = 50f);
            else
                ConfigureSquad(scene, settings => settings.SearchBreakLockerRounds = 0);

            var lockerUid = HideInLocker(scene);
            scene.TacticsSystem.UpdateNow();

            Assert.That(scene.TacticsSystem.TryGetOrder(scene.MemberUid, out var order), "the member nearest the locker should be sent to it");
            Assert.That(order.TargetUid, Is.EqualTo(lockerUid));
            Assert.That(order.Kind, Is.EqualTo(NpcOrderKind.Search), "not worth the rounds: sent to open it");
        });
    }

    /// <summary>
    ///     With a breaching charge that sticks to it (the X-4 does), the locker gets that instead of rounds: the search holds,
    ///         and whoever has it goes to put it on from the floor beside it. Should the charge come to nothing, the search
    ///         picks up again and the locker is shot open after all. Not tried again on the same locker. With the setting off,
    ///         the charge is left where it is.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestALockerItIsInGetsACharge(bool charges)
    {
        var scene = await SetUp(width: 7, height: 6, window: false);

        await Pair.Server.WaitAssertion(() =>
        {
            var entManager = scene.EntManager;
            ConfigureSquad(scene, settings => settings.SearchChargeLockers = charges);

            var lockerUid = HideInLocker(scene);
            var chargeUid = GiveInHand(entManager, scene.LeaderUid, Charge);
            GiveInHand(entManager, scene.LeaderUid, Activator);

            scene.TacticsSystem.UpdateNow();

            var hunt = scene.TacticsSystem.GetHunt(scene.SquadUid)!;
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.LeaderUid, out var leaderOrder), "the leader should have an order");

            if (!charges)
            {
                Assert.That(hunt.Phase, Is.EqualTo(NpcHuntPhase.Search), "turned off, it is a search as before");
                Assert.That(leaderOrder.Kind, Is.Not.EqualTo(NpcOrderKind.PlantCharge));
                return;
            }

            Assert.That(hunt.Phase, Is.EqualTo(NpcHuntPhase.Blast), "the search should hold for the charge");
            Assert.That(hunt.BlastLockerUid, Is.EqualTo(lockerUid));
            Assert.That(leaderOrder.Kind, Is.EqualTo(NpcOrderKind.PlantCharge), "the one with the charge should go to put it on");
            Assert.That(leaderOrder.TargetUid, Is.EqualTo(lockerUid), "on the locker");

            var lockerTile = entManager.System<SharedMapSystem>().TileIndicesFor(scene.GridUid, entManager.GetComponent<MapGridComponent>(scene.GridUid),
                entManager.GetComponent<TransformComponent>(lockerUid).Coordinates);
            var standTile = entManager.System<SharedMapSystem>().TileIndicesFor(scene.GridUid, entManager.GetComponent<MapGridComponent>(scene.GridUid),
                leaderOrder.Coordinates);
            Assert.That((standTile - lockerTile).X * (standTile - lockerTile).X + (standTile - lockerTile).Y * (standTile - lockerTile).Y, Is.EqualTo(1),
                "from the floor right beside it");

            // The charge comes to nothing: back to the search, and the locker is shot open after all.
            entManager.DeleteEntity(chargeUid);
            scene.TacticsSystem.UpdateNow();

            Assert.That(hunt.Phase, Is.EqualTo(NpcHuntPhase.Search), "the search should pick up again");
            Assert.That(hunt.BlastLockerUid, Is.Null);
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.MemberUid, out var order) && order.Kind == NpcOrderKind.BreakBarrier && order.TargetUid == lockerUid,
                "the member nearest it should shoot it open");

            // A new charge, and still not tried again on this locker.
            GiveInHand(entManager, scene.LeaderUid, Charge);
            scene.TacticsSystem.UpdateNow();
            Assert.That(hunt.Phase, Is.EqualTo(NpcHuntPhase.Search), "the charge is tried the once per locker");
        });
    }

    /// <summary>
    ///     The same in a room with air in it, at the pressure a charge needs by default, with the locker against its wall:
    ///         as lockers stand. A wall has no air of its own, so going by the far side of the locker, as for a wall to
    ///         blow, found it too thin, and no locker by a wall ever got a charge.
    /// </summary>
    [Test]
    public async Task TestALockerAgainstAWallInAirGetsACharge()
    {
        var scene = await SetUp(width: 7, height: 6, window: false);

        await Pair.Server.WaitPost(() =>
        {
            var entManager = scene.EntManager;
            var mixture = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            mixture.SetMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            mixture.SetMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
            var mapUid = entManager.GetComponent<TransformComponent>(scene.GridUid).MapUid!.Value;
            entManager.System<AtmosphereSystem>().SetMapAtmosphere(mapUid, space: false, mixture);
            entManager.EnsureComponent<GridAtmosphereComponent>(scene.GridUid);
        });

        await Pair.RunTicksSync(5); // the grid's atmosphere sets up its tiles

        // Shut in by its walls, the room does not fill from outside: air goes into every tile of it.
        await Pair.Server.WaitPost(() =>
        {
            var atmosphereSystem = scene.EntManager.System<AtmosphereSystem>();
            for (var x = 0; x < 7; x++)
            {
                for (var y = 0; y < 6; y++)
                {
                    var tileMixture = atmosphereSystem.GetGridTileMixture(scene.GridUid, new Vector2i(x, y), excite: true);
                    tileMixture?.AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
                    tileMixture?.AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
                    if (tileMixture != null)
                        tileMixture.Temperature = Atmospherics.T20C;
                }
            }
        });

        await Pair.RunTicksSync(5);

        await Pair.Server.WaitAssertion(() =>
        {
            var entManager = scene.EntManager;
            var atmosphereSystem = entManager.System<AtmosphereSystem>();
            var defaults = new NpcSquadTacticsSettings();
            ConfigureSquad(scene, settings =>
            {
                settings.SearchChargeLockers = true;
                settings.PlanChargeMinPressure = defaults.PlanChargeMinPressure;
            });

            // Against the east wall, which is at x = 7.
            var lockerUid = HideInLocker(scene, lockerX: 6);
            GiveInHand(entManager, scene.LeaderUid, Charge);
            GiveInHand(entManager, scene.LeaderUid, Activator);

            Assert.That(atmosphereSystem.GetGridTileMixture(scene.GridUid, new Vector2i(5, 3))?.Pressure ?? 0f,
                Is.GreaterThanOrEqualTo(defaults.PlanChargeMinPressure), "the test needs air in front of the locker");
            Assert.That(atmosphereSystem.GetGridTileMixture(scene.GridUid, new Vector2i(7, 3))?.Pressure ?? 0f,
                Is.LessThan(defaults.PlanChargeMinPressure), "and none to speak of in the wall behind it");

            scene.TacticsSystem.UpdateNow();

            var hunt = scene.TacticsSystem.GetHunt(scene.SquadUid)!;
            Assert.That(hunt.BlastLockerUid, Is.EqualTo(lockerUid), "the locker should get the charge, wall or no wall");
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.LeaderUid, out var order) && order.Kind == NpcOrderKind.PlantCharge,
                "the one with it should go to put it on");
        });
    }

    private static void ConfigureSquad(Scene scene, Action<NpcSquadTacticsSettings> configure)
    {
        foreach (var memberUid in new[] { scene.LeaderUid, scene.MemberUid })
        {
            configure(scene.EntManager.GetComponent<NpcSquadMemberComponent>(memberUid).Tactics);
        }
    }

    /// <summary>
    ///     Puts the hostile in a locked locker in the room, the squad in the room with it and armed, and has them believe it
    ///         in there. Returns the locker.
    /// </summary>
    private static EntityUid HideInLocker(Scene scene, int lockerX = 5)
    {
        var entManager = scene.EntManager;
        var transformSystem = entManager.System<SharedTransformSystem>();

        // In the room with it, which they are already inside: they search at once.
        var lockerUid = SpawnAt(entManager, LockedLocker, scene.GridUid, lockerX, 3);
        transformSystem.SetCoordinates(scene.LeaderUid, new EntityCoordinates(scene.GridUid, new Vector2(1.5f, 1.5f)));
        transformSystem.SetCoordinates(scene.MemberUid, new EntityCoordinates(scene.GridUid, new Vector2(3.5f, 3.5f)));
        GiveInHand(entManager, scene.LeaderUid, Gun);
        GiveInHand(entManager, scene.MemberUid, Gun);

        var storageSystem = entManager.System<SharedEntityStorageSystem>();
        Assert.That(storageSystem.Insert(scene.TargetUid, lockerUid), "the test needs the hostile in the locker");
        Assert.That(storageSystem.CanOpen(scene.MemberUid, lockerUid, silent: true), Is.False, "the test needs it locked to them");

        var now = IoCManager.Resolve<IGameTiming>().CurTime;
        var perceptionSystem = entManager.System<NpcPerceptionSystem>();
        var lockerCoordinates = entManager.GetComponent<TransformComponent>(lockerUid).Coordinates;

        foreach (var memberUid in new[] { scene.LeaderUid, scene.MemberUid })
        {
            perceptionSystem.SetContact(memberUid, scene.TargetUid, new NpcContact(NpcContactState.Suspected,
                now, now, now, lockerCoordinates, default, lockerUid, Reacted: true, ReactAt: now));
        }

        return lockerUid;
    }

    /// <summary>
    ///     A hostile in sight down a long corridor, behind reinforced glass, with no way in: somewhere far too big to be taken
    ///         for a room, so there are no room walls to blow. What stands in the way is: the breaching charge does not go on
    ///         glass, so on the wall beside it. They used to do nothing about it at all, whoever had a charge. With the setting
    ///         off, they still do not.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestTheWallBesideTheGlassIsBlown(bool barrier)
    {
        var scene = await SetUp(width: 30, height: 4, window: true);

        await Pair.Server.WaitPost(() =>
        {
            if (!barrier)
            {
                foreach (var memberUid in new[] { scene.LeaderUid, scene.MemberUid })
                {
                    var settings = scene.EntManager.GetComponent<NpcSquadMemberComponent>(memberUid).Tactics;
                    settings.PlanChargeBarrier = false;
                }
            }

            GiveInHand(scene.EntManager, scene.MemberUid, Charge);
            GiveInHand(scene.EntManager, scene.MemberUid, Activator);
        });

        await Step(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            var plan = scene.EntManager.TryGetComponent<NpcSquadPlanComponent>(scene.SquadUid, out var planComponent) ? planComponent.Plan : null;
            Assert.That(plan, Is.Not.Null, "nobody has a shot through the glass, so the squad should plan");

            if (!barrier)
            {
                Assert.That(plan!.State, Is.EqualTo(NpcSquadPlanState.Cooldown), "turned off, there is nothing to be done");
                return;
            }

            Assert.That(plan!.Steps[..plan.StepCount], Is.EqualTo(new[] { NpcSquadAction.BlowWall, NpcSquadAction.Enter }), $"{plan.Reason} {plan.Facts}");

            var site = plan.WallCharge!.Value.Site;
            Assert.That(scene.EntManager.GetComponent<MetaDataComponent>(site.TargetUid).EntityPrototype!.ID, Is.EqualTo(Wall), "the wall, as the charge does not go on glass");
            Assert.That(site.Tile.X, Is.EqualTo(-1), "in the barrier");
            Assert.That(site.Tile.Y, Is.InRange(1, 5), "beside the glass");
            Assert.That(site.StandTile.X, Is.LessThan(-1), "from their side of it");
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.MemberUid, out var order) && order.Kind == NpcOrderKind.PlantCharge,
                "the member with it should go to put it on");
        });
    }

    /// <summary>
    ///     Stages the contact, and updates twice: once to ask for a way to the hostile, and once when the answer is back.
    /// </summary>
    private async Task Step(Scene scene)
    {
        await Pair.Server.WaitPost(() =>
        {
            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();
        });

        await Pair.RunTicksSync(ProbeTicks);

        await Pair.Server.WaitPost(() =>
        {
            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();
        });
    }

    private static void StageVisible(Scene scene)
    {
        var now = IoCManager.Resolve<IGameTiming>().CurTime;
        var perceptionSystem = scene.EntManager.System<NpcPerceptionSystem>();
        var coordinates = scene.EntManager.GetComponent<TransformComponent>(scene.TargetUid).Coordinates;

        foreach (var memberUid in new[] { scene.LeaderUid, scene.MemberUid })
        {
            perceptionSystem.SetContact(memberUid, scene.TargetUid, new NpcContact(NpcContactState.Visible,
                now, now, now, coordinates, default, null, Reacted: true, ReactAt: now));
        }
    }

    private static EntityUid GiveInHand(IEntityManager entManager, EntityUid mobUid, string prototype)
    {
        var handsSystem = entManager.System<SharedHandsSystem>();
        var handId = $"hand{entManager.GetComponent<HandsComponent>(mobUid).Count}";
        handsSystem.AddHand(mobUid, handId, HandLocation.Right);

        var itemUid = entManager.SpawnEntity(prototype, entManager.GetComponent<TransformComponent>(mobUid).Coordinates);
        Assert.That(handsSystem.TryPickup(mobUid, itemUid, handId), "the test needs it holding that");
        return itemUid;
    }

    /// <summary>
    ///     A walled space <paramref name="width"/> by <paramref name="height"/> tiles from the origin, with no way in, the
    ///         hostile in it and a squad of two outside its west wall. With <paramref name="window"/>, that wall has reinforced
    ///         glass on a grille in it, level with the hostile.
    /// </summary>
    private async Task<Scene> SetUp(int width, int height, bool window)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var tacticsSystem = entManager.System<NpcSquadTacticsSystem>();
        var map = await Pair.CreateTestMap();

        EntityUid gridUid = default, firstUid = default, secondUid = default, targetUid = default;

        await server.WaitPost(() =>
        {
            tacticsSystem.UpdatesPaused = true;
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-12, -8), new Vector2i(width + 4, height + 4)).Owner;

            for (var x = -1; x <= width; x++)
            {
                for (var y = -1; y <= height; y++)
                {
                    if (x > -1 && x < width && y > -1 && y < height)
                        continue;

                    if (window && x == -1 && y == 3)
                    {
                        SpawnAt(entManager, Grille, gridUid, x, y);
                        SpawnAt(entManager, Window, gridUid, x, y);
                    }
                    else
                    {
                        SpawnAt(entManager, Wall, gridUid, x, y);
                    }
                }
            }

            firstUid = SpawnAt(entManager, BreachMob, gridUid, -3, 3);
            secondUid = SpawnAt(entManager, BreachMob, gridUid, -3, 2);
            targetUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 3, 3);
        });

        // Long enough for the navmesh to build and the squad to form.
        await Pair.RunTicksSync(150);

        EntityUid squadUid = default, leaderUid = default;

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.System<NpcSquadSystem>().TryGetSquad(firstUid, out var squadEntity), "the two should form a squad");
            squadUid = squadEntity!.Value.Owner;
            leaderUid = squadEntity.Value.Comp.Leader!.Value;
        });

        return new Scene(entManager, tacticsSystem, gridUid, squadUid, leaderUid, leaderUid == firstUid ? secondUid : firstUid, targetUid);
    }

    private sealed record Scene(
        IEntityManager EntManager,
        NpcSquadTacticsSystem TacticsSystem,
        EntityUid GridUid,
        EntityUid SquadUid,
        EntityUid LeaderUid,
        EntityUid MemberUid,
        EntityUid TargetUid);
}
