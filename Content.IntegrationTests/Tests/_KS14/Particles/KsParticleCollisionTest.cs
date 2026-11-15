#nullable enable
using System.Diagnostics;
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
///     Opt-in particle collision (<see cref="KsParticlesPrototype.Collision"/>): client-side emitters fired at a ring
///         of server-spawned, anchored test walls, stepped through <see cref="KsParticlesSystem.FrameUpdate"/>, with
///         every live particle checked in the grid's frame after every frame.
/// </summary>
[TestFixture]
[TestOf(typeof(KsParticlesSystem))]
public sealed class KsParticleCollisionTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private const float Dt = 1f / 20f;

    /// <summary>
    ///     The room's walls are the tiles at -3 and 3 on each axis, so its inside is [-2, 3) on both: the east wall's
    ///         face is at x = 3.
    /// </summary>
    private const int RoomHalf = 3;
    private const float EastFace = RoomHalf;
    private const float Radius = 0.1f;

    /// <summary>The emitter sits in the middle of tile (0, 0).</summary>
    private static readonly Vector2 EmitterPosition = new(0.5f, 0.5f);

    private readonly EntProtoId _wall = "KsParticleCollisionTestWall";
    private readonly ProtoId<KsParticlesPrototype> _bounceWorld = "KsParticleCollisionTestBounceWorld";
    private readonly ProtoId<KsParticlesPrototype> _bounceLocal = "KsParticleCollisionTestBounceLocal";
    private readonly ProtoId<KsParticlesPrototype> _passThrough = "KsParticleCollisionTestPassThrough";
    private readonly ProtoId<KsParticlesPrototype> _die = "KsParticleCollisionTestDie";
    private readonly ProtoId<KsParticlesPrototype> _stick = "KsParticleCollisionTestStick";
    private readonly ProtoId<KsParticlesPrototype> _swarm = "KsParticleCollisionTestSwarm";
    private readonly ProtoId<KsParticlesPrototype> _swarmPlain = "KsParticleCollisionTestSwarmPlain";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsParticleCollisionTestWall
  components:
  - type: Transform
    anchored: true
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeAabb
          bounds: ""-0.5,-0.5,0.5,0.5""
        layer:
        - WallLayer
        hard: true

- type: ksParticles
  id: KsParticleCollisionTestBounceWorld
  count: 20
  burst: 20
  spawning: 0
  lifespan: 0
  space: World
  velocity: [ 6, 0 ]
  position: { kind: box, a: [ 0, -1 ], b: [ 0, 1 ] }
  tickRate: 20
  collision:
    bounce: 0.8
    friction: 0
    radius: 0.1

- type: ksParticles
  parent: KsParticleCollisionTestBounceWorld
  id: KsParticleCollisionTestBounceLocal
  space: Local

- type: ksParticles
  parent: KsParticleCollisionTestBounceWorld
  id: KsParticleCollisionTestPassThrough
  collision:
    enabled: false

- type: ksParticles
  parent: KsParticleCollisionTestBounceWorld
  id: KsParticleCollisionTestDie
  collision:
    onHit: Die
    radius: 0.1

- type: ksParticles
  parent: KsParticleCollisionTestBounceWorld
  id: KsParticleCollisionTestStick
  # Pushed on into the wall after sticking, which it must ignore.
  gravity: 3, 0
  collision:
    onHit: Stick
    radius: 0.1

- type: ksParticles
  id: KsParticleCollisionTestSwarm
  count: 500
  burst: 500
  spawning: 0
  lifespan: 0
  space: World
  velocity: { kind: circle, a: 2, b: 6 }
  drift: { kind: circle, a: 0, b: 0.5 }
  gravity: 0, -3
  tickRate: 20
  collision:
    bounce: 1
    friction: 0
    radius: 0.1

- type: ksParticles
  parent: KsParticleCollisionTestSwarm
  id: KsParticleCollisionTestSwarmPlain
  collision:
    enabled: false
