#nullable enable
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Content.Client._KS14.Particles;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.BloodSpray;
using Content.Shared._KS14.Particles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Particles;

[TestFixture]
public sealed class KsParticleBurstBenchmark : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private readonly (ProtoId<KsParticlesPrototype> Prototype, float Amount)[] _effects =
    {
        ("KsBloodSplatterMist", 3f),
        ("KsBloodSplatterSprite", 1f),
        ("KsHeavyHitBloodExplosion", 0.15f),
        ("KsHeavyHitGibSplatter", 0.15f),
    };

    [Test, Explicit("Headless particle burst allocation and timing benchmark")]
    public async Task HeavyHitBurst()
    {
        var map = await Pair.CreateTestMap();
        const int samples = 500;
        await Server.WaitPost(() =>
        {
            var system = SEntMan.System<BloodSpraySystem>();
            var spawn = typeof(BloodSpraySystem).GetMethod("SpawnSplatterParticles", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate<Action<EntityUid, EntityCoordinates, Color, Vector2, float, float>>(system);
            var coordinates = new EntityCoordinates(map.Grid.Owner, Vector2.Zero);
            var times = new double[samples];
            long totalBytes = 0;
            var entities = new List<EntityUid>();
            for (var sample = -30; sample < samples; sample++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                spawn(map.Grid.Owner, coordinates, Color.Red, Vector2.UnitX, 3f, 45f);
                var elapsed = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
                var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                if (sample >= 0)
                {
                    times[sample] = elapsed;
                    totalBytes += allocated;
                }

                var emitters = SEntMan.AllEntityQueryEnumerator<KsParticlesComponent>();
                while (emitters.MoveNext(out var uid, out _))
                {
                    if (SEntMan.GetComponent<TransformComponent>(uid).ParentUid == map.Grid.Owner)
                        entities.Add(uid);
                }

                Assert.That(entities.Count, Is.EqualTo(_effects.Length));
                foreach (var uid in entities)
                    SEntMan.DeleteEntity(uid);
                entities.Clear();
            }

            Array.Sort(times);
            TestContext.Out.WriteLine($"Server heavy-hit emitters, spawn and configuration: {totalBytes / samples} bytes/hit; " +
                $"median {times[samples / 2]:F1} µs, p95 {times[samples * 95 / 100]:F1} µs.");
        });

        await Client.WaitPost(() =>
        {
            var system = CEntMan.System<KsParticlesSystem>();
            var coordinates = new EntityCoordinates(map.CMapUid, Vector2.Zero);
            var entities = new EntityUid[_effects.Length];
            var times = new double[samples];
            long totalBytes = 0;
            for (var sample = -30; sample < samples; sample++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                for (var i = 0; i < entities.Length; i++)
                {
                    var (prototype, amount) = _effects[i];
                    var uid = system.SpawnLocal(prototype, coordinates, once: true)!.Value;
                    entities[i] = uid;
                    system.SetAmount(uid, amount);
                }

                system.FrameUpdate(0.1f);
                var elapsed = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
                var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                if (sample >= 0)
                {
                    times[sample] = elapsed;
                    totalBytes += allocated;
                }

                foreach (var uid in entities)
                    CEntMan.DeleteEntity(uid);
            }

            Array.Sort(times);
            TestContext.Out.WriteLine($"Four local emitters, spawn and first frame: {totalBytes / samples} bytes/hit; " +
                $"median {times[samples / 2]:F1} µs, p95 {times[samples * 95 / 100]:F1} µs.");
        });
    }
}
