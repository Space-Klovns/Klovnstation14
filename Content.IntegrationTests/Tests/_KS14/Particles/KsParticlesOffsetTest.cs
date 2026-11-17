#nullable enable
using System.Numerics;
using Content.Client._KS14.Particles;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Particles;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Particles;

/// <summary>
///     <see cref="KsParticlesComponent.RotatingOffset"/> turns with the entity and leaves the emitter's own frame
///         alone; <see cref="KsParticlesComponent.Offset"/> stays where it was however the entity turns.
/// </summary>
[TestFixture]
[TestOf(typeof(KsParticlesSystem))]
public sealed class KsParticlesOffsetTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true };

    // An id that exists only in the test prototypes below.
    private readonly EntProtoId _emitter = "KsParticlesOffsetTestEmitter";

    [TestPrototypes]
    private const string Prototypes = @"
- type: ksParticles
  id: KsParticlesOffsetTestParticles
  spawning: 1
  lifespan: 10

- type: entity
  id: KsParticlesOffsetTestEmitter
  components:
  - type: KsParticles
    prototype: KsParticlesOffsetTestParticles
    offset: 0,-1
";

    [Test]
    public async Task OnlyTheRotatingOffsetTurnsWithTheEntity()
    {
        var testMap = await Pair.CreateTestMap();
        var turn = Angle.FromDegrees(90);

        await Client.WaitAssertion(() =>
        {
            var particlesSystem = CEntMan.System<KsParticlesSystem>();
            var transformSystem = CEntMan.System<SharedTransformSystem>();

            var fixedPosition = Vector2.Zero;
            var followingPosition = new Vector2(5f, 0f);
            var fixedUid = CEntMan.SpawnEntity(_emitter, new EntityCoordinates(testMap.CMapUid, fixedPosition));
            var followingUid = CEntMan.SpawnEntity(_emitter, new EntityCoordinates(testMap.CMapUid, followingPosition));
            var rotatingOffset = new Vector2(0.5f, -2f);
            particlesSystem.SetRotatingOffset(followingUid, rotatingOffset);
            transformSystem.SetLocalRotation(fixedUid, turn);
            transformSystem.SetLocalRotation(followingUid, turn);

            particlesSystem.FrameUpdate(0.05f);

            var offset = CEntMan.GetComponent<KsParticlesComponent>(fixedUid).Offset;
            var fixedEmitter = CEntMan.GetComponent<KsParticleEmitterComponent>(fixedUid);
            var followingEmitter = CEntMan.GetComponent<KsParticleEmitterComponent>(followingUid);
            var turnedOrigin = followingPosition + offset + turn.RotateVec(rotatingOffset);
            Assert.Multiple(() =>
            {
                Assert.That((fixedEmitter.Origin - (fixedPosition + offset)).Length(), Is.LessThan(0.001f),
                    "the plain offset ignores the entity's turn");
                Assert.That((followingEmitter.Origin - turnedOrigin).Length(), Is.LessThan(0.001f),
                    "the rotating offset turns with the entity, on top of the plain one");
                Assert.That(followingEmitter.Rotation.Theta, Is.EqualTo(fixedEmitter.Rotation.Theta).Within(0.001),
                    "and the emitter's own frame stays as it was");
            });
        });
    }
}
