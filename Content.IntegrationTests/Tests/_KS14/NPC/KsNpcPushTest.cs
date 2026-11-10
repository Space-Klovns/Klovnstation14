#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Pushing;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     NPCs pushing loose things out of their way (<see cref="NpcPushSystem"/>): a closet in the only doorway.
/// </summary>
public sealed class KsNpcPushTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string ShoverMob = "KsPushTestMobShover";
    private const string HandsOffMob = "KsPushTestMobHandsOff";
    private const string FeebleMob = "KsPushTestMobFeeble";
    private const string Closet = "ClosetSteelBase";

    private const int WallY = 3;
    private const int DoorwayX = 3;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsPushTestMobHandsOff
  components:
  - type: InputMover
  - type: MobMover
  - type: MovementSpeedModifier
  - type: NpcPusher
    forgetAfter: 120s # outlasts the walk, so it can be checked at the end

- type: entity
  parent: KsPushTestMobHandsOff
  id: KsPushTestMobShover
  components:
  - type: CombatMode
    canDisarm: true
    disarmFailChance: 0
  - type: MeleeWeapon
    attackRate: 1
    damage:
      types:
        Blunt: 1

- type: entity
  parent: KsPushTestMobShover
  id: KsPushTestMobFeeble
  components:
  - type: MeleeWeapon
    pushForce: 0 # its shoves land, and move nothing
    attackRate: 1
    damage:
      types:
        Blunt: 1
  - type: NpcPusher
    giveUpAfter: 2s
    forgetAfter: 120s # outlasts the walk, so it can be checked at the end
