#nullable enable
using System.Linq;
using System.Numerics;
using Content.Client._KS14.Particles;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Particles;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;

namespace Content.IntegrationTests.Tests._KS14.Particles;

/// <summary>
///     The three ways an emitter comes to exist, and that each one leaves exactly one emitting copy on the client
///         that should show it.
/// </summary>
[TestFixture]
[TestOf(typeof(KsSharedParticlesSystem))]
public sealed class KsParticlesNetworkTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private readonly ProtoId<KsParticlesPrototype> _flood = "KsParticlesNetTestFlood";

    [TestPrototypes]
    private const string Prototypes = @"
- type: ksParticles
  id: KsParticlesNetTestFlood
  count: 10
  spawning: 1000
  lifespan: 0
  tickRate: 20
";

    private Server._KS14.Particles.KsParticlesSystem ServerSystem => SEntMan.System<Server._KS14.Particles.KsParticlesSystem>();
    private KsParticlesSystem ClientSystem => CEntMan.System<KsParticlesSystem>();

    [Test]
    public async Task ServerSpawnReachesTheClientAndLeavesWithIt()
    {
        var testMap = await Pair.CreateTestMap();

        EntityUid? serverUid = null;
        await Server.WaitPost(() => serverUid = ServerSystem.SpawnPvs(_flood, new EntityCoordinates(testMap.MapUid, Vector2.Zero)));
        Assert.That(serverUid, Is.Not.Null, "the server spawns a networked emitter");

        await RunTicksSync(10);

        var clientUid = ToClientUid(serverUid!.Value);
        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.EntityExists(clientUid), "the emitter reached the client");
            Assert.That(CEntMan.IsClientSide(clientUid), Is.False);
            var emitterComponent = CEntMan.GetComponent<KsParticleEmitterComponent>(clientUid);
            Assert.That(emitterComponent.Prototype?.ID, Is.EqualTo(_flood.Id), "the client resolved the networked prototype id");
            Assert.That(emitterComponent.Alive, Is.EqualTo(10), "the client's frames ran its simulation");
        });

        await Server.WaitPost(() => SEntMan.DeleteEntity(serverUid.Value));
        await RunTicksSync(5);

        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.EntityExists(clientUid), Is.False, "deleted on the server, gone on the client");
            Assert.That(ClientSystem.Layers.SelectMany(layer => layer).Select(entry => entry.Uid), Does.Not.Contain(clientUid));
        });
    }

    [Test]
    public async Task ServerNeverSpawnsLocallyAndClientNeverSpawnsForPvs()
    {
        var testMap = await Pair.CreateTestMap();
        var before = 0;

        await Server.WaitPost(() =>
        {
            before = SEntMan.Count<KsParticlesComponent>();
            Assert.That(ServerSystem.SpawnLocal(_flood, new EntityCoordinates(testMap.MapUid, Vector2.Zero)), Is.Null);
            Assert.That(ServerSystem.SpawnPredicted(_flood, new EntityCoordinates(testMap.MapUid, Vector2.Zero), user: null), Is.Not.Null, "with no user, SpawnPredicted is a server spawn");
            Assert.That(SEntMan.Count<KsParticlesComponent>(), Is.EqualTo(before + 1));
        });

        await Client.WaitPost(() =>
        {
            before = CEntMan.Count<KsParticlesComponent>();
            Assert.That(ClientSystem.SpawnPvs(_flood, new EntityCoordinates(testMap.CMapUid, Vector2.Zero)), Is.Null);
            Assert.That(ClientSystem.SpawnPredicted(_flood, new EntityCoordinates(testMap.CMapUid, Vector2.Zero), user: null), Is.Null, "with no user, the client leaves it to the server");
            Assert.That(CEntMan.Count<KsParticlesComponent>(), Is.EqualTo(before));
        });
    }

    /// <summary>
    ///     The server's copy of a spawn made for this client's player is hidden here: not stepped, not listed. One
    ///         made for someone else is shown as usual.
    /// </summary>
    [Test]
    public async Task ServerCopyOfOwnPredictedSpawnIsHiddenOnlyForItsUser()
    {
        var testMap = await Pair.CreateTestMap();

        EntityUid ownUid = default, otherUid = default;
        await Server.WaitPost(() =>
        {
            var viewerUid = SEntMan.SpawnEntity(null, new MapCoordinates(Vector2.Zero, testMap.MapId));
            var otherUserUid = SEntMan.SpawnEntity(null, new MapCoordinates(new Vector2(3f, 0f), testMap.MapId));
            Server.PlayerMan.SetAttachedEntity(Pair.Player!, viewerUid);

            ownUid = ServerSystem.SpawnPredicted(_flood, new EntityCoordinates(viewerUid, Vector2.Zero), user: viewerUid)!.Value;
            otherUid = ServerSystem.SpawnPredicted(_flood, new EntityCoordinates(otherUserUid, Vector2.Zero), user: otherUserUid)!.Value;
        });

        await RunTicksSync(10);

        var ownClientUid = ToClientUid(ownUid);
        var otherClientUid = ToClientUid(otherUid);
        await Client.WaitAssertion(() =>
        {
            var listed = ClientSystem.Layers.SelectMany(layer => layer).Select(entry => entry.Uid).ToList();
            Assert.Multiple(() =>
            {
                Assert.That(ClientSystem.IsHiddenHere(ownClientUid), "the server's copy of this player's own spawn is hidden here");
                Assert.That(CEntMan.GetComponent<KsParticleEmitterComponent>(ownClientUid).Alive, Is.EqualTo(0), "and never stepped");
                Assert.That(listed, Does.Not.Contain(ownClientUid), "nor drawn");

                Assert.That(ClientSystem.IsHiddenHere(otherClientUid), Is.False, "another user's spawn is shown");
                Assert.That(CEntMan.GetComponent<KsParticleEmitterComponent>(otherClientUid).Alive, Is.EqualTo(10));
                Assert.That(listed, Does.Contain(otherClientUid));
            });
        });
    }

    /// <summary>
    ///     The user's own client makes its copy at once, as a client-side entity, and it emits.
    /// </summary>
    [Test]
    public async Task ClientPredictedSpawnForTheLocalPlayerEmitsAtOnce()
    {
        var testMap = await Pair.CreateTestMap();

        EntityUid viewerUid = default;
        await Server.WaitPost(() =>
        {
            viewerUid = SEntMan.SpawnEntity(null, new MapCoordinates(Vector2.Zero, testMap.MapId));
            Server.PlayerMan.SetAttachedEntity(Pair.Player!, viewerUid);
        });
        await RunTicksSync(5);

        var clientViewerUid = ToClientUid(viewerUid);
        EntityUid? keptUid = null;
        await Client.WaitPost(() =>
        {
            keptUid = ClientSystem.SpawnPredicted(_flood, new EntityCoordinates(clientViewerUid, Vector2.Zero), user: clientViewerUid);
            Assert.That(keptUid, Is.Not.Null);
            Assert.That(CEntMan.IsClientSide(keptUid!.Value), "the user's copy is the client's own entity");
            ClientSystem.FrameUpdate(0.05f);
            Assert.That(CEntMan.GetComponent<KsParticleEmitterComponent>(keptUid.Value).Alive, Is.EqualTo(10), "and it emits right away");
        });
    }

    /// <summary>
    ///     A timed despawn ends a client-only emitter on the client, as it does a server one.
    /// </summary>
    [Test]
    public async Task TimedDespawnEndsALocalEmitter()
    {
        var testMap = await Pair.CreateTestMap();

        EntityUid uid = default;
        await Client.WaitPost(() =>
        {
            uid = ClientSystem.SpawnLocal(_flood, new EntityCoordinates(testMap.CMapUid, Vector2.Zero))!.Value;
            CEntMan.EnsureComponent<TimedDespawnComponent>(uid).Lifetime = 0.1f;
        });

        await RunTicksSync(30);
        await Client.WaitAssertion(() => Assert.That(CEntMan.EntityExists(uid), Is.False));
    }

    /// <summary>
    ///     Every shipped prototype's sprites resolve on the client. A missing state would log an error, which fails
    ///         the test on its own; this also checks the count came through.
    /// </summary>
    [Test]
    public async Task ShippedPrototypesResolveTheirSpritesOnTheClient()
    {
        await Client.WaitPost(() =>
        {
            var spriteSystem = CEntMan.System<Robust.Client.GameObjects.SpriteSystem>();
            var sawmill = Client.ResolveDependency<Robust.Shared.Log.ILogManager>().GetSawmill("particles.test");
            foreach (var prototype in Client.ProtoMan.EnumeratePrototypes<KsParticlesPrototype>())
            {
                if (prototype.Abstract)
                    continue;

                var set = new KsParticleSpriteSet(prototype, spriteSystem, sawmill);
                Assert.That(set.States, Has.Length.EqualTo(prototype.Sprites.Count), prototype.ID);
                for (var i = 0; i < set.States.Length; i++)
                    Assert.That(set.States[i], Is.Not.SameAs(Robust.Client.Graphics.Texture.White), $"{prototype.ID} sprite {i} fell back to the white pixel");
            }
        });
    }
}
