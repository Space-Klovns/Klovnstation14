using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Shared._KS14.Ladders;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Ladders;

[TestFixture]
[TestOf(typeof(KsLadderSystem))]
public sealed class KsLadderTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestLadder
  name: test ladder
  components:
  - type: KsLadder

- type: entity
  parent: KsTestLadder
  id: KsTestLadderSlow
  components:
  - type: KsLadder
    useDelay: 1

- type: entity
  parent: KsTestLadder
  id: KsTestLadderKeyed
  components:
  - type: KsLadder
    linkKey: KsTestShaft

- type: entity
  id: KsTestLadderClimber
  name: test climber
  components:
  - type: DoAfter
  - type: Puller
    needsHands: false
  - type: Physics
    bodyType: KinematicController

- type: entity
  id: KsTestLadderCrate
  name: test crate
  components:
  - type: Pullable
  - type: Physics
    bodyType: Dynamic
";

    private readonly EntProtoId _ladder = "KsTestLadder";
    private readonly EntProtoId _slowLadder = "KsTestLadderSlow";
    private readonly EntProtoId _keyedLadder = "KsTestLadderKeyed";
    private readonly EntProtoId _climber = "KsTestLadderClimber";
    private readonly EntProtoId _crate = "KsTestLadderCrate";

    private void Use(EntityUid user, EntityUid ladder)
    {
        SEntMan.System<SharedInteractionSystem>().InteractionActivate(user, ladder,
            checkCanInteract: false, checkUseDelay: false, checkAccess: false, complexInteractions: true);
    }

    private MapCoordinates MapPosition(EntityUid uid)
        => SEntMan.System<SharedTransformSystem>().GetMapCoordinates(uid);

    [TestCase(false)]
    [TestCase(true)]
    public async Task UseMovesToTheLinkedLadder(bool otherMap)
    {
        var map = await Pair.CreateTestMap();
        var farMap = otherMap ? await Pair.CreateTestMap() : map;
        EntityUid climber = default, far = default;

        await Server.WaitAssertion(() =>
        {
            var near = SSpawnAtPosition(_ladder, map.GridCoords);
            far = SSpawnAtPosition(_ladder, farMap.GridCoords.Offset(new Vector2(3, 0)));
            SEntMan.System<KsLadderSystem>().Link(near, far);
            Assert.That(SComp<KsLadderComponent>(far).Linked, Is.EqualTo(near), "links are two-way");

            climber = SSpawnAtPosition(_climber, map.GridCoords.Offset(new Vector2(0.5f, 0)));
            Use(climber, near);
            Assert.That(MapPosition(climber), Is.EqualTo(MapPosition(far)));

            Use(climber, far);
            Assert.That(MapPosition(climber), Is.EqualTo(MapPosition(near)), "and back down");
        });
    }

    [Test]
    public async Task UseDelayTakesADoAfter()
    {
        var map = await Pair.CreateTestMap();
        EntityUid climber = default, near = default, far = default;
        await Server.WaitAssertion(() =>
        {
            near = SSpawnAtPosition(_slowLadder, map.GridCoords);
            far = SSpawnAtPosition(_ladder, map.GridCoords.Offset(new Vector2(3, 0)));
            SEntMan.System<KsLadderSystem>().Link(near, far);
            climber = SSpawnAtPosition(_climber, map.GridCoords);
            Use(climber, near);
            Assert.That(MapPosition(climber), Is.Not.EqualTo(MapPosition(far)), "no climb before the DoAfter ends");
            Assert.That(SComp<DoAfterComponent>(climber).DoAfters, Is.Not.Empty);
        });

        var delay = SComp<KsLadderComponent>(near).UseDelay!.Value;
        await Pair.RunTicksSync((int) Math.Ceiling(delay.TotalSeconds * Server.Timing.TickRate) + 5);
        await Server.WaitAssertion(() =>
            Assert.That(MapPosition(climber), Is.EqualTo(MapPosition(far))));
    }

    [Test]
    public async Task DeletedLinkDoesNothing()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var near = SSpawnAtPosition(_ladder, map.GridCoords);
            var far = SSpawnAtPosition(_ladder, map.GridCoords.Offset(new Vector2(3, 0)));
            SEntMan.System<KsLadderSystem>().Link(near, far);
            SEntMan.DeleteEntity(far);
            Assert.That(SComp<KsLadderComponent>(near).Linked, Is.Null, "the deleted end unlinks the other");

            var climber = SSpawnAtPosition(_climber, map.GridCoords);
            var before = MapPosition(climber);
            Use(climber, near);
            Assert.That(MapPosition(climber), Is.EqualTo(before));
            Assert.That(SEntMan.GetComponent<TransformComponent>(climber).MapID, Is.Not.EqualTo(MapId.Nullspace));
        });
    }

    [Test]
    public async Task LinkKeyResolvesAtMapInit()
    {
        var map = await Pair.CreateTestMap(initialized: false);
        await Server.WaitAssertion(() =>
        {
            var first = SSpawnAtPosition(_keyedLadder, map.GridCoords);
            var second = SSpawnAtPosition(_keyedLadder, map.GridCoords.Offset(new Vector2(3, 0)));
            Assert.That(SComp<KsLadderComponent>(first).Linked, Is.Null, "nothing links before map init");

            SEntMan.System<SharedMapSystem>().InitializeMap(map.MapId);
            Assert.That(SComp<KsLadderComponent>(first).Linked, Is.EqualTo(second));
            Assert.That(SComp<KsLadderComponent>(second).Linked, Is.EqualTo(first));
        });
    }

    [Test]
    public async Task PulledEntityComesAlongStillPulled()
    {
        var map = await Pair.CreateTestMap();
        var farMap = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var near = SSpawnAtPosition(_ladder, map.GridCoords);
            var far = SSpawnAtPosition(_ladder, farMap.GridCoords.Offset(new Vector2(3, 0)));
            SEntMan.System<KsLadderSystem>().Link(near, far);
            var climber = SSpawnAtPosition(_climber, map.GridCoords);
            var crate = SSpawnAtPosition(_crate, map.GridCoords.Offset(new Vector2(0.5f, 0)));
            var pullingSystem = SEntMan.System<PullingSystem>();
            Assert.That(pullingSystem.TryStartPull(climber, crate), Is.True, "precondition: the climber pulls the crate");

            Use(climber, near);
            Assert.That(MapPosition(crate).MapId, Is.EqualTo(MapPosition(far).MapId));
            Assert.That(SComp<PullerComponent>(climber).Pulling, Is.EqualTo(crate), "still pulled after the climb");
        });
    }
}