";

    /// <summary>
    ///     The only way into the room above is a doorway with a closet in it. An NPC that may push things out of its
    ///         way shoves the closet into the room and walks in; one that may not has no way in, and stays outside.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestClosetInTheDoorwayIsPushedIn(bool navPush)
    {
        var (gridUid, walkerUid, closetUid) = await WalkThroughDoorway(ShoverMob, navPush, otherWayX: null);
        var target = new Vector2(6.5f, 7.5f);

        await Server.WaitAssertion(() =>
        {
            var walkerPosition = SEntMan.GetComponent<TransformComponent>(walkerUid).LocalPosition;
            var closetPosition = SEntMan.GetComponent<TransformComponent>(closetUid).LocalPosition;

            if (navPush)
            {
                Assert.That((walkerPosition - target).Length(), Is.LessThan(1f), $"the NPC should have got in, but is at {walkerPosition}");
                Assert.That(closetPosition.Y, Is.GreaterThan(WallY + 1f), $"the closet should have been shoved into the room, but is at {closetPosition}");
            }
            else
            {
                Assert.That(walkerPosition.Y, Is.LessThan(WallY), $"the NPC should have stayed outside, but is at {walkerPosition}");
                Assert.That((closetPosition - new Vector2(DoorwayX + 0.5f, WallY + 0.5f)).Length(), Is.LessThan(0.1f), "nothing should have moved the closet");
            }
        });
    }

    /// <summary>
    ///     An NPC whose paths may push things, but which cannot shove - no combat mode - goes the long way round, through
    ///         a second doorway far off, rather than standing at the closet. It leaves the closet where it was, and
    ///         remembers it as something it cannot push.
    /// </summary>
    [Test]
    public async Task TestWhatCannotBeShovedIsGoneRound()
    {
        var (gridUid, walkerUid, closetUid) = await WalkThroughDoorway(HandsOffMob, navPush: true, otherWayX: 13);
        var target = new Vector2(6.5f, 7.5f);

        await Server.WaitAssertion(() =>
        {
            var walkerPosition = SEntMan.GetComponent<TransformComponent>(walkerUid).LocalPosition;
            var closetPosition = SEntMan.GetComponent<TransformComponent>(closetUid).LocalPosition;

            Assert.That((walkerPosition - target).Length(), Is.LessThan(1f), $"the NPC should have gone round, but is at {walkerPosition}");
            Assert.That((closetPosition - new Vector2(DoorwayX + 0.5f, WallY + 0.5f)).Length(), Is.LessThan(0.1f), "nothing should have moved the closet");

            var unpushableUids = new List<EntityUid>();
            SEntMan.System<NpcPushSystem>().GetUnpushable(walkerUid, unpushableUids);
            Assert.That(unpushableUids, Does.Contain(closetUid));
        });
    }

    /// <summary>
    ///     An NPC whose shoves get the closet nowhere - here, by having no strength in them; on a station, a closet
    ///         wedged against a wall - does not stand at it for ever: after a while it gives up on it, and goes the long
    ///         way round.
    /// </summary>
    [Test]
    public async Task TestPushThatGetsNowhereIsGivenUp()
    {
        var (_, walkerUid, closetUid) = await WalkThroughDoorway(FeebleMob, navPush: true, otherWayX: 13);
        var target = new Vector2(6.5f, 7.5f);
        var walkerPosition = Vector2.Zero;
        var unpushableUids = new List<EntityUid>();

        await Server.WaitPost(() =>
        {
            walkerPosition = SEntMan.GetComponent<TransformComponent>(walkerUid).LocalPosition;
            SEntMan.System<NpcPushSystem>().GetUnpushable(walkerUid, unpushableUids);
        });

        Assert.That((walkerPosition - target).Length(), Is.LessThan(1f), $"the NPC should have given up and gone round, but is at {walkerPosition}");
        Assert.That(unpushableUids, Does.Contain(closetUid), "and remember the closet as something it cannot push");
    }

    /// <summary>
    ///     A maintenance corridor one tile wide and seven long, barricaded with two closets, is the only way through. An
    ///         NPC that pushes gets through, shoving the closets ahead of it and out into the room at the far end - each
    ///         shove puts one onto a tile ahead that was free when the path was made - and gets there in good time.
    /// </summary>
    [Test]
    public async Task TestBarricadedCorridorIsPushedThrough()
    {
        var tileDefinitionManager = Server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default, walkerUid = default;
        var target = new Vector2(6.5f, 12.5f);

        await Server.WaitPost(() =>
        {
            gridUid = MakeGrid(SEntMan, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(0, 0), new Vector2i(8, 14)).Owner;

            for (var y = 3; y <= 9; y++)
            {
                for (var x = 0; x <= 8; x++)
                {
                    if (x != DoorwayX)
                        SpawnAt(SEntMan, "WallSolid", gridUid, x, y);
                }
            }

            // And round the room at the end, so nothing is shoved off the grid.
            for (var x = 0; x <= 8; x++)
            {
                SpawnAt(SEntMan, "WallSolid", gridUid, x, 14);
            }

            SpawnAt(SEntMan, Closet, gridUid, DoorwayX, 4);
            SpawnAt(SEntMan, Closet, gridUid, DoorwayX, 6);
            walkerUid = SpawnAt(SEntMan, ShoverMob, gridUid, DoorwayX, 0);
        });

        await Pair.RunTicksSync(90); // navmesh

        await Server.WaitPost(() =>
        {
            var htnComponent = SEntMan.GetComponent<HTNComponent>(walkerUid);
            htnComponent.Blackboard.SetValue(NPCBlackboard.NavPush, true);
            SEntMan.System<NPCSteeringSystem>().Register(walkerUid, new EntityCoordinates(gridUid, target));
            SEntMan.System<NPCSystem>().WakeNPC(walkerUid, htnComponent);
        });

        // A few seconds, pushing as it goes. Taking the closets for free floor - walking into one, waiting to be found
        //      stuck, asking for a path again - it was not through in a minute.
        var arrivedAfter = -1;
        for (var second = 1; second <= 20 && arrivedAfter < 0; second++)
        {
            await Pair.RunTicksSync(30);
            await Server.WaitPost(() =>
            {
                if ((SEntMan.GetComponent<TransformComponent>(walkerUid).LocalPosition - target).Length() < 1f)
                    arrivedAfter = second;
            });
        }

        Assert.That(arrivedAfter, Is.GreaterThan(0), "the NPC should have pushed its way through the corridor within 20 seconds");
    }

    /// <summary>
    ///     A wall along y = <see cref="WallY"/>, open at <see cref="DoorwayX"/>, where a closet stands, and at
    ///         <paramref name="otherWayX"/> if given. An NPC walks from below the closet to the far corner of the room
    ///         above, for 30 seconds.
    /// </summary>
    private async Task<(EntityUid GridUid, EntityUid WalkerUid, EntityUid ClosetUid)> WalkThroughDoorway(string walkerMob,
        bool navPush,
        int? otherWayX)
    {
        var tileDefinitionManager = Server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        var width = otherWayX is { } x ? x + 1 : 8;
        EntityUid gridUid = default, walkerUid = default, closetUid = default;

        await Server.WaitPost(() =>
        {
            gridUid = MakeGrid(SEntMan, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(0, 0), new Vector2i(width, 8)).Owner;

            for (var wallX = 0; wallX <= width; wallX++)
            {
                if (wallX != DoorwayX && wallX != otherWayX)
                    SpawnAt(SEntMan, "WallSolid", gridUid, wallX, WallY);
            }

            closetUid = SpawnAt(SEntMan, Closet, gridUid, DoorwayX, WallY);
            walkerUid = SpawnAt(SEntMan, walkerMob, gridUid, DoorwayX, 0);
        });

        await Pair.RunTicksSync(90); // navmesh

        await Server.WaitPost(() =>
        {
            var htnComponent = SEntMan.GetComponent<HTNComponent>(walkerUid);
            htnComponent.Blackboard.SetValue(NPCBlackboard.NavPush, navPush);
            SEntMan.System<NPCSteeringSystem>().Register(walkerUid, new EntityCoordinates(gridUid, new Vector2(6.5f, 7.5f)));
            SEntMan.System<NPCSystem>().WakeNPC(walkerUid, htnComponent);
        });

        await Pair.RunTicksSync(900);
        return (gridUid, walkerUid, closetUid);
    }
}
