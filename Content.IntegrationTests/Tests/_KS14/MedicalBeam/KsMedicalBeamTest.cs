#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.MedicalBeam;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.MedicalBeam;

/// <summary>
///     The medical beam gun (<see cref="KsMedicalBeamSystem"/>), on a test gun: fired at a friendly it locks on and
///         heals over time, without shooting; it won't lock onto an enemy; it breaks when range or sight is lost, and
///         stops when put down; the clients are told where the beam is.
/// </summary>
[TestFixture]
[TestOf(typeof(KsMedicalBeamSystem))]
public sealed class KsMedicalBeamTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _medic = "KsTestMedicalBeamMedic";
    private readonly EntProtoId _enemy = "KsTestMedicalBeamEnemy";
    private readonly EntProtoId _gun = "KsTestMedicalBeamGun";
    private readonly EntProtoId _wall = "KsTestMedicalBeamWall";
    private readonly EntProtoId _sparkle = "KsTestMedicalBeamSparkle";

    private static readonly ProtoId<DamageTypePrototype> Blunt = "Blunt";

    [TestPrototypes]
    private const string Prototypes = @"
- type: npcFaction
  id: KsTestMedicalBeamSideA

- type: npcFaction
  id: KsTestMedicalBeamSideB

- type: entity
  id: KsTestMedicalBeamMedic
  components:
  - type: Physics
    bodyType: KinematicController
  - type: Fixtures
    fixtures:
      fix1:
        shape: !type:PhysShapeCircle
          radius: 0.35
        density: 185
        mask:
        - MobMask
        layer:
        - MobLayer
  - type: Hands
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      500: Critical
      1000: Dead
  - type: Damageable
  - type: Injurable
    damageContainer: Biological
  - type: NpcFactionMember
    factions:
    - KsTestMedicalBeamSideA

- type: entity
  parent: KsTestMedicalBeamMedic
  id: KsTestMedicalBeamEnemy
  components:
  - type: NpcFactionMember
    factions:
    - KsTestMedicalBeamSideB

- type: entity
  parent: BaseItem
  id: KsTestMedicalBeamGun
  components:
  - type: Gun
    fireRate: 10
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
  - type: KsMedicalBeamGun
    maxRange: 5
    tickInterval: 0.5
    healEffect: KsTestMedicalBeamSparkle
    heal:
      Brute: 5

- type: entity
  id: KsTestMedicalBeamSparkle

- type: entity
  id: KsTestMedicalBeamWall
  components:
  - type: Transform
    anchored: true
  - type: Occluder
