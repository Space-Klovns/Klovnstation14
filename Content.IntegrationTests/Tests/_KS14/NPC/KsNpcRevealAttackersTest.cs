#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Perception;
using Content.Server.NPC.Systems;
using Content.Shared._KS14.NPC;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Projectiles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     <c>NpcPerceptionComponent.RevealAttackers</c>: a hostile that hurts an NPC is known to it wherever it is - far
///         past its vision, out of sight - for a short while, and then it is just another hostile gone out of sight.
///         An NPC without the switch only knows what it sees.
/// </summary>
public sealed class KsNpcRevealAttackersTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private static readonly ProtoId<DamageTypePrototype> BluntDamage = "Blunt";

    // Far past the observer's vision, and off the grid: nothing but being shot could tell it about this hostile.
    private static readonly Vector2 FarAway = new(0.5f, 25.5f);

    // Only in this test's [TestPrototypes], so instance fields: the linter checks static ones only.
    private readonly EntProtoId _revealingMob = "KsRevealTestMobRevealing";
    private readonly EntProtoId _blindMob = "KsRevealTestMobBlind";
    private readonly EntProtoId _bullet = "KsRevealTestBullet";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsSquadTestMobLoner
  id: KsRevealTestMobRevealing
  components:
  - type: Injurable
    damageContainer: Biological
  - type: HTN
    blackboard:
      VisionRadius: !type:Single
        5
      AggroVisionRadius: !type:Single
        5
  - type: NpcPerception
    revealAttackers: true

- type: entity
  parent: KsSquadTestMobLoner
  id: KsRevealTestMobBlind
  components:
  - type: Injurable
    damageContainer: Biological
  - type: HTN
    blackboard:
      VisionRadius: !type:Single
        5
      AggroVisionRadius: !type:Single
        5

- type: entity
  id: KsRevealTestBullet
  components:
  - type: Projectile
    damage:
      types:
        Blunt: 1
";

    /// <summary>
    ///     Shot from far away by a bullet, the NPC knows the shooter - not the bullet - where it is, as a hostile in
    ///         sight. A little later it has lost it and remembers where it was.
    /// </summary>
    [Test]
    public async Task TestShooterIsKnownWhereverItIsThenLost()
    {
        var scene = await SetUpScene(_revealingMob);
        var perceptionSystem = scene.EntManager.System<NpcPerceptionSystem>();
        var revealDuration = default(System.TimeSpan);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(perceptionSystem.TryGetContact(scene.ObserverUid, scene.AttackerUid, out _), Is.False,
                "it must not know the shooter before being shot, or the test proves nothing");

            Shoot(scene);
            revealDuration = scene.EntManager.GetComponent<NpcPerceptionComponent>(scene.ObserverUid).RevealDuration;

            Assert.That(perceptionSystem.TryGetContact(scene.ObserverUid, scene.AttackerUid, out var contact), "shot, it should know who by");
            Assert.That(contact.State, Is.EqualTo(NpcContactState.Visible));
            Assert.That(contact.Reacted, "and react at once: it has been shot");
            Assert.That(perceptionSystem.TryGetContact(scene.ObserverUid, scene.BulletUid, out _), Is.False, "the shooter, not the bullet");

            // A look round at the next update does not lose it, out of sight and range as it is.
            perceptionSystem.UpdateNow(scene.ObserverUid);
            Assert.That(perceptionSystem.IsVisible(scene.ObserverUid, scene.AttackerUid), "still known the moment after");
        });

        await Pair.RunSeconds((float)revealDuration.TotalSeconds + 1f);

        await Pair.Server.WaitAssertion(() =>
        {
            perceptionSystem.UpdateNow(scene.ObserverUid);
            Assert.That(perceptionSystem.TryGetContact(scene.ObserverUid, scene.AttackerUid, out var contact), "remembered like any other lost hostile");
            Assert.That(contact.State, Is.EqualTo(NpcContactState.Lost), "but no longer tracked");
        });
    }

    /// <summary>
    ///     Without the switch a shot from out of sight tells the NPC nothing.
    /// </summary>
    [Test]
    public async Task TestShooterIsNotKnownWithoutTheSwitch()
    {
        var scene = await SetUpScene(_blindMob);
        var perceptionSystem = scene.EntManager.System<NpcPerceptionSystem>();

        await Pair.Server.WaitAssertion(() =>
        {
            Shoot(scene);
            perceptionSystem.UpdateNow(scene.ObserverUid);

            Assert.That(scene.EntManager.System<DamageableSystem>().GetTotalDamage(scene.ObserverUid), Is.GreaterThan(Content.Shared.FixedPoint.FixedPoint2.Zero),
                "the shot should have landed, or this proves nothing");
            Assert.That(perceptionSystem.TryGetContact(scene.ObserverUid, scene.AttackerUid, out _), Is.False);
        });
    }

    private void Shoot(Scene scene)
    {
        var damage = new DamageSpecifier(SProtoMan.Index(BluntDamage), 5);
        scene.EntManager.System<DamageableSystem>().TryChangeDamage(scene.ObserverUid, damage, origin: scene.BulletUid);
    }

    private async Task<Scene> SetUpScene(EntProtoId observerPrototype)
    {
        var entManager = Pair.Server.EntMan;
        var map = await Pair.CreateTestMap();
        EntityUid observerUid = default;
        EntityUid attackerUid = default;
        EntityUid bulletUid = default;

        await Pair.Server.WaitPost(() =>
        {
            var gridUid = MakeGrid(entManager, Pair.Server.ResolveDependency<ITileDefinitionManager>(), map.MapId, map.Grid, new(-5, -5), new(10, 5)).Owner;
            observerUid = SpawnAt(entManager, observerPrototype, gridUid, 0, 0);
            entManager.System<NPCSystem>().SleepNPC(observerUid);

            attackerUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 1, 0);
            entManager.System<SharedTransformSystem>().SetCoordinates(attackerUid, new EntityCoordinates(gridUid, FarAway));

            bulletUid = entManager.SpawnEntity(_bullet, new EntityCoordinates(gridUid, FarAway));
            entManager.GetComponent<ProjectileComponent>(bulletUid).Shooter = attackerUid;
        });

        await Pair.RunTicksSync(5);
        return new Scene(entManager, observerUid, attackerUid, bulletUid);
    }

    private readonly record struct Scene(IEntityManager EntManager, EntityUid ObserverUid, EntityUid AttackerUid, EntityUid BulletUid);
}
