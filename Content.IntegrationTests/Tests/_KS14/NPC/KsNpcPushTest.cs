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
