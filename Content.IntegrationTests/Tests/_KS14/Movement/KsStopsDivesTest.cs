#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server.Actions;
using Content.Shared._KS14.Movement.StopsDives;
using Content.Shared.Actions;
using Content.Shared.Movement.Components;
using Content.Shared.Throwing;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.Movement;

/// <summary>
///     <see cref="KsStopsDivesComponent"/>: a dive that runs into it ends in it, one over the same thing without the
///         component carries on to where it was aimed, and something thrown goes over either.
/// </summary>
[TestFixture]
[TestOf(typeof(KsStopsDivesSystem))]
public sealed class KsStopsDivesTest : GameTest
{
    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _diver = "KsStopsDivesTestDiver";
    private readonly EntProtoId _snare = "KsStopsDivesTestSnare";
    private readonly EntProtoId _plainStrip = "KsStopsDivesTestPlainStrip";
    private readonly EntProtoId _thrown = "KsStopsDivesTestThrown";

    // The strips fill the tile at x = 2, across the way east from the diver at x = 0.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsStopsDivesTestDiver
  components:
  - type: Actions
  - type: JumpAbility
    action: KsStopsDivesTestDiveAction
    canCollide: false
    punishKnockdown: null

- type: entity
  parent: BaseAction
  id: KsStopsDivesTestDiveAction
  components:
  - type: Action
    useDelay: 6
  - type: TargetAction
  - type: WorldTargetAction
    event: !type:KsGravityJumpWorldEvent

- type: entity
  id: KsStopsDivesTestPlainStrip
  components:
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        hard: false
        shape:
          !type:PhysShapeAabb
          bounds: ""-0.5,-0.5,0.5,0.5""
        layer:
        - MidImpassable

- type: entity
  parent: KsStopsDivesTestPlainStrip
  id: KsStopsDivesTestSnare
  components:
  - type: KsStopsDives

- type: entity
  id: KsStopsDivesTestThrown
  components:
  - type: Physics
    bodyType: Dynamic
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeCircle
          radius: 0.2
        density: 20
        mask:
        - ItemMask
        layer:
        - Opaque
";

    private const float StripLeft = 2f;
    private const float StripRight = 3f;
    private static readonly Vector2 Start = new(0.5f, 0.5f);
    private static readonly Vector2 Aim = new(5.5f, 0.5f);

    private async Task<EntityUid> SetUp(EntProtoId strip)
    {
        var testMap = await Pair.CreateTestMap();
        EntityUid gridUid = default;

        await Server.WaitPost(() =>
        {
            gridUid = MakeGrid(SEntMan, Server.ResolveDependency<ITileDefinitionManager>(), testMap.MapId, testMap.Grid,
                new Vector2i(-2, -2), new Vector2i(8, 2)).Owner;
            SEntMan.SpawnAtPosition(strip, new EntityCoordinates(gridUid, new Vector2(StripLeft + 0.5f, 0.5f)));
        });

        return gridUid;
    }

    /// <summary>
    ///     Dives from the start at the aim, waits for the dive to be over, and returns how far east the diver got.
    /// </summary>
    private async Task<float> Dive(EntityUid gridUid)
    {
        EntityUid diverUid = default;
        await Server.WaitPost(() => diverUid = SEntMan.SpawnAtPosition(_diver, new EntityCoordinates(gridUid, Start)));
        await Pair.RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            var actionsSystem = Server.System<ActionsSystem>();
            var actionUid = SComp<JumpAbilityComponent>(diverUid).ActionEntity;
            Assert.That(actionsSystem.GetAction(actionUid), Is.Not.Null, "the diver should have its dive");

            var actionEvent = (WorldTargetActionEvent)actionsSystem.GetEvent(actionUid!.Value)!;
            actionEvent.Target = new EntityCoordinates(gridUid, Aim);
            actionsSystem.PerformAction(diverUid, actionsSystem.GetAction(actionUid)!.Value, actionEvent: actionEvent, predicted: false);

            Assert.That(SEntMan.HasComponent<ActiveLeaperComponent>(diverUid), "the diver should be diving");
        });

        return await WaitForRest(diverUid, () => SEntMan.HasComponent<ActiveLeaperComponent>(diverUid));
    }

    /// <summary>
    ///     Runs until <paramref name="inFlight"/> is false, and a little more for the body to come to rest, and returns how
    ///         far east <paramref name="uid"/> is then.
    /// </summary>
    private async Task<float> WaitForRest(EntityUid uid, Func<bool> inFlight)
    {
        var flying = true;
        for (var tick = 0; tick < 600 && flying; tick++)
        {
            await Pair.RunTicksSync(1);
            await Server.WaitPost(() => flying = inFlight());
        }

        Assert.That(flying, Is.False, "it should have come down");
        await Pair.RunTicksSync(30);

        var x = 0f;
        await Server.WaitPost(() => x = SEntMan.GetComponent<TransformComponent>(uid).LocalPosition.X);
        return x;
    }

    [Test]
    public async Task ADiveIntoItEndsInIt()
    {
        var gridUid = await SetUp(_snare);
        var x = await Dive(gridUid);

        // The diver's body is caught as its edge reaches the strip, so its middle is a little short of it.
        Assert.That(x, Is.InRange(StripLeft - 0.5f, StripRight), "the dive should end where it ran into the snare");
    }

    [Test]
    public async Task ADiveOverSomethingElseCarriesOn()
    {
        var gridUid = await SetUp(_plainStrip);
        var x = await Dive(gridUid);

        Assert.That(x, Is.GreaterThan(StripRight), "the dive should carry on past a strip that does not stop dives");
    }

    [Test]
    public async Task SomethingThrownGoesOverIt()
    {
        var gridUid = await SetUp(_snare);
        EntityUid thrownUid = default;

        await Server.WaitPost(() =>
        {
            thrownUid = SEntMan.SpawnAtPosition(_thrown, new EntityCoordinates(gridUid, Start));
            Server.System<ThrowingSystem>().TryThrow(thrownUid, new EntityCoordinates(gridUid, Aim), baseThrowSpeed: 10f);
            Assert.That(SEntMan.HasComponent<ThrownItemComponent>(thrownUid), "it should have been thrown");
        });

        var x = await WaitForRest(thrownUid, () => SEntMan.HasComponent<ThrownItemComponent>(thrownUid));
        Assert.That(x, Is.GreaterThan(StripRight), "something thrown should go over what only stops dives");
    }
}
