#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._KS14.Particles;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.BloodSpray;
using Content.Shared._KS14.Particles;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Decals;
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
    private readonly EntProtoId _damageableMob = "KsBloodSplatterTestDamageableMob";
    private static readonly ProtoId<DamageTypePrototype> PiercingDamage = "Piercing";

    private static readonly ProtoId<KsParticlesPrototype> SplatterMist = "KsBloodSplatterMist";
    private static readonly ProtoId<KsParticlesPrototype> SplatterSprite = "KsBloodSplatterSprite";
    private static readonly ProtoId<KsParticlesPrototype> GibExplosion = "KsBloodExplosion";
    private static readonly ProtoId<KsParticlesPrototype> GibSplatter = "KsGibSplatter";
    private static readonly ProtoId<KsParticlesPrototype> DismemberExplosion = "KsDismemberBloodExplosion";
    private static readonly ProtoId<KsParticlesPrototype> DismemberSplatter = "KsDismemberGibSplatter";
    private static readonly ProtoId<KsParticlesPrototype> HeavyHitExplosion = "KsHeavyHitBloodExplosion";
    private static readonly ProtoId<KsParticlesPrototype> HeavyHitSplatter = "KsHeavyHitGibSplatter";

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

- type: damageModifierSet
  id: KsBloodSplatterTestBleed
  coefficients:
    Piercing: 1

