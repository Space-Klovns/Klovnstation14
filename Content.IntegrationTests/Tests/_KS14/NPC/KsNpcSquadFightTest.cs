#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Squad;
using Content.Server._KS14.NPC.Squad.Tactics;
using Content.Shared._KS14.NPC;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     What a squad does together in a fight (<c>NpcSquadTacticsSystem.Fight.cs</c>): going forward on a hostile when it has
///         a moment, and sending members round the side of one it is all fighting from the front. Tactics are driven by hand,
///         and what each member sees is staged.
/// </summary>
public sealed class KsNpcSquadFightTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string FighterMob = "KsSquadFightTestMob";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsSquadFightTestMob
  components:
  - type: Gun # a gun in hand, as far as anyone asks: only the armed go in
  - type: NpcSquadMember
    tactics:
      pushing: true
      flanking: true

- type: entity
  parent: KsSquadFightTestMob
  id: KsSquadFightTestMobUnarmed
  components:
  - type: NpcSquadMember
    tactics:
      pushing: true
      flanking: true

- type: entity
  parent: KsSquadFightTestMob
  id: KsSquadFightTestMobHesitant
  components:
  - type: NpcSquadMember
    tactics:
      flankChance: 0

- type: entity
  parent: KsSquadFightTestMob
  id: KsSquadFightTestMobNoPush # armed like the rest: only the setting keeps it back
  components:
  - type: NpcSquadMember
    tactics:
      pushing: false

- type: entity
  id: KsSquadFightTestGrenade
  components:
  - type: TimerTrigger
    delay: 1000
  - type: Explosive
    explosionType: Default
    totalIntensity: 10
    intensitySlope: 3
    maxIntensity: 10
