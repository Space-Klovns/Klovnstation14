#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Damage.DamagedSound;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Destructible;
using Content.Shared.FixedPoint;
using Content.Shared.Gravity;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Damage;

/// <summary>
///     <see cref="KsDamagedSoundComponent"/> on the server: damage plays it, its cooldown holds it back, contact damage
///         plays it for the mob in contact (leaving that mob's player to predict it), and barbed wire, which uses it,
///         also sounds when destroyed. What the attacker's own client hears is <see cref="KsDamagedSoundPredictionTest"/>.
/// </summary>
[TestFixture]
[TestOf(typeof(KsDamagedSoundSystem))]
public sealed class KsDamagedSoundTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    internal const string TestWire = "KsDamagedSoundTestWire";
    internal const string TestSoundPath = "/Audio/Effects/fence_rattle1.ogg";

    private static readonly EntProtoId BarbedWire = "KsBarbedWire";
    private static readonly EntProtoId Human = "MobHuman";
    private static readonly ProtoId<DamageTypePrototype> Blunt = "Blunt";

    [TestPrototypes]
    internal const string Prototypes = $@"
- type: entity
  id: {TestWire}
  components:
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        hard: false
        shape:
          !type:PhysShapeAabb
          bounds: ""-0.5,-0.5,0.5,0.5""
        layer:
        - MidImpassable
  - type: Damageable
  - type: Injurable
  - type: DamageContacts
    damage:
      types:
        Slash: 1
  - type: KsDamagedSound
    sound:
      path: {TestSoundPath}
    cooldown: 0.5
    playOnContactDamage: true
