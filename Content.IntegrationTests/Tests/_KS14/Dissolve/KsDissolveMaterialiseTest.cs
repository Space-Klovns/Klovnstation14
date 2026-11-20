#nullable enable
using Content.Client._KS14.Dissolve;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Dissolve;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;
using ServerDissolveSystem = Content.Server._KS14.Dissolve.KsDissolveSystem;

namespace Content.IntegrationTests.Tests._KS14.Dissolve;

/// <summary>
///     The dissolve played backwards (<see cref="ServerDissolveSystem.Materialise"/>): the client draws it, and the
///         entity is kept and drawn whole once it has finished; a materialise turned into a dissolve still deletes.
/// </summary>
[TestFixture]
[TestOf(typeof(ServerDissolveSystem))]
public sealed class KsDissolveMaterialiseTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private readonly EntProtoId _thing = "KsTestDissolveMaterialiseThing";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestDissolveMaterialiseThing
  components:
  - type: Sprite
    sprite: _KS14/Interface/Perks/perks.rsi
    state: healing
";

    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(0.5);

    private int Ticks(float seconds) => (int)MathF.Ceiling(seconds * (float)Pair.Server.ResolveDependency<IGameTiming>().TickRate);

    private async Task<EntityUid> SpawnThing()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        EntityUid uid = default;
        await server.WaitPost(() =>
        {
            // A player looking on, so the client is sent it.
            var viewerUid = server.EntMan.SpawnAtPosition(null, new EntityCoordinates(testMap.Grid, 1.5f, 0.5f));
            server.EntMan.EnsureComponent<EyeComponent>(viewerUid);
            server.PlayerMan.SetAttachedEntity(Pair.Player!, viewerUid);

            uid = server.EntMan.SpawnAtPosition(_thing, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            server.System<ServerDissolveSystem>().Materialise(uid, Duration);
        });
        await Pair.RunTicksSync(5);
        return uid;
    }

    [Test]
    public async Task MaterialisingEntityIsDrawnBackwardsThenKeptWhole()
    {
        var server = Pair.Server;
        var client = Pair.Client;
        var uid = await SpawnThing();
        var clientUid = Pair.ToClientUid(uid);

        await server.WaitAssertion(() =>
            Assert.That(server.System<ServerDissolveSystem>().IsMaterialising(uid), "it should be materialising"));
        await client.WaitAssertion(() =>
        {
            Assert.That(client.EntMan.TryGetComponent<KsDissolveComponent>(clientUid, out var dissolve) && dissolve.Reverse,
                "the client should know it is a materialise");
            Assert.That(client.EntMan.HasComponent<KsDissolveVisualsComponent>(clientUid), "the client should be drawing it");
        });

        await Pair.RunTicksSync(Ticks((float)(Duration + ServerDissolveSystem.Linger).TotalSeconds + 0.5f));

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.Deleted(uid), Is.False, "a materialised entity should be kept");
            Assert.That(server.EntMan.HasComponent<KsDissolveComponent>(uid), Is.False, "the materialise should be over");
        });
        await client.WaitAssertion(() =>
        {
            Assert.That(client.EntMan.HasComponent<KsDissolveVisualsComponent>(clientUid), Is.False, "the client should stop drawing it");
            Assert.That(client.EntMan.GetComponent<SpriteComponent>(clientUid).Visible, "it should be drawn whole");
        });
    }

    [Test]
    public async Task MaterialiseTurnedIntoADissolveStillDeletes()
    {
        var server = Pair.Server;
        var uid = await SpawnThing();

        await server.WaitPost(() => server.System<ServerDissolveSystem>().Dissolve(uid, Duration));
        await server.WaitAssertion(() =>
            Assert.That(server.System<ServerDissolveSystem>().IsDissolving(uid), "it should be dissolving now"));

        await Pair.RunTicksSync(Ticks((float)(Duration + ServerDissolveSystem.Linger).TotalSeconds + 0.5f));

        await server.WaitAssertion(() => Assert.That(server.EntMan.Deleted(uid), "the dissolved entity should be deleted"));
    }
}
