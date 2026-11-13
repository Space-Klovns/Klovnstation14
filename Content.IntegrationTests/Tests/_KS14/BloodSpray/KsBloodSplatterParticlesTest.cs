#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.BloodSpray;
using Content.Shared._KS14.Particles;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Gibbing;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;

namespace Content.IntegrationTests.Tests._KS14.BloodSpray;

/// <summary>
///     The TGMC blood particles: a hit's spray goes out through the exit wound in the blood's colour and cleans up
///         after itself, and gibbing leaves a burst of gore behind.
/// </summary>
[TestFixture]
[TestOf(typeof(BloodSpraySystem))]
public sealed class KsBloodSplatterParticlesTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private readonly EntProtoId _mob = "KsBloodSplatterTestMob";

    private static readonly ProtoId<KsParticlesPrototype> SplatterMist = "KsBloodSplatterMist";
    private static readonly ProtoId<KsParticlesPrototype> SplatterSprite = "KsBloodSplatterSprite";
    private static readonly ProtoId<KsParticlesPrototype> GibExplosion = "KsBloodExplosion";
    private static readonly ProtoId<KsParticlesPrototype> GibSplatter = "KsGibSplatter";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsBloodSplatterTestMob
  components:
  - type: SolutionContainerManager
  - type: Bloodstream
    bloodlossDamage:
      types:
        Bloodloss: 1
    bloodlossHealDamage:
      types:
        Bloodloss: -1
";

    private Dictionary<string, EntityUid> ServerEmitters()
    {
        var emitters = new Dictionary<string, EntityUid>();
        var query = SEntMan.EntityQueryEnumerator<KsParticlesComponent>();
        while (query.MoveNext(out var uid, out var particlesComponent))
        {
            if (particlesComponent.Prototype is { } prototype)
                emitters[prototype.Id] = uid;
        }

        return emitters;
    }

    private Color BloodColor(EntityUid mobUid)
    {
        var bloodstreamComponent = SEntMan.GetComponent<BloodstreamComponent>(mobUid);
        var solutionSystem = SEntMan.System<SharedSolutionContainerSystem>();
        Assert.That(solutionSystem.ResolveSolution(mobUid, bloodstreamComponent.BloodSolutionName, ref bloodstreamComponent.BloodSolution, out var solution));
        return solution!.GetColor(Server.ProtoMan);
    }

    [Test]
    public async Task SprayGoesOutThroughTheExitWoundAndCleansUp()
    {
        var testMap = await Pair.CreateTestMap();
        var exitDirection = new Vector2(1f, 0f);

        EntityUid mobUid = default;
        Dictionary<string, EntityUid> emitters = new();
        await Server.WaitAssertion(() =>
        {
            mobUid = SEntMan.SpawnEntity(_mob, new MapCoordinates(Vector2.Zero, testMap.MapId));
            var bloodstreamComponent = SEntMan.GetComponent<BloodstreamComponent>(mobUid);

            SEntMan.System<BloodSpraySystem>().HandleBleedEffects((mobUid, bloodstreamComponent), 20f, exitDirection);
            emitters = ServerEmitters();

            var bloodColor = BloodColor(mobUid);
            var transformSystem = SEntMan.System<SharedTransformSystem>();
            Assert.Multiple(() =>
            {
                foreach (var prototype in new[] { SplatterMist, SplatterSprite })
                {
                    Assert.That(emitters.TryGetValue(prototype.Id, out var emitterUid), $"{prototype} spawned");
                    Assert.That(SEntMan.GetComponent<KsParticlesComponent>(emitterUid).Color, Is.EqualTo(bloodColor), $"{prototype} is the blood's colour");

                    // The prototypes fly along their emitter's -y; the emitter is square to its grid, then turned.
                    var gridRotation = SEntMan.GetComponent<TransformComponent>(emitterUid).GridUid is { } gridUid ? transformSystem.GetWorldRotation(gridUid) : Angle.Zero;
                    var emitterRotation = gridRotation + SEntMan.GetComponent<KsParticlesComponent>(emitterUid).Rotation;
                    var travel = emitterRotation.RotateVec(new Vector2(0f, -1f));
                    Assert.That(Vector2.Distance(travel, exitDirection), Is.LessThan(0.001f), $"{prototype} sprays out of the exit wound, not back at the attacker");
                    Assert.That(SEntMan.HasComponent<TimedDespawnComponent>(emitterUid), $"{prototype} is one-shot");
                }
            });
        });

        await RunTicksSync(5);
        await Client.WaitAssertion(() =>
        {
            foreach (var emitterUid in emitters.Values)
                Assert.That(CEntMan.EntityExists(ToClientUid(emitterUid)), "the client is sent the emitters");
        });

        await RunSeconds(2f);
        await Server.WaitAssertion(() =>
        {
            foreach (var (prototype, emitterUid) in emitters)
                Assert.That(SEntMan.EntityExists(emitterUid), Is.False, $"{prototype} deleted itself");
        });
    }

    [Test]
    public async Task NoSprayWithoutDecals()
    {
        var testMap = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var mobUid = SEntMan.SpawnEntity(_mob, new MapCoordinates(Vector2.Zero, testMap.MapId));
            var bloodstreamComponent = SEntMan.GetComponent<BloodstreamComponent>(mobUid);

            // Below BloodSpraySystem's threshold for decals, so below the particles' too.
            SEntMan.System<BloodSpraySystem>().HandleBleedEffects((mobUid, bloodstreamComponent), 1f, new Vector2(1f, 0f));
            Assert.That(ServerEmitters(), Is.Empty);
        });
    }

    [Test]
    public async Task GibbingLeavesAGoreBurst()
    {
        var testMap = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var mobUid = SEntMan.SpawnEntity(_mob, new MapCoordinates(new Vector2(2f, 3f), testMap.MapId));
            var bloodColor = BloodColor(mobUid);

            SEntMan.System<GibbingSystem>().Gib(mobUid);
            var emitters = ServerEmitters();

            var transformSystem = SEntMan.System<SharedTransformSystem>();
            Assert.Multiple(() =>
            {
                foreach (var prototype in new[] { GibExplosion, GibSplatter })
                {
                    Assert.That(emitters.TryGetValue(prototype.Id, out var emitterUid), $"{prototype} spawned");
                    Assert.That(SEntMan.GetComponent<KsParticlesComponent>(emitterUid).Color, Is.EqualTo(bloodColor), $"{prototype} is the blood's colour");
                    Assert.That(SEntMan.GetComponent<TransformComponent>(emitterUid).ParentUid, Is.Not.EqualTo(mobUid), $"{prototype} stays behind when the mob is deleted");
                    Assert.That(Vector2.Distance(transformSystem.GetWorldPosition(emitterUid), new Vector2(2f, 3f)), Is.LessThan(0.001f), $"{prototype} is where the mob was");
                }
            });
        });
    }
}