";

    [Test]
    public async Task DamagePlaysTheSoundOncePerCooldown()
    {
        var server = Pair.Server;
        var damageableSystem = server.System<DamageableSystem>();
        var testMap = await Pair.CreateTestMap();

        EntityUid wireUid = default;
        var cooldown = TimeSpan.Zero;
        await server.WaitPost(() =>
        {
            wireUid = SSpawnAtPosition(TestWire, testMap.GridCoords);
            cooldown = SComp<KsDamagedSoundComponent>(wireUid).Cooldown;
        });

        var heard = new Dictionary<EntityUid, HeardSound>();

        async Task Hit()
        {
            await server.WaitPost(() => damageableSystem.TryChangeDamage(wireUid, OneBlunt(), interruptsDoAfters: false));
            await Pair.RunTicksSync(1);
            await server.WaitPost(() => CollectSounds(server.EntMan, testMap.MapId, TestSoundPath, heard));
        }

        await Hit();
        Assert.That(heard, Has.Count.EqualTo(1), "damage should play the sound");
        Assert.That(heard.Values.Single().ExcludedEntity, Is.Null,
            "damage no client predicted should be heard from the server by everyone");

        await Hit();
        Assert.That(heard, Has.Count.EqualTo(1), "a second hit within the cooldown should play nothing");

        await Pair.RunTicksSync(TicksFor(cooldown) + 1);
        await Hit();
        Assert.That(heard, Has.Count.EqualTo(2), "a hit after the cooldown should play the sound again");
    }

    /// <summary>
    ///     Two mobs walk into the wire together, so both take contact damage on the same ticks: the wire plays once per
    ///         tick, not once per mob, and leaves out the player of a mob it plays for, whose client predicts it.
    /// </summary>
    [Test]
    public async Task ContactDamagePlaysOncePerTickForTheMobInIt()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var wireCoordinates = testMap.GridCoords.Offset(new Vector2(0.5f, 0.5f));

        var mobUids = new List<EntityUid>();
        await server.WaitPost(() =>
        {
            server.EntMan.EnsureComponent<GravityComponent>(testMap.MapUid).Enabled = true;

            SSpawnAtPosition(TestWire, wireCoordinates);
            for (var i = 0; i < 2; i++)
                mobUids.Add(SSpawnAtPosition(Human, wireCoordinates));

            // Kinematic mobs spawn asleep, and a sleeping body's contacts are never updated. Walking mobs are awake.
            var physicsSystem = server.System<SharedPhysicsSystem>();
            foreach (var mobUid in mobUids)
            {
                var physicsComponent = SComp<PhysicsComponent>(mobUid);
                physicsSystem.SetSleepingAllowed(mobUid, physicsComponent, false);
                physicsSystem.WakeBody(mobUid, body: physicsComponent);
            }
        });

        // Each damage tick moves a mob's next one on, so the times seen are the ticks there were, however far apart
        //      contact damage puts them.
        const float seconds = 4f;
        var heard = new Dictionary<EntityUid, HeardSound>();
        var damageTickTimes = new HashSet<TimeSpan>();
        for (var tick = 0; tick < TicksFor(TimeSpan.FromSeconds(seconds)); tick++)
        {
            await Pair.RunTicksSync(1);
            await server.WaitPost(() =>
            {
                CollectSounds(server.EntMan, testMap.MapId, TestSoundPath, heard);

                if (server.EntMan.TryGetComponent<Content.Shared.Damage.Components.DamagedByContactComponent>(mobUids[0], out var damagedComponent)
                    && damagedComponent.NextSecond > TimeSpan.Zero)
                {
                    damageTickTimes.Add(damagedComponent.NextSecond);
                }
            });
        }

        var damageTicks = damageTickTimes.Count;
        Assert.That(damageTicks, Is.GreaterThanOrEqualTo(2), "the mobs should have taken contact damage more than once, or this proves nothing");
        Assert.That(heard, Has.Count.InRange(damageTicks - 1, damageTicks + 1),
            "contact damage should play the sound once per damage tick, not once per mob in the wire");
        Assert.That(heard.Values.Select(sound => sound.ExcludedEntity), Is.All.AnyOf(mobUids[0], mobUids[1]),
            "the sound should leave out the mob in the wire, whose client predicts it");
    }

    /// <summary>
    ///     Barbed wire, destroyed, plays something besides its rattle.
    /// </summary>
    [Test]
    public async Task BarbedWireSoundsWhenDestroyed()
    {
        var server = Pair.Server;
        var damageableSystem = server.System<DamageableSystem>();
        var testMap = await Pair.CreateTestMap();

        EntityUid wireUid = default;
        var rattleFiles = new HashSet<string>();
        await server.WaitPost(() =>
        {
            wireUid = SSpawnAtPosition(BarbedWire, testMap.GridCoords.Offset(new Vector2(0.5f, 0.5f)));
            rattleFiles = SoundFiles(SComp<KsDamagedSoundComponent>(wireUid).Sound);
        });
        await Pair.RunTicksSync(1);

        await server.WaitPost(() =>
        {
            var destroyedAt = server.System<SharedDestructibleSystem>().DestroyedAt(wireUid);
            var damage = new DamageSpecifier(SProtoMan.Index(Blunt), destroyedAt + FixedPoint2.New(1));
            damageableSystem.TryChangeDamage(wireUid, damage, ignoreResistances: true, interruptsDoAfters: false);
        });
        await Pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.Deleted(wireUid), Is.True, "the damage should have destroyed the wire");

            var otherSounds = new List<string>();
            var query = server.EntMan.AllEntityQueryEnumerator<AudioComponent, TransformComponent>();
            while (query.MoveNext(out var audioComponent, out var transformComponent))
            {
                if (transformComponent.MapID == testMap.MapId && !rattleFiles.Contains(audioComponent.FileName))
                    otherSounds.Add(audioComponent.FileName);
            }

            Assert.That(otherSounds, Is.Not.Empty, "destroying barbed wire should play a sound of its own");
        });
    }

    private DamageSpecifier OneBlunt()
    {
        return new DamageSpecifier(SProtoMan.Index(Blunt), FixedPoint2.New(1));
    }

    private int TicksFor(TimeSpan time)
    {
        return (int)Math.Ceiling(time.TotalSeconds * Pair.Server.ResolveDependency<IGameTiming>().TickRate);
    }

    private HashSet<string> SoundFiles(SoundSpecifier sound)
    {
        return sound switch
        {
            SoundPathSpecifier pathSpecifier => new HashSet<string> { pathSpecifier.Path.ToString() },
            SoundCollectionSpecifier { Collection: { } collection } => SProtoMan.Index<SoundCollectionPrototype>(collection)
                .PickFiles.Select(path => path.ToString()).ToHashSet(),
            _ => new HashSet<string>(),
        };
    }

    /// <summary>
    ///     Adds every audio entity on <paramref name="mapId"/> playing <paramref name="path"/> to <paramref name="heard"/>,
    ///         so a sound that has finished and despawned since is still counted.
    /// </summary>
    internal static void CollectSounds(IEntityManager entityManager, MapId mapId, string path, Dictionary<EntityUid, HeardSound> heard)
    {
        var query = entityManager.AllEntityQueryEnumerator<AudioComponent, TransformComponent>();
        while (query.MoveNext(out var audioUid, out var audioComponent, out var transformComponent))
        {
            if (transformComponent.MapID == mapId && audioComponent.FileName == path)
                heard.TryAdd(audioUid, new HeardSound(audioComponent.ExcludedEntity, entityManager.IsClientSide(audioUid)));
        }
    }

    /// <summary>
    ///     What a sound was when it was first seen: it despawns once it has played.
    /// </summary>
    internal readonly record struct HeardSound(EntityUid? ExcludedEntity, bool ClientSide);
}
