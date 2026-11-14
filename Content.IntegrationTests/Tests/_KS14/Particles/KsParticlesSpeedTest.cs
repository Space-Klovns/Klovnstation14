#nullable enable
using System.Numerics;
using Content.Client._KS14.Particles;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Particles;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Particles;

/// <summary>
///     <see cref="KsParticlesComponent.Speed"/> multiplies each particle's starting velocity, and nothing else.
/// </summary>
[TestFixture]
[TestOf(typeof(KsParticlesSystem))]
public sealed class KsParticlesSpeedTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true };

    private readonly ProtoId<KsParticlesPrototype> _mover = "KsParticlesSpeedTestMover";

    [TestPrototypes]
    private const string Prototypes = @"
- type: ksParticles
  id: KsParticlesSpeedTestMover
  burst: 1
  spawning: 0
  lifespan: 10
  velocity: [ 0, 1.5 ]
  scale: 0.75
";

    [Test]
    public async Task SpeedMultipliesTheStartingVelocityOnly()
    {
        var testMap = await Pair.CreateTestMap();
        const float speed = 3f;

        await Client.WaitAssertion(() =>
        {
            var particlesSystem = CEntMan.System<KsParticlesSystem>();
            var prototype = Client.ProtoMan.Index(_mover);

            var plainUid = particlesSystem.SpawnLocal(_mover, new EntityCoordinates(testMap.CMapUid, Vector2.Zero))!.Value;
            var fastUid = particlesSystem.SpawnLocal(_mover, new EntityCoordinates(testMap.CMapUid, new Vector2(5f, 0f)))!.Value;
            particlesSystem.SetSpeed(fastUid, speed);

            particlesSystem.FrameUpdate(0.05f);

            var plainParticle = CEntMan.GetComponent<KsParticleEmitterComponent>(plainUid).Particles[0];
            var fastParticle = CEntMan.GetComponent<KsParticleEmitterComponent>(fastUid).Particles[0];
            var expectedVelocity = prototype.Velocity.SampleVector2(new System.Random(0));
            Assert.Multiple(() =>
            {
                Assert.That(plainParticle.Velocity.Y, Is.EqualTo(expectedVelocity.Y).Within(0.001f));
                Assert.That(fastParticle.Velocity.Y, Is.EqualTo(expectedVelocity.Y * speed).Within(0.001f), "speed multiplies the velocity");
                Assert.That(fastParticle.Scale, Is.EqualTo(plainParticle.Scale), "and leaves the size alone");
            });
        });
    }
}
