using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Shared._KS14.Ladders;
using Content.Shared.Interaction;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Content.IntegrationTests.Fixtures.Attributes;

namespace Content.IntegrationTests.Tests._KS14.Ladders;

/// <summary>
///     With PVS, a client is usually not sent the far end of a ladder on another map: it must not call the ladder
///         unlinked, and the server's climb must still reach it.
/// </summary>
[TestFixture]
[TestOf(typeof(KsLadderSystem))]
public sealed class KsLadderPvsTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestPvsLadder
  name: test ladder
  components:
  - type: KsLadder

- type: entity
  id: KsTestPvsLadderClimber
  name: test climber
  components:
  - type: DoAfter
  - type: Eye
";

    private readonly EntProtoId _ladder = "KsTestPvsLadder";
    private readonly EntProtoId _climber = "KsTestPvsLadderClimber";
    private const string NotLinkedMessage = "ks-ladder-not-linked";

    [Test]
    public async Task UnseenFarEndIsNotCalledUnlinked()
    {
        await OverrideCVar(Side.Server, CVars.NetPVS, true);
        var map = await Pair.CreateTestMap();
        var farMap = await Pair.CreateTestMap();
        EntityUid near = default, far = default, climber = default;
        await Server.WaitPost(() =>
        {
            near = SSpawnAtPosition(_ladder, map.GridCoords);
            far = SSpawnAtPosition(_ladder, farMap.GridCoords.Offset(new Vector2(3, 0)));
            SEntMan.System<KsLadderSystem>().Link(near, far);
            climber = SSpawnAtPosition(_climber, map.GridCoords);
            Server.PlayerMan.SetAttachedEntity(ServerSession!, climber);
        });
        await RunUntilSynced();
        await Pair.RunTicksSync(10);

        var farNetEntity = SEntMan.GetNetEntity(far);
        var notLinked = "";
        await Client.WaitPost(() =>
        {
            Assert.That(CEntMan.TryGetEntity(farNetEntity, out var clientFar) && CEntMan.HasComponent<KsLadderComponent>(clientFar),
                Is.False, "precondition: the client hasn't been sent the far end");
            Assert.That(CEntMan.GetComponent<KsLadderComponent>(ToClientUid(near)).Linked, Is.Not.Null,
                "precondition: the client knows the ladder is linked");

            notLinked = Client.ResolveDependency<ILocalizationManager>().GetString(NotLinkedMessage);
            CEntMan.System<SharedInteractionSystem>().InteractionActivate(ToClientUid(climber), ToClientUid(near),
                checkCanInteract: false, checkUseDelay: false, checkAccess: false, complexInteractions: true);
        });
        await Server.WaitPost(() =>
            SEntMan.System<SharedInteractionSystem>().InteractionActivate(climber, near,
                checkCanInteract: false, checkUseDelay: false, checkAccess: false, complexInteractions: true));
        await Pair.RunTicksSync(10);

        var serverPosition = default(MapCoordinates);
        await Server.WaitAssertion(() =>
        {
            var transformSystem = SEntMan.System<SharedTransformSystem>();
            serverPosition = transformSystem.GetMapCoordinates(climber);
            Assert.That(serverPosition, Is.EqualTo(transformSystem.GetMapCoordinates(far)));
        });
        await Client.WaitAssertion(() =>
        {
            var labels = CEntMan.System<Content.Client.Popups.PopupSystem>().WorldLabels.Select(label => label.Text);
            Assert.That(labels, Does.Not.Contain(notLinked), "the client called a linked ladder unlinked");
            var clientPosition = CEntMan.System<SharedTransformSystem>().GetMapCoordinates(ToClientUid(climber));
            Assert.That(clientPosition.Position, Is.EqualTo(serverPosition.Position));
            Assert.That(clientPosition.MapId, Is.Not.EqualTo(MapId.Nullspace));
        });
    }
}