";

    private sealed record Scene(TestMapData Map, EntityUid MedicUid, EntityUid GunUid, EntityUid FriendUid, EntityUid EnemyUid);

    private int Ticks(float seconds) => (int)MathF.Ceiling(seconds * (float)Pair.Server.ResolveDependency<IGameTiming>().TickRate);

    private async Task<Scene> SetUp()
    {
        var server = Pair.Server;
        var entMan = server.EntMan;
        var testMap = await Pair.CreateTestMap();
        Scene scene = default!;

        await server.WaitPost(() =>
        {
            var medicUid = entMan.SpawnAtPosition(_medic, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            var gunUid = entMan.SpawnAtPosition(_gun, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            var handsSystem = server.System<SharedHandsSystem>();
            handsSystem.AddHand(medicUid, "hand", HandLocation.Middle);
            Assert.That(handsSystem.TryPickupAnyHand(medicUid, gunUid, checkActionBlocker: false), "the medic should hold the gun");

            var friendUid = entMan.SpawnAtPosition(_medic, new EntityCoordinates(testMap.Grid, 3.5f, 0.5f));
            var enemyUid = entMan.SpawnAtPosition(_enemy, new EntityCoordinates(testMap.Grid, 0.5f, 2.5f));
            Hurt(friendUid, 50);
            Hurt(enemyUid, 50);

            // The client draws what its player can see.
            server.PlayerMan.SetAttachedEntity(Pair.Player!, medicUid);
            scene = new Scene(testMap, medicUid, gunUid, friendUid, enemyUid);
        });
        await Pair.RunTicksSync(5);
        return scene;
    }

    private void Hurt(EntityUid uid, float amount)
    {
        var damage = new DamageSpecifier(Pair.Server.ProtoMan.Index(Blunt), FixedPoint2.New(amount));
        Pair.Server.System<DamageableSystem>().TryChangeDamage(uid, damage, ignoreResistances: true);
    }

    private float Damage(EntityUid uid) => (float)Pair.Server.System<DamageableSystem>().GetTotalDamage(uid);

    private KsMedicalBeamGunComponent Beam(Scene scene) => Pair.Server.EntMan.GetComponent<KsMedicalBeamGunComponent>(scene.GunUid);

    private bool Fire(Scene scene, EntityUid targetUid)
    {
        var entMan = Pair.Server.EntMan;
        var gun = new Entity<GunComponent>(scene.GunUid, entMan.GetComponent<GunComponent>(scene.GunUid));
        return Pair.Server.System<SharedGunSystem>().AttemptShoot(scene.MedicUid, gun, entMan.GetComponent<TransformComponent>(targetUid).Coordinates, targetUid);
    }

    /// <summary>
    ///     Fires at a spot, clicking no entity.
    /// </summary>
    private void FireAt(Scene scene, EntityCoordinates coordinates)
    {
        var entMan = Pair.Server.EntMan;
        var gun = new Entity<GunComponent>(scene.GunUid, entMan.GetComponent<GunComponent>(scene.GunUid));
        Pair.Server.System<SharedGunSystem>().AttemptShoot(scene.MedicUid, gun, coordinates);
    }

    private int Sparkles(EntityUid patientUid)
    {
        var entMan = Pair.Server.EntMan;
        var count = 0;
        var children = entMan.GetComponent<TransformComponent>(patientUid).ChildEnumerator;
        while (children.MoveNext(out var childUid))
        {
            if (entMan.GetComponent<MetaDataComponent>(childUid).EntityPrototype?.ID == _sparkle.Id)
                count++;
        }

        return count;
    }

    [Test]
    public async Task BeamLocksOntoAFriendAndHealsOverTimeButNotOntoAnEnemy()
    {
        var server = Pair.Server;
        var client = Pair.Client;
        var scene = await SetUp();
        var beam = Beam(scene);
        var healByGroup = beam.Heal;
        var heal = 0f;
        foreach (var amount in healByGroup.Values)
            heal += (float)amount;

        await server.WaitAssertion(() =>
        {
            Assert.That(Fire(scene, scene.EnemyUid), Is.False, "the beam gun should never shoot");
            Assert.That(beam.Target, Is.Null, "the beam should not lock onto an enemy");

            Fire(scene, scene.FriendUid);
            Assert.That(beam.Target, Is.EqualTo(scene.FriendUid), "the beam should lock onto the friend");
            Assert.That(beam.User, Is.EqualTo(scene.MedicUid));
        });

        // The state takes a few ticks to arrive.
        await Pair.RunTicksSync(10);
        var clientGunUid = Pair.ToClientUid(scene.GunUid);
        var clientFriendUid = Pair.ToClientUid(scene.FriendUid);
        await client.WaitAssertion(() =>
            Assert.That(client.EntMan.GetComponent<KsMedicalBeamGunComponent>(clientGunUid).Target, Is.EqualTo(clientFriendUid),
                "the client should be told where the beam is, to draw it"));

        // A little over two ticks of healing: two or three heals, whole ones.
        var friendBefore = Damage(scene.FriendUid);
        await Pair.RunTicksSync(Ticks((float)beam.TickInterval.TotalSeconds * 2f) + 2);
        await server.WaitAssertion(() =>
        {
            var heals = (friendBefore - Damage(scene.FriendUid)) / heal;
            Assert.That(heals, Is.InRange(1.95f, 3.05f), "the friend should heal each tick");
            Assert.That(heals, Is.EqualTo(MathF.Round(heals)).Within(0.05f), "each tick should heal the whole amount");
        });
    }

    [Test]
    public async Task BeamBreaksWhenRangeOrSightIsLostAndStopsWhenPutDown()
    {
        var server = Pair.Server;
        var entMan = server.EntMan;
        var scene = await SetUp();
        var beam = Beam(scene);
        var transformSystem = server.System<SharedTransformSystem>();

        // Out of range.
        await server.WaitPost(() =>
        {
            Fire(scene, scene.FriendUid);
            Assert.That(beam.Target, Is.EqualTo(scene.FriendUid));
            transformSystem.SetCoordinates(scene.FriendUid, new EntityCoordinates(scene.Map.Grid, beam.MaxRange + 2f, 0.5f));
        });
        await Pair.RunTicksSync(2);
        await server.WaitAssertion(() => Assert.That(beam.Target, Is.Null, "the beam should break out of range"));

        // Out of sight.
        await server.WaitPost(() =>
        {
            transformSystem.SetCoordinates(scene.FriendUid, new EntityCoordinates(scene.Map.Grid, 3.5f, 0.5f));
            Fire(scene, scene.FriendUid);
            Assert.That(beam.Target, Is.EqualTo(scene.FriendUid));
            entMan.SpawnAtPosition(_wall, new EntityCoordinates(scene.Map.Grid, 2.5f, 0.5f));
        });
        await Pair.RunTicksSync(2);
        await server.WaitAssertion(() => Assert.That(beam.Target, Is.Null, "the beam should break out of sight"));

        // Put down.
        await server.WaitPost(() =>
        {
            transformSystem.SetCoordinates(scene.FriendUid, new EntityCoordinates(scene.Map.Grid, 0.5f, -1.5f));
            Fire(scene, scene.FriendUid);
            Assert.That(beam.Target, Is.EqualTo(scene.FriendUid));
            Assert.That(server.System<SharedHandsSystem>().TryDrop(scene.MedicUid, scene.GunUid, checkActionBlocker: false));
        });
        await Pair.RunTicksSync(2);
        await server.WaitAssertion(() => Assert.That(beam.Target, Is.Null, "the beam should stop when put down"));
    }

    /// <summary>
    ///     Each heal tick on a hurt patient puts a sparkle on them, riding along; none once they are whole.
    /// </summary>
    [Test]
    public async Task HealTickSparklesOnAHurtPatientOnly()
    {
        var server = Pair.Server;
        var scene = await SetUp();
        var beam = Beam(scene);

        await server.WaitPost(() => Fire(scene, scene.FriendUid));
        await Pair.RunTicksSync(Ticks((float)beam.TickInterval.TotalSeconds) + 2);
        var sparkles = 0;
        await server.WaitAssertion(() =>
        {
            Assert.That(beam.Target, Is.EqualTo(scene.FriendUid));
            sparkles = Sparkles(scene.FriendUid);
            Assert.That(sparkles, Is.GreaterThan(0), "a heal tick on a hurt patient should sparkle on them");

            // Made whole.
            Hurt(scene.FriendUid, -Damage(scene.FriendUid));
            Assert.That(Damage(scene.FriendUid), Is.EqualTo(0f));
        });

        await Pair.RunTicksSync(Ticks((float)beam.TickInterval.TotalSeconds * 2f) + 2);
        await server.WaitAssertion(() =>
        {
            Assert.That(beam.Target, Is.EqualTo(scene.FriendUid), "the beam should stay on a whole patient");
            Assert.That(Sparkles(scene.FriendUid), Is.EqualTo(sparkles), "a whole patient should not sparkle");
        });
    }

    /// <summary>
    ///     Fired at the patient again the beam stays; at an enemy, or at nobody, it lets go. A click on an enemy is never
    ///         passed on to a friend beside them.
    /// </summary>
    [Test]
    public async Task FiringAtNoValidFriendLetsGoAndAClickedMobIsNeverPassedOn()
    {
        var server = Pair.Server;
        var scene = await SetUp();
        var beam = Beam(scene);
        var transformSystem = server.System<SharedTransformSystem>();
        var tick = Ticks(0.2f);

        await server.WaitPost(() => Fire(scene, scene.FriendUid));
        await Pair.RunTicksSync(tick);
        await server.WaitPost(() => Fire(scene, scene.FriendUid));
        await server.WaitAssertion(() => Assert.That(beam.Target, Is.EqualTo(scene.FriendUid), "firing at the patient again should keep the beam"));

        await Pair.RunTicksSync(tick);
        await server.WaitPost(() => Fire(scene, scene.EnemyUid));
        await server.WaitAssertion(() => Assert.That(beam.Target, Is.Null, "firing at an enemy should let go"));

        await Pair.RunTicksSync(tick);
        await server.WaitPost(() => Fire(scene, scene.FriendUid));
        await Pair.RunTicksSync(tick);
        await server.WaitPost(() => FireAt(scene, new EntityCoordinates(scene.Map.Grid, -2.5f, -2.5f)));
        await server.WaitAssertion(() => Assert.That(beam.Target, Is.Null, "firing at nobody should let go"));

        // A friend right beside the enemy, well within the aim assist.
        await server.WaitPost(() =>
        {
            var enemyPosition = transformSystem.GetWorldPosition(scene.EnemyUid);
            transformSystem.SetWorldPosition(scene.FriendUid, enemyPosition + new System.Numerics.Vector2(beam.AimAssistRadius / 2f, 0f));
        });
        await Pair.RunTicksSync(tick);
        await server.WaitPost(() => Fire(scene, scene.EnemyUid));
        await server.WaitAssertion(() => Assert.That(beam.Target, Is.Null, "a click on an enemy should not lock onto the friend beside it"));

        // Control: the same click, on no mob, does find the friend.
        await Pair.RunTicksSync(tick);
        await server.WaitPost(() => FireAt(scene, server.EntMan.GetComponent<TransformComponent>(scene.EnemyUid).Coordinates));
        await server.WaitAssertion(() => Assert.That(beam.Target, Is.EqualTo(scene.FriendUid), "a click on no mob should still find the friend close by"));
    }
}