";

    private KsParticlesSystem ClientSystem => CEntMan.System<KsParticlesSystem>();

    /// <summary>
    ///     A test map whose grid, turned by <paramref name="gridRotation"/>, holds a floored 7 × 7 room walled on
    ///         every side, with the client's copy up to date.
    /// </summary>
    private async Task<EntityUid> SetUpRoomAsync(Angle gridRotation)
    {
        var testMap = await Pair.CreateTestMap();
        await Server.WaitPost(() =>
        {
            var mapSystem = SEntMan.System<SharedMapSystem>();
            var transformSystem = SEntMan.System<SharedTransformSystem>();
            transformSystem.SetLocalRotation(testMap.Grid.Owner, gridRotation);

            for (var x = -RoomHalf; x <= RoomHalf; x++)
            {
                for (var y = -RoomHalf; y <= RoomHalf; y++)
                    mapSystem.SetTile(testMap.Grid, new Vector2i(x, y), testMap.Tile.Tile);
            }

            for (var x = -RoomHalf; x <= RoomHalf; x++)
            {
                for (var y = -RoomHalf; y <= RoomHalf; y++)
                {
                    if (Math.Abs(x) != RoomHalf && Math.Abs(y) != RoomHalf)
                        continue;

                    SEntMan.SpawnAttachedTo(_wall, new EntityCoordinates(testMap.Grid.Owner, x + 0.5f, y + 0.5f));
                }
            }
        });

        await RunTicksSync(10);
        return testMap.CGridUid;
    }

    private Entity<KsParticlesComponent, KsParticleEmitterComponent> SpawnEmitter(ProtoId<KsParticlesPrototype> prototype, EntityUid clientGridUid, Vector2? position = null)
    {
        var uid = ClientSystem.SpawnLocal(prototype, new EntityCoordinates(clientGridUid, position ?? EmitterPosition))!.Value;
        return (uid, CEntMan.GetComponent<KsParticlesComponent>(uid), CEntMan.GetComponent<KsParticleEmitterComponent>(uid));
    }

    /// <summary>
    ///     Particle <paramref name="index"/>'s position in the grid's frame, from either space.
    /// </summary>
    private Vector2 GridPosition(KsParticleEmitterComponent emitterComponent, int index, EntityUid clientGridUid)
    {
        var particle = emitterComponent.Particles[index];
        var position = new Vector2(particle.Position.X, particle.Position.Y);
        if (emitterComponent.Prototype!.Space == KsParticleSpace.Local)
            position = Vector2.Transform(position, emitterComponent.Matrix);

        var transformSystem = CEntMan.System<SharedTransformSystem>();
        return Vector2.Transform(position, transformSystem.GetInvWorldMatrix(clientGridUid));
    }

    /// <summary>
    ///     Particle <paramref name="index"/>'s velocity in the grid's frame.
    /// </summary>
    private Vector2 GridVelocity(KsParticleEmitterComponent emitterComponent, int index, EntityUid clientGridUid)
    {
        var particle = emitterComponent.Particles[index];
        var velocity = new Vector2(particle.Velocity.X, particle.Velocity.Y);
        if (emitterComponent.Prototype!.Space == KsParticleSpace.Local)
            velocity = Vector2.TransformNormal(velocity, emitterComponent.Matrix);

        var transformSystem = CEntMan.System<SharedTransformSystem>();
        return Vector2.TransformNormal(velocity, transformSystem.GetInvWorldMatrix(clientGridUid));
    }

    /// <summary>
    ///     Steps a bouncing emitter until every particle has hit the east wall, checking after every frame that none
    ///         is at, in or past it, then that they came back off it at the bounce's speed.
    /// </summary>
    private async Task AssertBouncesOffTheEastWall(ProtoId<KsParticlesPrototype> prototype, Angle gridRotation)
    {
        var clientGridUid = await SetUpRoomAsync(gridRotation);

        await Client.WaitAssertion(() =>
        {
            var emitter = SpawnEmitter(prototype, clientGridUid);
            var collision = emitter.Comp2.Prototype!.Collision!;
            var launchSpeed = 6f;
            var reached = false;
            var allowedEast = EastFace - collision.Radius + 1e-3f;

            // 2.5 m to the wall at 6 m/s: well inside 40 frames, and enough after for a second bounce off the west.
            for (var frame = 0; frame < 40; frame++)
            {
                ClientSystem.FrameUpdate(Dt);
                Assert.That(emitter.Comp2.Alive, Is.EqualTo(20), $"frame {frame}: a bouncing particle never dies");

                for (var i = 0; i < emitter.Comp2.Alive; i++)
                {
                    var position = GridPosition(emitter.Comp2, i, clientGridUid);
                    Assert.That(position.X, Is.LessThanOrEqualTo(allowedEast),
                        $"frame {frame} particle {i} at {position}: at, inside or past the east wall");
                    Assert.That(position.X, Is.GreaterThanOrEqualTo(-(RoomHalf - 1) + collision.Radius - 1e-3f),
                        $"frame {frame} particle {i} at {position}: at, inside or past the west wall");
                }

                if (frame == 12)
                {
                    // By now (0.6 s, 3.6 m of travel) every particle has met the east wall once and is on its way back.
                    reached = true;
                    for (var i = 0; i < emitter.Comp2.Alive; i++)
                    {
                        var velocity = GridVelocity(emitter.Comp2, i, clientGridUid);
                        Assert.That(velocity.X, Is.EqualTo(-launchSpeed * collision.Bounce).Within(1e-2f),
                            $"particle {i}: bounced back at the bounce's fraction of its speed");
                        Assert.That(velocity.Y, Is.EqualTo(0f).Within(1e-3f), $"particle {i}: a square hit stays square");
                    }
                }
            }

            Assert.That(reached);
        });
    }

    [Test]
    public async Task WorldParticlesBounceOffAWall()
    {
        await AssertBouncesOffTheEastWall(_bounceWorld, Angle.Zero);
    }

    /// <summary>
    ///     Local particles, on a turned grid: the emitter is square to the grid, so its +x is the grid's, and the walls
    ///         are axis-aligned only in the grid's frame.
    /// </summary>
    [Test]
    public async Task LocalParticlesBounceOffAWallOnATurnedGrid()
    {
        await AssertBouncesOffTheEastWall(_bounceLocal, Angle.FromDegrees(37));
    }

    [Test]
    public async Task WorldParticlesBounceOffAWallOnATurnedGrid()
    {
        await AssertBouncesOffTheEastWall(_bounceWorld, Angle.FromDegrees(-121));
    }

    /// <summary>
    ///     Without collision, the same particles fly straight through: it is opt-in, and costs nothing when off.
    /// </summary>
    [Test]
    public async Task ParticlesWithoutCollisionPassThrough()
    {
        var clientGridUid = await SetUpRoomAsync(Angle.Zero);

        await Client.WaitAssertion(() =>
        {
            var emitter = SpawnEmitter(_passThrough, clientGridUid);
            Assert.That(emitter.Comp2.Collision, Is.Null, "a disabled collision keeps no walls");

            for (var frame = 0; frame < 20; frame++)
                ClientSystem.FrameUpdate(Dt);

            Assert.That(emitter.Comp2.Alive, Is.EqualTo(20));
            for (var i = 0; i < emitter.Comp2.Alive; i++)
                Assert.That(GridPosition(emitter.Comp2, i, clientGridUid).X, Is.GreaterThan(EastFace + 1f), $"particle {i} went through the wall");
        });
    }

    /// <summary>
    ///     Spawned 0.2 m deep in the east wall, particles come out of its nearer face, the west one, and bounce off it
    ///         on the way.
    /// </summary>
    [Test]
    public async Task ParticlesSpawnedInAWallArePushedOut()
    {
        var clientGridUid = await SetUpRoomAsync(Angle.Zero);

        await Client.WaitAssertion(() =>
        {
            var emitter = SpawnEmitter(_bounceWorld, clientGridUid, new Vector2(EastFace + 0.2f, 0.5f));
            var collision = emitter.Comp2.Prototype!.Collision!;
            for (var frame = 0; frame < 10; frame++)
            {
                ClientSystem.FrameUpdate(Dt);
                Assert.That(emitter.Comp2.Alive, Is.EqualTo(20), $"frame {frame}");
                for (var i = 0; i < emitter.Comp2.Alive; i++)
                {
                    var position = GridPosition(emitter.Comp2, i, clientGridUid);
                    Assert.That(position.X, Is.LessThanOrEqualTo(EastFace - collision.Radius + 1e-3f), $"frame {frame} particle {i} at {position}");
                    Assert.That(GridVelocity(emitter.Comp2, i, clientGridUid).X, Is.LessThan(0f), $"frame {frame} particle {i}: sent back out");
                }
            }
        });
    }

    [Test]
    public async Task DieKillsOnHit()
    {
        var clientGridUid = await SetUpRoomAsync(Angle.Zero);

        await Client.WaitAssertion(() =>
        {
            var emitter = SpawnEmitter(_die, clientGridUid);
            ClientSystem.FrameUpdate(Dt);
            Assert.That(emitter.Comp2.Alive, Is.EqualTo(20), "nothing has hit yet");

            for (var frame = 0; frame < 20; frame++)
            {
                ClientSystem.FrameUpdate(Dt);
                for (var i = 0; i < emitter.Comp2.Alive; i++)
                    Assert.That(GridPosition(emitter.Comp2, i, clientGridUid).X, Is.LessThan(EastFace), $"frame {frame} particle {i}");
            }

            Assert.That(emitter.Comp2.Alive, Is.Zero, "every particle died on the wall");
        });
    }

    [Test]
    public async Task StickHoldsOnTheWall()
    {
        var clientGridUid = await SetUpRoomAsync(Angle.Zero);

        await Client.WaitAssertion(() =>
        {
            var emitter = SpawnEmitter(_stick, clientGridUid);
            var radius = emitter.Comp2.Prototype!.Collision!.Radius;
            for (var frame = 0; frame < 30; frame++)
                ClientSystem.FrameUpdate(Dt);

            Assert.That(emitter.Comp2.Alive, Is.EqualTo(20));
            for (var i = 0; i < emitter.Comp2.Alive; i++)
            {
                var particle = emitter.Comp2.Particles[i];
                Assert.Multiple(() =>
                {
                    Assert.That(particle.Stuck, $"particle {i} stuck");
                    // The wall's own shape is a little larger than its tile, by the physics polygon skin.
                    Assert.That(GridPosition(emitter.Comp2, i, clientGridUid).X, Is.InRange(EastFace - radius - 0.03f, EastFace - radius + 1e-3f), $"particle {i} on the wall's face");
                    Assert.That(particle.Velocity.Length(), Is.Zero, $"particle {i} still");
                });
            }
        });
    }

    /// <summary>
    ///     Five hundred particles bouncing about the room in every direction, under gravity and drift: none ever
    ///         leaves it, and once warm a step allocates nothing. Also prints what a step costs, with and without.
    /// </summary>
    [Test]
    public async Task ASwarmStaysInsideAndAllocatesNothing()
    {
        var clientGridUid = await SetUpRoomAsync(Angle.FromDegrees(15));

        await Client.WaitAssertion(() =>
        {
            var colliding = SpawnEmitter(_swarm, clientGridUid);
            var plain = SpawnEmitter(_swarmPlain, clientGridUid);
            var radius = colliding.Comp2.Prototype!.Collision!.Radius;
            const float inside = RoomHalf - 1;

            // Warm: the burst, the arrays grown, the walls gathered.
            for (var frame = 0; frame < 5; frame++)
                ClientSystem.FrameUpdate(Dt);

            Assert.That(colliding.Comp2.Alive, Is.EqualTo(500));
            Assert.That(colliding.Comp2.Collision!.BoxCount, Is.GreaterThan(0), "the room's walls were gathered");

            // Every particle, every step, inside the room.
            const int steps = 200;
            var collidingPrototype = colliding.Comp2.Prototype!;
            var plainPrototype = plain.Comp2.Prototype!;
            for (var step = 0; step < steps; step++)
            {
                ClientSystem.Step(colliding.Comp1, colliding.Comp2, collidingPrototype, Dt);
                for (var i = 0; i < colliding.Comp2.Alive; i++)
                {
                    var position = GridPosition(colliding.Comp2, i, clientGridUid);
                    Assert.That(position.X > -inside && position.X < EastFace && position.Y > -inside && position.Y < EastFace,
                        $"step {step} particle {i} at {position} left the room");
                }
            }

            Assert.That(colliding.Comp2.Alive, Is.EqualTo(500));

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var step = 0; step < steps; step++)
                ClientSystem.Step(colliding.Comp1, colliding.Comp2, collidingPrototype, Dt);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            // Timing is printed, never asserted: the best of a few alternating rounds, so the JIT and the other
            //      pooled instances count for as little as possible.
            var stopwatch = new Stopwatch();
            var collidingBest = double.MaxValue;
            var plainBest = double.MaxValue;
            for (var round = 0; round < 5; round++)
            {
                stopwatch.Restart();
                for (var step = 0; step < steps; step++)
                    ClientSystem.Step(colliding.Comp1, colliding.Comp2, collidingPrototype, Dt);
                collidingBest = Math.Min(collidingBest, stopwatch.Elapsed.TotalMicroseconds / steps);

                stopwatch.Restart();
                for (var step = 0; step < steps; step++)
                    ClientSystem.Step(plain.Comp1, plain.Comp2, plainPrototype, Dt);
                plainBest = Math.Min(plainBest, stopwatch.Elapsed.TotalMicroseconds / steps);
            }

            TestContext.Out.WriteLine(
                $"{colliding.Comp2.Alive} particles, {colliding.Comp2.Collision.BoxCount} wall boxes: {collidingBest:F1} µs per step with collision, " +
                $"{plainBest:F1} µs without; {allocated} bytes allocated over {steps} warm steps");

            Assert.That(allocated, Is.Zero, "a warm colliding step, gathers included, allocates nothing");
            Assert.That(radius, Is.GreaterThan(0f));
        });
    }
}
