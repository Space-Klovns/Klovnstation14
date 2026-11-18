#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server.Gravity;
using Content.Shared._KS14.Storage;
using Content.Shared.CombatMode;
using Content.Shared.Gravity;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Weapons.Melee.Events;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Storage;

/// <summary>
///     Shoving someone into an open closet, as the shover's own client sees it: the closet shuts on them the moment
///         the client asks, and stays shut on every tick after, rather than flapping open again until the server
///         has answered.
/// </summary>
[TestFixture]
[TestOf(typeof(KsEntityStorageCatchSystem))]
public sealed class KsEntityStorageCatchPredictionTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId Human = "MobHuman";
    private readonly EntProtoId _closet = "KsCatchPredictionTestCloset";

    /// <summary>
    ///     How many ticks the closet is watched for after the shove: well past the round trip to the server.
    /// </summary>
    private const int WatchedTicks = 20;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: ClosetBase
  id: KsCatchPredictionTestCloset
  components:
  - type: KsEntityStorageCatch
  - type: KsShoveObstacle
";

    [Test]
    public async Task ClientKeepsTheClosetShutOnTheShoved()
    {
        var testMap = await Pair.CreateTestMap();
        EntityUid shoverUid = default;
        EntityUid targetUid = default;
        EntityUid closetUid = default;

        await Server.WaitPost(() =>
        {
            var mapSystem = SEntMan.System<SharedMapSystem>();
            for (var x = -1; x < 5; x++)
            {
                mapSystem.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, 0), testMap.Tile.Tile);
            }

            var gravityComponent = SEntMan.EnsureComponent<GravityComponent>(testMap.Grid.Owner);
            SEntMan.System<GravitySystem>().EnableGravity(testMap.Grid.Owner, gravityComponent);

            shoverUid = SEntMan.SpawnEntity(Human, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            targetUid = SEntMan.SpawnEntity(Human, new EntityCoordinates(testMap.Grid, 1.5f, 0.5f));
            closetUid = SEntMan.SpawnEntity(_closet, new EntityCoordinates(testMap.Grid, 2.5f, 0.5f));
            SEntMan.System<SharedEntityStorageSystem>().OpenStorage(closetUid);

            Server.PlayerMan.SetAttachedEntity(Pair.Player!, shoverUid);
            SEntMan.System<SharedCombatModeSystem>().SetInCombatMode(shoverUid, true);
        });

        await Pair.RunTicksSync(10);

        var netTarget = SEntMan.GetNetEntity(targetUid);
        var netCoordinates = SEntMan.GetNetCoordinates(new EntityCoordinates(testMap.Grid, 1.5f, 0.5f));
        var clientTargetUid = CEntMan.GetEntity(netTarget);
        var clientClosetUid = CEntMan.GetEntity(SEntMan.GetNetEntity(closetUid));

        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.GetComponent<EntityStorageComponent>(clientClosetUid).Open, "The client must see the closet open.");

            // What a right-click in combat mode sends. The client runs its own prediction of it at once.
            CEntMan.RaisePredictiveEvent(new DisarmAttackEvent(netTarget, netCoordinates));

            var storage = CEntMan.GetComponent<EntityStorageComponent>(clientClosetUid);
            Assert.That(storage.Open, Is.False, "The client must predict the closet shutting.");
            Assert.That(storage.Contents.Contains(clientTargetUid), "The client must predict the shoved landing inside.");
        });

        for (var tick = 1; tick <= WatchedTicks; tick++)
        {
            await Pair.RunTicksSync(1);
            await Client.WaitAssertion(() =>
            {
                var storage = CEntMan.GetComponent<EntityStorageComponent>(clientClosetUid);
                Assert.That(storage.Open, Is.False, $"The closet must stay shut on the client: it was open again {tick} ticks after the shove.");
                Assert.That(storage.Contents.Contains(clientTargetUid),
                    $"The shoved must stay inside on the client: they were out again {tick} ticks after the shove.");
            });
        }

        await Server.WaitAssertion(() =>
        {
            var storage = SEntMan.GetComponent<EntityStorageComponent>(closetUid);
            Assert.That(storage.Open, Is.False, "The server must shut the closet too.");
            Assert.That(storage.Contents.Contains(targetUid), "The server must put the shoved inside too.");
        });
    }
}
