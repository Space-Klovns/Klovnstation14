#nullable enable
using System.Linq;
using System.Numerics;
using Content.Client._KS14.Particles;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Particles;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._KS14.Particles;

/// <summary>
///     The client's simulation, on client-only emitters, with test prototypes that leave nothing to chance: counts,
///         motion, lifespans, bursts, emitting, spaces. The headless client draws nothing, so what is checked is the
///         state each step leaves in <see cref="KsParticleEmitterComponent"/>.
/// </summary>
[TestFixture]
[TestOf(typeof(KsParticlesSystem))]
public sealed class KsParticleEmitterTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private const float TickRate = 20f;
    private const float Dt = 1f / TickRate;

    private readonly ProtoId<KsParticlesPrototype> _linear = "KsParticlesTestLinear";
    private readonly ProtoId<KsParticlesPrototype> _flood = "KsParticlesTestFlood";
    private readonly ProtoId<KsParticlesPrototype> _burst = "KsParticlesTestBurst";
    private readonly ProtoId<KsParticlesPrototype> _shrink = "KsParticlesTestShrink";
    private readonly ProtoId<KsParticlesPrototype> _timed = "KsParticlesTestTimed";
    private readonly ProtoId<KsParticlesPrototype> _world = "KsParticlesTestWorld";
    private readonly ProtoId<KsParticlesPrototype> _bounded = "KsParticlesTestBounded";

    [TestPrototypes]
    private const string Prototypes = @"
- type: ksParticles
  id: KsParticlesTestLinear
  count: 10
  spawning: 20
  lifespan: 1
  velocity: [ 1, 0 ]
  gravity: 0, -2
  tickRate: 20

- type: ksParticles
  id: KsParticlesTestFlood
  count: 10
  spawning: 1000
  lifespan: 0
  tickRate: 20

- type: ksParticles
  id: KsParticlesTestBurst
  count: 20
  spawning: 0
  burst: 5
  lifespan: 0
  tickRate: 20

- type: ksParticles
  id: KsParticlesTestShrink
  count: 5
  spawning: 0
  burst: 1
  lifespan: 0
  scale: 0.1
  grow: -1
  tickRate: 20

- type: ksParticles
  id: KsParticlesTestTimed
  count: 100
  spawning: 100
  lifespan: 0.2
  emitDuration: 0.5
  tickRate: 20

- type: ksParticles
  id: KsParticlesTestWorld
  count: 5
  spawning: 0
  burst: 1
  lifespan: 0
  space: World
  tickRate: 20

- type: ksParticles
  id: KsParticlesTestBounded
  count: 5
  spawning: 0
  burst: 1
  lifespan: 0
  velocity: [ 1, 0 ]
  bounds: ""-1,-1,1,1""
  tickRate: 20