- type: entity
  parent: KsBloodSplatterTestMob
  id: KsBloodSplatterTestDamageableMob
  components:
  - type: Damageable
  - type: Injurable
  - type: Bloodstream
    damageBleedModifiers: KsBloodSplatterTestBleed
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

    private List<EntityUid> ClientEmitters(ProtoId<KsParticlesPrototype> prototype, bool clientSide)
    {
        var emitters = new List<EntityUid>();
        var query = CEntMan.EntityQueryEnumerator<KsParticlesComponent>();
        while (query.MoveNext(out var uid, out var particlesComponent))
        {
            if (particlesComponent.Prototype == prototype && CEntMan.IsClientSide(uid) == clientSide)
                emitters.Add(uid);
        }

        return emitters;
    }

    /// <summary>
    ///     Where an emitter sends its particles: its -y, turned by its grid and its own rotation.
    /// </summary>
    private Vector2 ServerTravel(EntityUid emitterUid)
    {
        var transformSystem = SEntMan.System<SharedTransformSystem>();
        var gridRotation = SEntMan.GetComponent<TransformComponent>(emitterUid).GridUid is { } gridUid ? transformSystem.GetWorldRotation(gridUid) : Angle.Zero;
        var emitterRotation = gridRotation + SEntMan.GetComponent<KsParticlesComponent>(emitterUid).Rotation;
        return emitterRotation.RotateVec(new Vector2(0f, -1f));
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
            Assert.Multiple(() =>
            {
                foreach (var prototype in new[] { SplatterMist, SplatterSprite })
                {
                    Assert.That(emitters.TryGetValue(prototype.Id, out var emitterUid), $"{prototype} spawned");
                    Assert.That(SEntMan.GetComponent<KsParticlesComponent>(emitterUid).Color, Is.EqualTo(bloodColor), $"{prototype} is the blood's colour");

                    Assert.That(Vector2.Distance(ServerTravel(emitterUid), exitDirection), Is.LessThan(0.001f), $"{prototype} sprays out of the exit wound, not back at the attacker");
                    Assert.That(SEntMan.HasComponent<TimedDespawnComponent>(emitterUid), $"{prototype} is one-shot");
                }
            });
        });

        await RunTicksSync(5);
        await Client.WaitAssertion(() =>
        {
            foreach (var emitterUid in emitters.Values)
            {
                var clientUid = ToClientUid(emitterUid);
                Assert.That(CEntMan.EntityExists(clientUid), "the client is sent the emitters");
                Assert.That(CEntMan.System<KsParticlesSystem>().IsHiddenHere(clientUid), Is.False, "and shows them: it predicted nothing");
            }
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

    /// <summary>
    ///     A client that predicts the hit (running the shared bleed on its own) sprays at once, once however often
    ///         the tick is re-predicted, and hides the server's copy when it arrives instead of showing it twice.
    /// </summary>
    [Test]
    public async Task PredictedSprayShowsAtOnceAndHidesTheServersCopy()
    {
        var testMap = await Pair.CreateTestMap();
        var exitDirection = new Vector2(0f, 1f);

        EntityUid mobUid = default;
        await Server.WaitPost(() => mobUid = SEntMan.SpawnEntity(_mob, new MapCoordinates(Vector2.Zero, testMap.MapId)));
        await RunTicksSync(5);

        var clientMobUid = ToClientUid(mobUid);
        await Client.WaitAssertion(() =>
        {
            var bloodstreamComponent = CEntMan.GetComponent<BloodstreamComponent>(clientMobUid);
            var bloodSpraySystem = CEntMan.System<BloodSpraySystem>();

            // Twice in one tick, as a re-prediction runs it again.
            bloodSpraySystem.HandleBleedEffects((clientMobUid, bloodstreamComponent), 20f, exitDirection);
            bloodSpraySystem.HandleBleedEffects((clientMobUid, bloodstreamComponent), 20f, exitDirection);

            Assert.That(ClientEmitters(SplatterMist, clientSide: true), Has.Count.EqualTo(1), "the client sprays its own at once, and only once");
            Assert.That(ClientEmitters(SplatterSprite, clientSide: true), Has.Count.EqualTo(1));
        });

        await Server.WaitPost(() =>
        {
            var bloodstreamComponent = SEntMan.GetComponent<BloodstreamComponent>(mobUid);
            SEntMan.System<BloodSpraySystem>().HandleBleedEffects((mobUid, bloodstreamComponent), 20f, exitDirection);
        });
        await RunTicksSync(5);

        await Client.WaitAssertion(() =>
        {
            var particlesSystem = CEntMan.System<KsParticlesSystem>();
            Assert.Multiple(() =>
            {
                foreach (var prototype in new[] { SplatterMist, SplatterSprite })
                {
                    var serverCopies = ClientEmitters(prototype, clientSide: false);
                    Assert.That(serverCopies, Has.Count.EqualTo(1), $"{prototype}: the server's copy arrived");
                    Assert.That(particlesSystem.IsHiddenHere(serverCopies[0]), $"{prototype}: and is hidden, as the client shows its own");
                    Assert.That(particlesSystem.IsHiddenHere(ClientEmitters(prototype, clientSide: true)[0]), Is.False, $"{prototype}: the client's own still shows");
                }
            });
        });
    }

    /// <summary>
    ///     Every pellet of a shotgun's shot hits on one tick. A client's kept copies of them spray once each, and each of
    ///         the server's sprays is hidden when it arrives: the hit does not spray again a round trip later.
    /// </summary>
    [Test]
    public async Task EveryKeptHitOfOneTickHidesAServersCopy()
    {
        const int hits = 3;

        var testMap = await Pair.CreateTestMap();
        var exitDirection = new Vector2(0f, 1f);

        EntityUid mobUid = default;
        await Server.WaitPost(() => mobUid = SEntMan.SpawnEntity(_mob, new MapCoordinates(Vector2.Zero, testMap.MapId)));
        await RunTicksSync(5);

        var clientMobUid = ToClientUid(mobUid);
        await Client.WaitAssertion(() =>
        {
            var bloodstreamComponent = CEntMan.GetComponent<BloodstreamComponent>(clientMobUid);
            var bloodSpraySystem = CEntMan.System<BloodSpraySystem>();

            for (var hit = 0; hit < hits; hit++)
            {
                using (bloodSpraySystem.BeginHit(seed: hit, kept: true))
                    bloodSpraySystem.HandleBleedEffects((clientMobUid, bloodstreamComponent), 20f, exitDirection);
            }

            Assert.That(ClientEmitters(SplatterMist, clientSide: true), Has.Count.EqualTo(hits), "not every kept hit of the tick sprayed");
        });

        await Server.WaitPost(() =>
        {
            var bloodstreamComponent = SEntMan.GetComponent<BloodstreamComponent>(mobUid);
            for (var hit = 0; hit < hits; hit++)
            {
                SEntMan.System<BloodSpraySystem>().HandleBleedEffects((mobUid, bloodstreamComponent), 20f, exitDirection);
            }
        });
        await RunTicksSync(5);

        await Client.WaitAssertion(() =>
        {
            var particlesSystem = CEntMan.System<KsParticlesSystem>();
            Assert.Multiple(() =>
            {
                foreach (var prototype in new[] { SplatterMist, SplatterSprite })
                {
                    var serverCopies = ClientEmitters(prototype, clientSide: false);
                    Assert.That(serverCopies, Has.Count.EqualTo(hits), $"precondition: {prototype}: the server's copies arrived");
                    Assert.That(serverCopies.Count(particlesSystem.IsHiddenHere), Is.EqualTo(hits), $"{prototype}: a server's copy is shown beside the client's own");
                }
            });
        });
    }

    /// <summary>
    ///     A lost limb's gore is thrown after the limb, faster for a harder throw, within limits.
    /// </summary>
    [Test]
    public async Task DismemberGoreFollowsTheThrow()
    {
        var testMap = await Pair.CreateTestMap();
        var throwDirection = new Vector2(-1f, 0f);

        await Server.WaitAssertion(() =>
        {
            var bloodSpraySystem = SEntMan.System<BloodSpraySystem>();
            var bloodColor = Color.White;

            (float ThrowSpeed, float ExpectedSpeed)[] cases =
            [
                (BloodSpraySystem.DismemberReferenceThrowSpeed, 1f),
                (BloodSpraySystem.DismemberReferenceThrowSpeed * 100f, BloodSpraySystem.MaxDismemberSpeedMultiplier),
                (BloodSpraySystem.DismemberReferenceThrowSpeed * 0.01f, BloodSpraySystem.MinDismemberSpeedMultiplier),
            ];

            Assert.Multiple(() =>
            {
                foreach (var (throwSpeed, expectedSpeed) in cases)
                {
                    // A mob of its own each time: one tick's spawns on one mob are one effect.
                    var mobUid = SEntMan.SpawnEntity(_mob, new MapCoordinates(Vector2.Zero, testMap.MapId));
                    bloodColor = BloodColor(mobUid);
                    bloodSpraySystem.SpawnDismemberParticles(mobUid, throwDirection, throwSpeed);

                    var emitters = ServerEmitters();
                    foreach (var prototype in new[] { DismemberExplosion, DismemberSplatter })
                    {
                        Assert.That(emitters.TryGetValue(prototype.Id, out var emitterUid), $"{prototype} spawned");
                        var particlesComponent = SEntMan.GetComponent<KsParticlesComponent>(emitterUid);
                        Assert.That(particlesComponent.Color, Is.EqualTo(bloodColor), $"{prototype} is the blood's colour");
                        Assert.That(Vector2.Distance(ServerTravel(emitterUid), throwDirection), Is.LessThan(0.001f), $"{prototype} flies after the limb");
                        Assert.That(particlesComponent.Speed, Is.EqualTo(expectedSpeed).Within(0.001f), $"{prototype} at throw speed {throwSpeed}");
                        SEntMan.DeleteEntity(emitterUid);
                    }
                }
            });
        });
    }

    private List<Vector2> ClientDecalPositions()
    {
        var positions = new List<Vector2>();
        var query = CEntMan.AllEntityQueryEnumerator<DecalChunkComponent>();
        while (query.MoveNext(out var chunkComponent))
        {
            foreach (var decal in chunkComponent.Decals.Values)
                positions.Add(decal.Coordinates);
        }

        return positions;
    }

    /// <summary>
    ///     A client re-predicts a hit's tick after each server state, with a moving target wherever that state put
    ///         it. The decals of every re-prediction land where the first prediction put them, not on the target.
    /// </summary>
    [Test]
    public async Task RepredictedHitOnAMovedTargetBleedsWhereItWasHit()
    {
        var testMap = await Pair.CreateTestMap();
        var exitDirection = new Vector2(1f, 0f);
        var moved = new Vector2(0f, 2f);

        EntityUid mobUid = default;
        await Server.WaitPost(() =>
        {
            // Floor all round, so every decal of the spray has a tile to land on, wherever it starts.
            var mapSystem = SEntMan.System<SharedMapSystem>();
            for (var x = -6; x <= 6; x++)
            {
                for (var y = -6; y <= 6; y++)
                    mapSystem.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, y), testMap.Tile.Tile);
            }

            mobUid = SEntMan.SpawnEntity(_mob, new EntityCoordinates(testMap.Grid.Owner, new Vector2(0.5f, 0.5f)));
        });
        await RunTicksSync(5);

        var clientMobUid = ToClientUid(mobUid);
        await Client.WaitAssertion(() =>
        {
            var bloodstreamComponent = CEntMan.GetComponent<BloodstreamComponent>(clientMobUid);
            var bloodSpraySystem = CEntMan.System<BloodSpraySystem>();
            var transformSystem = CEntMan.System<SharedTransformSystem>();

            bloodSpraySystem.HandleBleedEffects((clientMobUid, bloodstreamComponent), 20f, exitDirection);
            var firstPrediction = ClientDecalPositions();
            Assert.That(firstPrediction, Is.Not.Empty, "the client predicts the decals");

            // The same tick again, as a re-prediction runs it, with the target moved on by a newer state.
            var clientTransform = CEntMan.GetComponent<TransformComponent>(clientMobUid);
            transformSystem.SetLocalPosition(clientMobUid, clientTransform.LocalPosition + moved, clientTransform);
            bloodSpraySystem.HandleBleedEffects((clientMobUid, bloodstreamComponent), 20f, exitDirection);

            foreach (var position in ClientDecalPositions())
            {
                Assert.That(firstPrediction.Exists(firstPosition => Vector2.Distance(firstPosition, position) < 0.001f),
                    $"a re-predicted decal at {position} is not where the first prediction put one: it followed the target");
            }
        });
    }

    /// <summary>
    ///     The client's state reset raises collisions, so a kept projectile's predicted hit can land while a state is
    ///         being applied. Its damage still sprays blood there at once: only a state's own damage, which has no
    ///         origin, is left to the server.
    /// </summary>
    [Test]
    public async Task HitPredictedDuringTheStateResetStillSprays()
    {
        var testMap = await Pair.CreateTestMap();

        EntityUid mobUid = default, shooterUid = default;
        await Server.WaitPost(() =>
        {
            mobUid = SEntMan.SpawnEntity(_damageableMob, new MapCoordinates(Vector2.Zero, testMap.MapId));
            shooterUid = SEntMan.SpawnEntity(null, new MapCoordinates(new Vector2(-2f, 0f), testMap.MapId));
        });
        await RunTicksSync(5);

        var clientMobUid = ToClientUid(mobUid);
        var clientShooterUid = ToClientUid(shooterUid);
        await Client.WaitAssertion(() =>
        {
            var damageableSystem = CEntMan.System<DamageableSystem>();
            var damage = new DamageSpecifier(Client.ProtoMan.Index(PiercingDamage), 20);

            using (Client.ResolveDependency<Robust.Client.Timing.IClientGameTiming>().StartStateApplicationArea())
                damageableSystem.TryChangeDamage(clientMobUid, damage, origin: clientShooterUid);

            Assert.That(ClientEmitters(SplatterMist, clientSide: true), Has.Count.EqualTo(1), "the hit sprays at once, state reset or not");
            Assert.That(ClientEmitters(SplatterSprite, clientSide: true), Has.Count.EqualTo(1));
        });
    }

    /// <summary>
    ///     A heavier hit sprays more mist and a bigger splatter: the reference damage sprays the prototypes as they are.
    /// </summary>
    [Test]
    public async Task SprayGrowsWithTheDamage()
    {
        var testMap = await Pair.CreateTestMap();

        (float Damage, float ExpectedAmount)[] cases =
        [
            (BloodSpraySystem.ReferenceSprayDamage, 1f),
            (BloodSpraySystem.ReferenceSprayDamage * 2f, 2f),
            (BloodSpraySystem.ReferenceSprayDamage * 100f, BloodSpraySystem.MaxSprayAmount),
        ];

        await Server.WaitAssertion(() =>
        {
            var bloodSpraySystem = SEntMan.System<BloodSpraySystem>();
            Assert.Multiple(() =>
            {
                foreach (var (damage, expectedAmount) in cases)
                {
                    var mobUid = SEntMan.SpawnEntity(_mob, new MapCoordinates(Vector2.Zero, testMap.MapId));
                    var bloodstreamComponent = SEntMan.GetComponent<BloodstreamComponent>(mobUid);
                    bloodSpraySystem.HandleBleedEffects((mobUid, bloodstreamComponent), 20f, new Vector2(1f, 0f), damage: damage);

                    var emitters = ServerEmitters();
                    var mist = SEntMan.GetComponent<KsParticlesComponent>(emitters[SplatterMist.Id]);
                    var splatter = SEntMan.GetComponent<KsParticlesComponent>(emitters[SplatterSprite.Id]);
                    Assert.That(mist.Amount, Is.EqualTo(expectedAmount).Within(0.001f), $"mist at {damage} damage");
                    Assert.That(splatter.Scale, Is.EqualTo(MathF.Sqrt(expectedAmount)).Within(0.001f), $"splatter at {damage} damage");

                    foreach (var emitterUid in emitters.Values)
                        SEntMan.DeleteEntity(emitterUid);
                    SEntMan.DeleteEntity(mobUid);
                }
            });
        });
    }

    /// <summary>
    ///     A heavy hit punches out more: its mist flies faster, and the exit wound throws a share of a lost limb's gore,
    ///         more the harder the hit, up to a cap. A lighter hit throws none.
    /// </summary>
    [Test]
    public async Task HeavyHitsPunchOutGore()
    {
        var testMap = await Pair.CreateTestMap();

        float[] damages =
        [
            BloodSpraySystem.HeavyHitDamage - 1f,
            BloodSpraySystem.HeavyHitDamage,
            BloodSpraySystem.HeavyHitDamage * 2f,
            BloodSpraySystem.HeavyHitDamage * 100f,
        ];

        await Server.WaitAssertion(() =>
        {
            var bloodSpraySystem = SEntMan.System<BloodSpraySystem>();
            Assert.Multiple(() =>
            {
                foreach (var damage in damages)
                {
                    var mobUid = SEntMan.SpawnEntity(_mob, new MapCoordinates(Vector2.Zero, testMap.MapId));
                    var bloodstreamComponent = SEntMan.GetComponent<BloodstreamComponent>(mobUid);
                    bloodSpraySystem.HandleBleedEffects((mobUid, bloodstreamComponent), 20f, new Vector2(1f, 0f), damage: damage);

                    var emitters = ServerEmitters();
                    var mist = SEntMan.GetComponent<KsParticlesComponent>(emitters[SplatterMist.Id]);
                    if (damage < BloodSpraySystem.HeavyHitDamage)
                    {
                        Assert.That(emitters.ContainsKey(HeavyHitExplosion.Id) || emitters.ContainsKey(HeavyHitSplatter.Id), Is.False, $"a {damage} damage hit threw heavy gore");
                        Assert.That(mist.Speed, Is.EqualTo(1f), $"a {damage} damage hit's mist flew faster");
                    }
                    else
                    {
                        var expectedSpeed = MathF.Min(1f + (damage - BloodSpraySystem.HeavyHitDamage) * BloodSpraySystem.HeavySpraySpeedPerDamage, BloodSpraySystem.MaxHeavySpraySpeed);
                        var expectedGore = Math.Clamp(damage * BloodSpraySystem.HeavyGorePerDamage, BloodSpraySystem.MinHeavyGoreAmount, BloodSpraySystem.MaxHeavyGoreAmount);
                        Assert.That(mist.Speed, Is.EqualTo(expectedSpeed).Within(0.001f), $"mist speed at {damage} damage");
                        Assert.That(emitters.ContainsKey(HeavyHitExplosion.Id) && emitters.ContainsKey(HeavyHitSplatter.Id), $"a {damage} damage hit threw no gore");
                        if (emitters.TryGetValue(HeavyHitSplatter.Id, out var goreUid))
                            Assert.That(SEntMan.GetComponent<KsParticlesComponent>(goreUid).Amount, Is.EqualTo(expectedGore).Within(0.001f), $"gore at {damage} damage");
                    }

                    foreach (var emitterUid in emitters.Values)
                        SEntMan.DeleteEntity(emitterUid);
                    SEntMan.DeleteEntity(mobUid);
                }
            });
        });
    }
}