";

    private sealed record Scene(IEntityManager EntManager, NpcSquadTacticsSystem TacticsSystem, EntityUid GridUid, List<EntityUid> Members, EntityUid HostileUid);

    /// <summary>
    ///     A hostile that was shooting and has gone quiet: all but one go in close, each from its own side, and the moment it
    ///         shoots again they stop.
    /// </summary>
    [Test]
    public async Task TestAHostileGoneQuietIsPushedOn()
    {
        // Round it, so nobody is on one side and nothing flanks.
        var scene = await SetUp(FighterMob, (-6, 0), (1, 6), (1, -6));

        await Pair.Server.WaitAssertion(() =>
        {
            Shoot(scene);
            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();
            Assert.That(CountOrders(scene, NpcOrderKind.Push), Is.Zero, "still shooting: no moment yet");
        });

        await RunSeconds(1.5f);

        await Pair.Server.WaitAssertion(() =>
        {
            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();

            var pushers = GetOrders(scene, NpcOrderKind.Push);
            Assert.That(pushers, Has.Count.EqualTo(2), "all but the one furthest back should go in");

            var transformSystem = scene.EntManager.System<SharedTransformSystem>();
            var hostilePosition = transformSystem.GetMapCoordinates(scene.HostileUid).Position;
            var bearings = new List<float>();

            foreach (var (_, order) in pushers)
            {
                Assert.That(order.OverCombat, "a push is carried out over fighting");
                Assert.That(order.TargetUid, Is.EqualTo(scene.HostileUid));

                var offset = transformSystem.ToMapCoordinates(order.Coordinates).Position - hostilePosition;
                Assert.That(offset.Length(), Is.InRange(1.5f, 5f), "close by the hostile");
                bearings.Add(MathF.Atan2(offset.Y, offset.X));
            }

            Assert.That(System.Math.Abs(Angle.ShortestDistance(new Angle(bearings[0]), new Angle(bearings[1])).Degrees), Is.GreaterThan(20d),
                "each from its own side");

            Shoot(scene);
            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();
            Assert.That(CountOrders(scene, NpcOrderKind.Push), Is.Zero, "it has reloaded: the push is off");
        });
    }

    /// <summary>
    ///     No moment, no push: a hostile that never fired, or a squad that does not push.
    /// </summary>
    [TestCase(FighterMob, false)]
    [TestCase("KsSquadFightTestMobNoPush", true)]
    public async Task TestNoPushWithoutAMoment(string mob, bool shot)
    {
        var scene = await SetUp(mob, (-6, 0), (1, 6), (1, -6));

        // Armed, or having no gun in hand would be what kept them back (TestNotEveryoneGoes), whatever the squad does.
        await Pair.Server.WaitAssertion(() =>
        {
            var gunSystem = scene.EntManager.System<SharedGunSystem>();
            foreach (var memberUid in scene.Members)
            {
                Assert.That(gunSystem.TryGetGun(memberUid, out _), "the test needs them armed");
            }
        });

        await Pair.Server.WaitPost(() =>
        {
            if (shot)
                Shoot(scene);
        });

        await RunSeconds(1.5f);

        await Pair.Server.WaitAssertion(() =>
        {
            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();
            Assert.That(CountOrders(scene, NpcOrderKind.Push), Is.Zero);
        });
    }

    /// <summary>
    ///     One of the squad's grenades going off by the hostile is a moment too, and one where nobody hangs back: all three go.
    /// </summary>
    [Test]
    public async Task TestABlastOnTheHostileIsPushedOn()
    {
        var scene = await SetUp(FighterMob, (-6, 0), (1, 6), (1, -6));

        await Pair.Server.WaitAssertion(() =>
        {
            var hostileCoordinates = scene.EntManager.GetComponent<TransformComponent>(scene.HostileUid).Coordinates;
            scene.TacticsSystem.NoteBlast(scene.Members[0], hostileCoordinates);
            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();
            Assert.That(CountOrders(scene, NpcOrderKind.Push), Is.EqualTo(3));
        });
    }

    /// <summary>
    ///     A live grenade by them, anyone's: nobody goes forward or round, and a push already on stops.
    /// </summary>
    [Test]
    public async Task TestNobodyGoesInByALiveGrenade()
    {
        var scene = await SetUp(FighterMob, (-6, 0), (1, 6), (1, -6));

        await Pair.Server.WaitAssertion(() =>
        {
            var hostileCoordinates = scene.EntManager.GetComponent<TransformComponent>(scene.HostileUid).Coordinates;
            scene.TacticsSystem.NoteBlast(scene.Members[0], hostileCoordinates);
            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();
            Assert.That(CountOrders(scene, NpcOrderKind.Push), Is.EqualTo(3), "the test needs a push on");

            var grenadeUid = SpawnAt(scene.EntManager, "KsSquadFightTestGrenade", scene.GridUid, -5, 1);
            scene.EntManager.System<Content.Shared.Trigger.Systems.TriggerSystem>().ActivateTimerTrigger(grenadeUid);

            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();
            Assert.That(CountOrders(scene, NpcOrderKind.Push), Is.Zero, "a live grenade by one of them: the push is off");
        });
    }

    /// <summary>
    ///     Only those with a gun in hand go in, not one halfway through changing a magazine; and a squad whose flank chance
    ///         comes up short does not flank.
    /// </summary>
    [TestCase("KsSquadFightTestMobUnarmed", NpcOrderKind.Push)]
    [TestCase("KsSquadFightTestMobHesitant", NpcOrderKind.Flank)]
    public async Task TestNotEveryoneGoes(string mob, NpcOrderKind kind)
    {
        var scene = kind == NpcOrderKind.Flank
            ? await SetUp(mob, (-6, -1), (-6, 0), (-6, 1))
            : await SetUp(mob, (-6, 0), (1, 6), (1, -6));

        await Pair.Server.WaitAssertion(() =>
        {
            if (kind == NpcOrderKind.Push)
            {
                // The unarmed test mob has no gun of its own: its prototype only drops the one it inherits.
                foreach (var memberUid in scene.Members)
                {
                    scene.EntManager.RemoveComponent<Content.Shared.Weapons.Ranged.Components.GunComponent>(memberUid);
                }

                var hostileCoordinates = scene.EntManager.GetComponent<TransformComponent>(scene.HostileUid).Coordinates;
                scene.TacticsSystem.NoteBlast(scene.Members[0], hostileCoordinates);
            }

            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();
            Assert.That(CountOrders(scene, kind), Is.Zero);
        });

        // A flank is only ordered once a way round has been found, some updates on: followed as long as the squad that
        //      does flank is (TestASquadOnOneSideFlanks, the same layout), or nothing has had the time to go wrong.
        if (kind == NpcOrderKind.Flank)
            Assert.That(await FollowFlank(scene), Is.Empty, "its chance came up short: nobody goes round");
    }

    /// <summary>
    ///     A squad all fighting a hostile from one side sends one round its side, out of the others' way, once a way round has
    ///         been found; one already round it sends nobody.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestASquadOnOneSideFlanks(bool oneSide)
    {
        var scene = oneSide
            ? await SetUp(FighterMob, (-6, -1), (-6, 0), (-6, 1))
            : await SetUp(FighterMob, (-6, 0), (1, 6), (1, -6));

        var waypoints = await FollowFlank(scene);

        if (!oneSide)
        {
            Assert.That(waypoints, Is.Empty, "already round it: nobody needs to go");
            return;
        }

        Assert.That(waypoints, Is.Not.Empty, "one goes round, the rest keep it pinned");

        var transformSystem = scene.EntManager.System<SharedTransformSystem>();
        var hostilePosition = transformSystem.GetMapCoordinates(scene.HostileUid).Position;
        var offset = waypoints[^1] - hostilePosition;
        var turn = System.Math.Abs(Angle.ShortestDistance(new Angle(MathF.PI), offset.ToAngle()).Degrees); // they are west of it

        Assert.That(turn, Is.InRange(55d, 180d), "round its side or behind it, not in front of it");
        Assert.That(offset.Length(), Is.InRange(3f, 8f));
    }

    /// <summary>
    ///     A corridor with a maintenance passage alongside it, joined behind the squad and by a side corridor that comes out
    ///         beside the hostile: the way round is through maintenance and down the side corridor, out of its sight, not along
    ///         the corridor past it. Behind it in the corridor is no flank at all: the squad's misses fly on down there.
    /// </summary>
    [Test]
    public async Task TestAFlankGoesThroughMaintenance()
    {
        // Corridor along y = 0; passage along y = 4, joined at x = -4; a side corridor at x = 1 from the corridor up to y = 5.
        var scene = await SetUp(FighterMob,
            (-6, 0), (-5, 0), (-4, 0),
            walls: (x, y) => !(y == 0 && x is >= -10 and <= 10 ||
                y == 4 && x is >= -4 and <= 6 ||
                x == -4 && y is >= 1 and <= 3 ||
                x == 1 && y is >= 1 and <= 5));

        var waypoints = await FollowFlank(scene);
        Assert.That(waypoints, Is.Not.Empty, "the test needs a flank");

        var transformSystem = scene.EntManager.System<SharedTransformSystem>();
        var hostilePosition = transformSystem.GetMapCoordinates(scene.HostileUid).Position;
        var offset = waypoints[^1] - hostilePosition;

        Assert.Multiple(() =>
        {
            Assert.That(waypoints.Any(waypoint => waypoint.Y > 3.5f && waypoint.X < 0f), "round through the passage: " + string.Join(" ", waypoints));
            Assert.That(System.Math.Abs(Angle.ShortestDistance(new Angle(MathF.PI), offset.ToAngle()).Degrees), Is.GreaterThan(55d),
                "to its side");
            Assert.That(waypoints.Any(waypoint => waypoint.Y < 1f && waypoint.X > -3f), Is.False, "never along the corridor towards it");
        });
    }

    /// <summary>
    ///     Runs the squad until a flank is ordered, then walks the flanker along it waypoint by waypoint, by teleporting it to
    ///         each, and returns where it was sent, in order: empty for no flank.
    /// </summary>
    private async Task<List<Vector2>> FollowFlank(Scene scene)
    {
        var waypoints = new List<Vector2>();
        var transformSystem = scene.EntManager.System<SharedTransformSystem>();
        EntityUid? flankerUid = null;

        for (var i = 0; i < 40; i++)
        {
            await Pair.Server.WaitPost(() =>
            {
                StageVisible(scene);
                scene.TacticsSystem.UpdateNow();

                var flankers = GetOrders(scene, NpcOrderKind.Flank);
                if (flankers.Count == 0)
                    return;

                var (uid, order) = flankers[0];
                Assert.That(order.OverCombat);
                Assert.That(order.TargetUid, Is.EqualTo(scene.HostileUid));
                flankerUid ??= uid;

                var position = transformSystem.ToMapCoordinates(order.Coordinates).Position;
                if (waypoints.Count == 0 || (waypoints[^1] - position).Length() > 0.1f)
                    waypoints.Add(position);

                transformSystem.SetCoordinates(uid, order.Coordinates);
            });

            await Pair.RunTicksSync(5);
        }

        return waypoints;
    }

    private void Shoot(Scene scene)
    {
        var coordinates = scene.EntManager.GetComponent<TransformComponent>(scene.HostileUid).Coordinates;
        var ev = new GunShotEvent(scene.HostileUid, new List<(EntityUid? Uid, IShootable Shootable)>(), coordinates, coordinates);
        scene.EntManager.EventBus.RaiseEvent(EventSource.Local, ref ev);
    }

    private static void StageVisible(Scene scene)
    {
        var now = Robust.Shared.IoC.IoCManager.Resolve<IGameTiming>().CurTime;
        var perceptionSystem = scene.EntManager.System<NpcPerceptionSystem>();
        var coordinates = scene.EntManager.GetComponent<TransformComponent>(scene.HostileUid).Coordinates;

        foreach (var memberUid in scene.Members)
        {
            perceptionSystem.SetContact(memberUid, scene.HostileUid, new NpcContact(NpcContactState.Visible,
                now, now, now, coordinates, default, null, Reacted: true, ReactAt: now));
        }
    }

    private static List<(EntityUid Uid, NpcOrder Order)> GetOrders(Scene scene, NpcOrderKind kind)
    {
        var orders = new List<(EntityUid, NpcOrder)>();
        foreach (var memberUid in scene.Members)
        {
            if (scene.TacticsSystem.TryGetOrder(memberUid, out var order) && order.Kind == kind)
                orders.Add((memberUid, order));
        }

        return orders;
    }

    private static int CountOrders(Scene scene, NpcOrderKind kind)
    {
        return GetOrders(scene, kind).Count;
    }

    /// <summary>
    ///     A squad of <paramref name="mob"/> at <paramref name="memberTiles"/> and a hostile at 1,0 in the open, on a grid
    ///         from -12,-8 to 12,8, with the squad formed and tactics paused.
    /// </summary>
    private Task<Scene> SetUp(string mob, params (int X, int Y)[] memberTiles)
    {
        return SetUp(mob, memberTiles, walls: null);
    }

    private Task<Scene> SetUp(string mob, (int X, int Y) first, (int X, int Y) second, (int X, int Y) third, System.Func<int, int, bool> walls)
    {
        return SetUp(mob, [first, second, third], walls);
    }

    /// <param name="walls">Which tiles of the grid are walls, if any.</param>
    private async Task<Scene> SetUp(string mob, (int X, int Y)[] memberTiles, System.Func<int, int, bool>? walls)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var tacticsSystem = entManager.System<NpcSquadTacticsSystem>();
        var map = await Pair.CreateTestMap();
        var members = new List<EntityUid>();
        EntityUid gridUid = default, hostileUid = default;

        await server.WaitPost(() =>
        {
            tacticsSystem.UpdatesPaused = true;
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-12, -8), new Vector2i(12, 8)).Owner;

            if (walls != null)
            {
                for (var x = -12; x <= 12; x++)
                {
                    for (var y = -8; y <= 8; y++)
                    {
                        if (walls(x, y))
                            SpawnAt(entManager, "WallSolid", gridUid, x, y);
                    }
                }
            }

            foreach (var (x, y) in memberTiles)
            {
                members.Add(SpawnAt(entManager, mob, gridUid, x, y));
            }
        });

        await Pair.RunTicksSync(240); // for the squad to form

        // Every wall has to be in the navmesh before a way round is looked for: one missing is a way through.
        await WaitForNavmeshSettled(Pair, gridUid);

        await server.WaitAssertion(() =>
        {
            var squadSystem = entManager.System<NpcSquadSystem>();
            Assert.That(members.Select(memberUid => squadSystem.GetSquadOrSelf(memberUid)).Distinct().Count(), Is.EqualTo(1),
                "the test needs them in one squad");
            hostileUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 1, 0);
        });

        return new Scene(entManager, tacticsSystem, gridUid, members, hostileUid);
    }
}