";

    private KsParticlesSystem ClientSystem => CEntMan.System<KsParticlesSystem>();

    /// <summary>
    ///     A client-only emitter on the client's copy of a fresh test map, before any step has run.
    /// </summary>
    private async Task<Entity<KsParticlesComponent, KsParticleEmitterComponent>> SpawnLocalAsync(ProtoId<KsParticlesPrototype> prototype, EntityUid? clientParentUid = null)
    {
        var testMap = await Pair.CreateTestMap();
        var parentUid = clientParentUid ?? testMap.CMapUid;

        Entity<KsParticlesComponent, KsParticleEmitterComponent> emitter = default;
        await Client.WaitPost(() =>
        {
            var uid = ClientSystem.SpawnLocal(prototype, new EntityCoordinates(parentUid, Vector2.Zero));
            Assert.That(uid, Is.Not.Null, "the client spawns a local emitter");
            emitter = (uid!.Value, CEntMan.GetComponent<KsParticlesComponent>(uid.Value), CEntMan.GetComponent<KsParticleEmitterComponent>(uid.Value));
        });

        Assert.That(emitter.Comp2!.Prototype, Is.Not.Null, "the emitter resolved its prototype on startup");
        Assert.That(CEntMan.IsClientSide(emitter.Owner), "a local emitter is a client-side entity");
        return emitter;
    }

    private void Steps(Entity<KsParticlesComponent, KsParticleEmitterComponent> emitter, int steps)
    {
        for (var i = 0; i < steps; i++)
            ClientSystem.Step(emitter.Comp1, emitter.Comp2, emitter.Comp2.Prototype!, Dt);
    }

    private static KsParticle Oldest(KsParticleEmitterComponent emitterComponent)
    {
        var oldest = 0;
        for (var i = 1; i < emitterComponent.Alive; i++)
        {
            if (emitterComponent.Particles[i].Age > emitterComponent.Particles[oldest].Age)
                oldest = i;
        }

        return emitterComponent.Particles[oldest];
    }

    /// <summary>
    ///     Semi-implicit Euler at a fixed step: velocity first, then position. After k steps of dt under gravity g,
    ///         y = g dt² k(k+1)/2, which is the closed form ½gt² plus g dt t / 2.
    /// </summary>
    [Test]
    public async Task ParticlesFollowVelocityAndGravity()
    {
        var emitter = await SpawnLocalAsync(_linear);
        const int steps = 10;

        await Client.WaitAssertion(() =>
        {
            Steps(emitter, steps);

            Assert.That(emitter.Comp2.Alive, Is.EqualTo(steps), "one spawn per step at 20 per second and 20 steps per second");

            var particle = Oldest(emitter.Comp2);
            var t = steps * Dt;
            Assert.Multiple(() =>
            {
                Assert.That(particle.Age, Is.EqualTo(t).Within(1e-4f));
                Assert.That(particle.Position.X, Is.EqualTo(1f * t).Within(1e-4f), "constant velocity in x");
                Assert.That(particle.Position.Y, Is.EqualTo(-2f * Dt * Dt * steps * (steps + 1) / 2f).Within(1e-4f), "the discrete integral of gravity");
                Assert.That(particle.Position.Y, Is.EqualTo(0.5f * -2f * t * t).Within(MathF.Abs(-2f * Dt * t / 2f) + 1e-4f), "within one step's error of ½gt²");
                Assert.That(particle.Velocity.Y, Is.EqualTo(-2f * t).Within(1e-4f));
                Assert.That(particle.PrevPosition.X, Is.EqualTo(1f * (t - Dt)).Within(1e-4f), "the previous step's position is kept for interpolation");
            });
        });
    }

    [Test]
    public async Task AliveNeverExceedsCountAndNoParticleOutlivesItsLifespan()
    {
        var flood = await SpawnLocalAsync(_flood);
        var linear = await SpawnLocalAsync(_linear);

        await Client.WaitAssertion(() =>
        {
            for (var step = 0; step < 60; step++)
            {
                Steps(flood, 1);
                Steps(linear, 1);
                Assert.That(flood.Comp2.Alive, Is.LessThanOrEqualTo(10), $"flood step {step}");
                for (var i = 0; i < linear.Comp2.Alive; i++)
                    Assert.That(linear.Comp2.Particles[i].Age, Is.LessThan(1f + 1e-4f), $"linear step {step} particle {i}");
            }

            Assert.That(flood.Comp2.Alive, Is.EqualTo(10), "a flood fills the emitter to its count");
            // At the cap, a step's spawn is refused and its death then frees a slot for the next step's spawn, so the
            //      emitter sits one below the cap on every other step.
            Assert.That(linear.Comp2.Alive, Is.InRange(9, 10), "after a lifespan, deaths and spawns balance at the cap");
        });
    }

    [Test]
    public async Task BurstSpawnsAtOnceAndShrinkingToNothingKills()
    {
        var burst = await SpawnLocalAsync(_burst);
        var shrink = await SpawnLocalAsync(_shrink);

        await Client.WaitAssertion(() =>
        {
            Steps(burst, 1);
            Assert.That(burst.Comp2.Alive, Is.EqualTo(5), "the burst spawns on the first step");
            Steps(burst, 10);
            Assert.That(burst.Comp2.Alive, Is.EqualTo(5), "no spawning after the burst, and an infinite lifespan");

            Steps(shrink, 1);
            Assert.That(shrink.Comp2.Alive, Is.EqualTo(1));
            Steps(shrink, 1);
            Assert.That(shrink.Comp2.Particles[0].Scale.X, Is.EqualTo(0.1f - 2f * Dt).Within(1e-4f));
            Steps(shrink, 1);
            Assert.That(shrink.Comp2.Alive, Is.EqualTo(0), "scale 0.1 shrinking at 1 per second is gone within 0.1 s");
        });
    }

    [Test]
    public async Task EmittingOffDrainsAndOnResumes()
    {
        var emitter = await SpawnLocalAsync(_linear);

        await Client.WaitAssertion(() =>
        {
            Steps(emitter, 5);
            Assert.That(emitter.Comp2.Alive, Is.EqualTo(5));

            ClientSystem.SetEmitting(emitter.Owner, false);
            Steps(emitter, 25);
            Assert.That(emitter.Comp2.Alive, Is.EqualTo(0), "nothing spawns while off, and the live ones die at their lifespan");

            ClientSystem.SetEmitting(emitter.Owner, true);
            Steps(emitter, 3);
            Assert.That(emitter.Comp2.Alive, Is.EqualTo(3), "spawning resumes at the same rate");
        });
    }

    [Test]
    public async Task EmitDurationStopsSpawning()
    {
        var emitter = await SpawnLocalAsync(_timed);

        await Client.WaitAssertion(() =>
        {
            // Three steps: 0.15 s, so nothing has reached the 0.2 s lifespan yet.
            Steps(emitter, 3);
            Assert.That(emitter.Comp2.Alive, Is.EqualTo(3 * 5), "100 per second is 5 per step");

            Steps(emitter, 21);
            Assert.That(emitter.Comp2.Age, Is.GreaterThan(0.5f));
            Assert.That(emitter.Comp2.Alive, Is.EqualTo(0), "0.5 s of emitting and 0.2 s lifespans: all gone by 1.2 s");
        });
    }

    [Test]
    public async Task BoundsKill()
    {
        var emitter = await SpawnLocalAsync(_bounded);

        await Client.WaitAssertion(() =>
        {
            Steps(emitter, 1);
            Assert.That(emitter.Comp2.Alive, Is.EqualTo(1));
            Steps(emitter, 18);
            Assert.That(emitter.Comp2.Alive, Is.EqualTo(1), "still inside x < 1 after 0.95 s at 1 m/s");
            Steps(emitter, 2);
            Assert.That(emitter.Comp2.Alive, Is.EqualTo(0), "past the bound, dead");
        });
    }

    /// <summary>
    ///     A local emitter's particles are in its frame, so moving the entity moves them; a world emitter's stay put.
    /// </summary>
    [Test]
    public async Task LocalParticlesFollowTheEmitterAndWorldParticlesStay()
    {
        var testMap = await Pair.CreateTestMap();
        EntityUid clientParentUid = default;
        // Away from the map origin, so a world particle at the emitter is told apart from one at the local origin.
        await Client.WaitPost(() => clientParentUid = CEntMan.SpawnEntity(null, new MapCoordinates(new Vector2(2f, 0f), testMap.MapId)));

        var local = await SpawnLocalAsync(_burst, clientParentUid);
        var world = await SpawnLocalAsync(_world, clientParentUid);

        await Client.WaitPost(() =>
        {
            // One frame places the emitters and runs their burst.
            ClientSystem.FrameUpdate(Dt);
            Assert.That(local.Comp2.Alive, Is.EqualTo(5));
            Assert.That(world.Comp2.Alive, Is.EqualTo(1));

            var worldParticleBefore = world.Comp2.Particles[0].Position;
            Assert.That(worldParticleBefore.X, Is.EqualTo(2f).Within(1e-4f), "a world particle is born at the emitter's map position");
            Box2 localBoundsBefore = local.Comp2.WorldBounds;

            CEntMan.System<SharedTransformSystem>().SetWorldPosition(clientParentUid, new Vector2(7f, 0f));
            ClientSystem.FrameUpdate(Dt);

            Assert.Multiple(() =>
            {
                Assert.That(local.Comp2.Origin.X, Is.EqualTo(7f).Within(1e-4f), "the local emitter moved with its parent");
                Assert.That(local.Comp2.Particles[0].Position.X, Is.EqualTo(0f).Within(1e-4f), "its particles are still at the origin of its frame");
                Box2 localBoundsAfter = local.Comp2.WorldBounds;
                Assert.That(localBoundsAfter.Center.X, Is.EqualTo(localBoundsBefore.Center.X + 5f).Within(1e-3f), "so they are drawn, and culled, 5 m over");

                Assert.That(world.Comp2.Origin.X, Is.EqualTo(7f).Within(1e-4f), "the world emitter moved too");
                Assert.That(world.Comp2.Particles[0].Position.X, Is.EqualTo(2f).Within(1e-4f), "but its particle stayed where it was emitted");
            });
        });
    }

    /// <summary>
    ///     What the overlays read: emitters with something alive, sorted by layer, each with a map and bounds.
    /// </summary>
    [Test]
    public async Task FrameUpdateListsLiveEmittersByLayer()
    {
        var emitter = await SpawnLocalAsync(_burst);

        await Client.WaitPost(() =>
        {
            ClientSystem.FrameUpdate(Dt);
            var layer = ClientSystem.Layers[(int)KsParticleLayer.Effects];
            Box2 worldBounds = emitter.Comp2.WorldBounds;

            Assert.Multiple(() =>
            {
                Assert.That(layer.Select(entry => entry.Uid), Does.Contain(emitter.Owner));
                Assert.That(ClientSystem.Layers[(int)KsParticleLayer.Floor].Select(entry => entry.Uid), Does.Not.Contain(emitter.Owner));
                Assert.That(emitter.Comp2.MapId, Is.Not.EqualTo(MapId.Nullspace));
                Assert.That(worldBounds.Contains(emitter.Comp2.Origin), "the bounds cover the emitter");
                Assert.That(ClientSystem.TotalAlive, Is.GreaterThanOrEqualTo(5));
            });
        });
    }

    [Test]
    public async Task DensityZeroSpawnsNothingAndDisablingListsNothing()
    {
        await OverrideCVar(Side.Client, KsCCVars.ParticlesDensity, 0f);
        var starved = await SpawnLocalAsync(_flood);

        await Client.WaitAssertion(() =>
        {
            Steps(starved, 5);
            Assert.That(starved.Comp2.Alive, Is.EqualTo(0), "at density 0 nothing spawns");
        });

        await OverrideCVar(Side.Client, KsCCVars.ParticlesDensity, 1f);
        var fed = await SpawnLocalAsync(_flood);

        await Client.WaitAssertion(() =>
        {
            Steps(fed, 5);
            Assert.That(fed.Comp2.Alive, Is.EqualTo(10), "at density 1 the flood fills up");
        });

        await OverrideCVar(Side.Client, KsCCVars.ParticlesEnabled, false);
        await Client.WaitAssertion(() =>
        {
            ClientSystem.FrameUpdate(Dt);
            Assert.That(ClientSystem.Layers.All(layer => layer.Count == 0), "disabled, nothing is listed for drawing");
        });
    }

    [Test]
    public async Task GlobalBudgetCapsSpawning()
    {
        await OverrideCVar(Side.Client, KsCCVars.ParticlesMaxAlive, 7);
        var emitter = await SpawnLocalAsync(_flood);

        await Client.WaitAssertion(() =>
        {
            // The budget is handed out at the start of each frame from the previous frame's total.
            ClientSystem.FrameUpdate(Dt);
            Assert.That(emitter.Comp2.Alive, Is.EqualTo(7), "the flood stops at the budget, not at its own count");
        });
    }
}
