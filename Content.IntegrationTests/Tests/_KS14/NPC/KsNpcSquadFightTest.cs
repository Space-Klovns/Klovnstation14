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
  - type: NpcSquadMember
    tactics:
      pushing: true
      flanking: true
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
    [TestCase(SyndicateMob, true)]
    public async Task TestNoPushWithoutAMoment(string mob, bool shot)
    {
        var scene = await SetUp(mob, (-6, 0), (1, 6), (1, -6));

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
    ///     One of the squad's grenades going off by the hostile is a moment too.
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
            Assert.That(CountOrders(scene, NpcOrderKind.Push), Is.EqualTo(2));
        });
    }

    /// <summary>
    ///     A squad all fighting a hostile from one side sends one round its side, out of the others' way; one already round it
    ///         sends nobody.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestASquadOnOneSideFlanks(bool oneSide)
    {
        var scene = oneSide
            ? await SetUp(FighterMob, (-6, -1), (-6, 0), (-6, 1))
            : await SetUp(FighterMob, (-6, 0), (1, 6), (1, -6));

        await Pair.Server.WaitAssertion(() =>
        {
            StageVisible(scene);
            scene.TacticsSystem.UpdateNow();

            var flankers = GetOrders(scene, NpcOrderKind.Flank);

            if (!oneSide)
            {
                Assert.That(flankers, Is.Empty, "already round it: nobody needs to go");
                return;
            }

            Assert.That(flankers, Has.Count.EqualTo(1), "one goes round, the rest keep it pinned");
            var (_, order) = flankers[0];
            Assert.That(order.OverCombat);

            var transformSystem = scene.EntManager.System<SharedTransformSystem>();
            var hostilePosition = transformSystem.GetMapCoordinates(scene.HostileUid).Position;
            var offset = transformSystem.ToMapCoordinates(order.Coordinates).Position - hostilePosition;
            var squadBearing = new Angle(MathF.PI); // they are west of it
            var turn = MathF.Abs((float) Angle.ShortestDistance(squadBearing, new Angle(MathF.Atan2(offset.Y, offset.X))).Degrees);

            Assert.That(turn, Is.InRange(55f, 125f), "round its side, not in front of it or behind it");
            Assert.That(offset.Length(), Is.InRange(4f, 6f));
        });
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
    private async Task<Scene> SetUp(string mob, params (int X, int Y)[] memberTiles)
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

            foreach (var (x, y) in memberTiles)
            {
                members.Add(SpawnAt(entManager, mob, gridUid, x, y));
            }
        });

        await Pair.RunTicksSync(240); // for the squad to form

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
