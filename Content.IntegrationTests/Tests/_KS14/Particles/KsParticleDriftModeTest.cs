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
///     <see cref="KsParticlesPrototype.DriftMode"/>: by default drift jolts each step's motion and leaves the velocity
///         alone; with <see cref="KsParticleDriftMode.Accelerate"/> it builds the velocity up, by drift every 0.1 s, as
///         BYOND does.
/// </summary>
[TestFixture]
[TestOf(typeof(KsParticlesSystem))]
public sealed class KsParticleDriftModeTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true };

    private const float Dt = 1f / 20f;

    private readonly ProtoId<KsParticlesPrototype> _offset = "KsParticleDriftModeTestOffset";
    private readonly ProtoId<KsParticlesPrototype> _accelerate = "KsParticleDriftModeTestAccelerate";

    [TestPrototypes]
    private const string Prototypes = @"
- type: ksParticles
  id: KsParticleDriftModeTestOffset
  burst: 1
  spawning: 0
  lifespan: 0
  space: World
  tickRate: 20
  drift: [ 1, 0 ]

- type: ksParticles
  parent: KsParticleDriftModeTestOffset
  id: KsParticleDriftModeTestAccelerate
  driftMode: Accelerate
";

    [Test]
    public async Task OffsetJoltsAndAccelerateBuildsUp()
    {
        var testMap = await Pair.CreateTestMap();

        await Client.WaitAssertion(() =>
        {
            var particlesSystem = CEntMan.System<KsParticlesSystem>();
            var offsetUid = particlesSystem.SpawnLocal(_offset, new EntityCoordinates(testMap.CMapUid, Vector2.Zero))!.Value;
            var accelerateUid = particlesSystem.SpawnLocal(_accelerate, new EntityCoordinates(testMap.CMapUid, new Vector2(0f, 5f)))!.Value;

            // One second: twenty steps.
            for (var step = 0; step < 20; step++)
                particlesSystem.FrameUpdate(Dt);

            var offsetParticle = CEntMan.GetComponent<KsParticleEmitterComponent>(offsetUid).Particles[0];
            var accelerateParticle = CEntMan.GetComponent<KsParticleEmitterComponent>(accelerateUid).Particles[0];
            Assert.Multiple(() =>
            {
                Assert.That(offsetParticle.Velocity.X, Is.Zero, "an offset drift changed the velocity");
                Assert.That(offsetParticle.Position.X, Is.EqualTo(1f).Within(0.06f), "an offset drift of 1 m/s did not move it 1 m in a second");

                // 1 m/s every 0.1 s, for a second: 10 m/s.
                Assert.That(accelerateParticle.Velocity.X, Is.EqualTo(10f).Within(0.01f), "an accelerating drift did not build the velocity up as BYOND does");
                Assert.That(accelerateParticle.Position.X, Is.GreaterThan(4f), "an accelerating drift did not carry the particle along with its velocity");
            });
        });
    }
}
