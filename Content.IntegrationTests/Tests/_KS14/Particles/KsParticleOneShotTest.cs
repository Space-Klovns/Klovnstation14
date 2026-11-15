#nullable enable
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
///     <see cref="KsParticlesPrototype.OneShot"/>: an emitter that fires and deletes itself once its particles are
///         gone, wherever it was spawned.
/// </summary>
[TestFixture]
[TestOf(typeof(KsSharedParticlesSystem))]
public sealed class KsParticleOneShotTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private readonly ProtoId<KsParticlesPrototype> _oneShot = "KsParticlesOneShotTest";
    private readonly ProtoId<KsParticlesPrototype> _lasting = "KsParticlesOneShotTestLasting";
    private readonly EntProtoId _carrier = "KsParticlesOneShotTestCarrier";

    [TestPrototypes]
    private const string Prototypes = @"
- type: ksParticles
  id: KsParticlesOneShotTest
  burst: 3
  spawning: 0
  oneShot: true
  lifespan: { kind: num, a: 0.1, b: 0.3 }

- type: ksParticles
  id: KsParticlesOneShotTestLasting
  burst: 3
  spawning: 0
  lifespan: 0.3

- type: entity
  id: KsParticlesOneShotTestCarrier
  components:
  - type: KsParticles
    prototype: KsParticlesOneShotTest
";

    private Server._KS14.Particles.KsParticlesSystem ServerSystem => SEntMan.System<Server._KS14.Particles.KsParticlesSystem>();
    private KsParticlesSystem ClientSystem => CEntMan.System<KsParticlesSystem>();

    [Test]
    public async Task LifetimeCoversTheLongestParticle()
    {
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ProtoMan.Index(_oneShot);
            Assert.That(prototype.OneShotLifetime, Is.GreaterThanOrEqualTo(prototype.Lifespan.MaxFloat), "the entity outlives the longest particle");
            Assert.That(Server.ProtoMan.Index(_lasting).OneShotLifetime, Is.Null, "not one-shot, never despawned");
        });
    }

    [Test]
    public async Task ServerSpawnDeletesItselfEverywhere()
    {
        var testMap = await Pair.CreateTestMap();

        EntityUid oneShotUid = default, lastingUid = default;
        await Server.WaitPost(() =>
        {
            oneShotUid = ServerSystem.SpawnPvs(_oneShot, new EntityCoordinates(testMap.MapUid, Vector2.Zero))!.Value;
            lastingUid = ServerSystem.SpawnPvs(_lasting, new EntityCoordinates(testMap.MapUid, Vector2.Zero))!.Value;
        });

        await RunTicksSync(3);
        var clientUid = ToClientUid(oneShotUid);
        await Client.WaitAssertion(() => Assert.That(CEntMan.EntityExists(clientUid), "the client got the emitter"));

        await RunSeconds(1f);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityExists(oneShotUid), Is.False, "a one-shot emitter deleted itself");
            Assert.That(SEntMan.EntityExists(lastingUid), "an ordinary one is left alone");
        });
        await Client.WaitAssertion(() => Assert.That(CEntMan.EntityExists(clientUid), Is.False, "and the client saw it go"));
    }

    [Test]
    public async Task LocalSpawnDeletesItself()
    {
        var testMap = await Pair.CreateTestMap();

        EntityUid uid = default;
        await Client.WaitPost(() => uid = ClientSystem.SpawnLocal(_oneShot, new EntityCoordinates(testMap.CMapUid, Vector2.Zero))!.Value);

        await RunSeconds(1f);
        await Client.WaitAssertion(() => Assert.That(CEntMan.EntityExists(uid), Is.False));
    }

    [Test]
    public async Task EntityCarryingAOneShotPrototypeDeletesItself()
    {
        var testMap = await Pair.CreateTestMap();

        EntityUid uid = default;
        await Server.WaitPost(() => uid = SEntMan.SpawnEntity(_carrier, new MapCoordinates(Vector2.Zero, testMap.MapId)));
        await Server.WaitAssertion(() => Assert.That(SEntMan.HasComponent<TimedDespawnComponent>(uid)));

        await RunSeconds(1f);
        await Server.WaitAssertion(() => Assert.That(SEntMan.EntityExists(uid), Is.False));
    }

    /// <summary>
    ///     A shipped one-shot effect that never ends, or never emits, is a mistake in its YAML.
    /// </summary>
    [Test]
    public async Task ShippedOneShotPrototypesEndAndEmit()
    {
        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var prototype in Server.ProtoMan.EnumeratePrototypes<KsParticlesPrototype>())
                {
                    if (prototype.Abstract || !prototype.OneShot)
                        continue;

                    Assert.That(prototype.OneShotLifetime, Is.Not.Null, $"{prototype.ID} is one-shot but its particles live forever");
                    Assert.That(prototype.Burst > 0 || prototype.EmitDuration != null, $"{prototype.ID} is one-shot but has neither a burst nor an emit duration");
                }
            });
        });
    }
}
