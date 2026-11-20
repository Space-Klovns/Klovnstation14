#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Companions;
using Content.Server._KS14.Dissolve;
using Content.Server.Mind;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Shared._KS14.Companions;
using Content.Shared._KS14.Dissolve;
using Content.Shared._KS14.FactionCollision;
using Content.Shared._KS14.Perks;
using Content.Shared._KS14.Scenario.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Companions;

/// <summary>
///     Companions (<see cref="KsCompanionSystem"/>), given by a test perk: they take their owner's sides, follow it or
///         ride on it, come back after being destroyed, stay through the owner's crit, go when it dies and return when
///         it is revived, and are deleted with the perk.
/// </summary>
[TestFixture]
[TestOf(typeof(KsCompanionSystem))]
public sealed class KsCompanionTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _owner = "KsTestCompanionOwner";
    private readonly EntProtoId _enemy = "KsTestCompanionEnemy";
    private readonly ProtoId<KsPerkPrototype> _perk = "KsTestCompanionPerk";

    private static readonly ProtoId<DamageTypePrototype> Blunt = "Blunt";

    [TestPrototypes]
    private const string Prototypes = @"
- type: npcFaction
  id: KsTestCompanionSide

- type: npcFaction
  id: KsTestCompanionEnemySide
  hostile:
  - KsTestCompanionSide

- type: scenarioFaction
  id: KsTestCompanionScenarioSide
  nameLocId: scenario-faction-nt
  victoryLocId: scenario-roundend-victory-nt
  winTypeLocIds:
    Decimation: scenario-roundend-victory-nt-decimation

- type: htnCompound
  id: KsTestCompanionCompound
  branches:
  - tasks:
    - !type:HTNPrimitiveTask
      operator: !type:NoOperator

- type: entity
  id: KsTestCompanionMobBase
  abstract: true
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
  - type: Damageable
  - type: Injurable
    damageContainer: Biological

- type: entity
  parent: KsTestCompanionMobBase
  id: KsTestCompanionOwner
  components:
  - type: MobState
  - type: MobThresholds
    allowRevives: true
    thresholds:
      0: Alive
      50: Critical
      100: Dead
  - type: NpcFactionMember
    factions:
    - KsTestCompanionSide
  - type: ScenarioFactionMember
    id: KsTestCompanionScenarioSide

- type: entity
  parent: KsTestCompanionMobBase
  id: KsTestCompanionFollower
  components:
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      20: Dead
  - type: HTN
    rootTask:
      task: KsTestCompanionCompound
  # One it came with mustn't make it one of the side's people either.
  - type: ScenarioFactionMember
    id: KsTestCompanionScenarioSide

- type: entity
  id: KsTestCompanionEnemy
  components:
  - type: NpcFactionMember
    factions:
    - KsTestCompanionEnemySide

# A body as the shoulder turret's: pushable, and hit by what is shot.
- type: entity
  id: KsTestCompanionRider
  components:
  - type: Physics
    bodyType: KinematicController
  - type: Fixtures
    fixtures:
      fix1:
        shape: !type:PhysShapeCircle
          radius: 0.15
        density: 10
        layer:
        - BulletImpassable
  - type: Damageable

- type: ksPerk
  id: KsTestCompanionPerk
  name: ks-perk-healing-name
  description: ks-perk-healing-desc
  icon: { sprite: _KS14/Interface/Perks/perks.rsi, state: healing }
  components:
  - type: KsCompanions
    companions:
    - prototype: KsTestCompanionFollower
      respawnDelay: 3
      materialiseDuration: 0.5
      dissolveDuration: 0.5
    - prototype: KsTestCompanionRider
      mode: Attached
      offset: 0.25, 0.4
      respawnDelay: 3
      materialiseDuration: 0.5
      dissolveDuration: 0.5
";

    private const int Follower = 0;
    private const int Rider = 1;

    private int Ticks(float seconds) => (int)MathF.Ceiling(seconds * (float)Pair.Server.ResolveDependency<IGameTiming>().TickRate);

    private async Task<EntityUid> SpawnOwnerWithPerk()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        EntityUid ownerUid = default;
        await server.WaitPost(() =>
        {
            ownerUid = server.EntMan.SpawnAtPosition(_owner, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            Assert.That(server.System<SharedKsPerkSystem>().TryAddPerk(ownerUid, _perk), "the test perk should be given");
        });
        await Pair.RunTicksSync(2);
        return ownerUid;
    }

    private EntityUid? Companion(EntityUid ownerUid, int index)
    {
        return Pair.Server.System<KsCompanionSystem>().TryGetCompanion(ownerUid, index, out var companionUid) ? companionUid : null;
    }

    private KsCompanionEntry Entry(EntityUid ownerUid, int index)
    {
        return Pair.Server.EntMan.GetComponent<KsCompanionsComponent>(ownerUid).Companions[index];
    }

    private void Hurt(EntityUid uid, float amount)
    {
        var damage = new DamageSpecifier(Pair.Server.ProtoMan.Index(Blunt), FixedPoint2.New(amount));
        Pair.Server.System<DamageableSystem>().TryChangeDamage(uid, damage, ignoreResistances: true);
    }

    [Test]
    public async Task CompanionsMaterialiseOnTheOwnersSideAndFollowOrRideIt()
    {
        var server = Pair.Server;
        var entMan = server.EntMan;
        var ownerUid = await SpawnOwnerWithPerk();

        await server.WaitAssertion(() =>
        {
            var followerUid = Companion(ownerUid, Follower);
            Assert.That(followerUid, Is.Not.Null, "the follower should be out");
            var follower = followerUid!.Value;

            Assert.That(entMan.TryGetComponent<KsDissolveComponent>(follower, out var dissolve) && dissolve.Reverse,
                "the follower should materialise");
            Assert.That(entMan.TryGetComponent<NpcFactionMemberComponent>(follower, out var followerFactions) &&
                        new HashSet<ProtoId<NpcFactionPrototype>>(followerFactions.Factions).SetEquals(entMan.GetComponent<NpcFactionMemberComponent>(ownerUid).Factions),
                "the follower should take the owner's NPC factions");
            Assert.That(entMan.HasComponent<ScenarioFactionMemberComponent>(follower), Is.False,
                "the follower should be no scenario faction's member, so no rule counts it as one of its side's people");
            Assert.That(entMan.HasComponent<KsFactionCollisionShooterComponent>(follower),
                "the follower's shots should pass through the owner's side");

            var blackboard = entMan.GetComponent<HTNComponent>(follower).Blackboard;
            Assert.That(blackboard.TryGetValue<EntityCoordinates>(NPCBlackboard.FollowTarget, out var followTarget, entMan) &&
                        followTarget.EntityId == ownerUid,
                "the follower should follow the owner");

            var riderUid = Companion(ownerUid, Rider);
            Assert.That(riderUid, Is.Not.Null, "the rider should be out");
            var riderTransform = entMan.GetComponent<TransformComponent>(riderUid!.Value);
            var offset = Entry(ownerUid, Rider).Offset;
            Assert.That(riderTransform.ParentUid, Is.EqualTo(ownerUid), "the rider should ride on the owner");
            Assert.That(riderTransform.LocalPosition, Is.EqualTo(offset), "the rider should sit at its offset");

            // Moved off its spot by something other than physics: put back on the next update, not from the move.
            server.System<SharedTransformSystem>().SetCoordinates(riderUid.Value, new EntityCoordinates(ownerUid, new Vector2(3f, 3f)));
        });
        await Pair.RunTicksSync(1);
        await server.WaitAssertion(() =>
        {
            var riderTransform = entMan.GetComponent<TransformComponent>(Companion(ownerUid, Rider)!.Value);
            Assert.That(riderTransform.ParentUid, Is.EqualTo(ownerUid));
            Assert.That(riderTransform.LocalPosition, Is.EqualTo(Entry(ownerUid, Rider).Offset), "a moved rider should go back to its offset");
        });

        // Whole once it has finished.
        await Pair.RunTicksSync(Ticks((float)(Entry(ownerUid, Follower).MaterialiseDuration + KsDissolveSystem.Linger).TotalSeconds + 0.5f));
        await server.WaitAssertion(() =>
            Assert.That(entMan.HasComponent<KsDissolveComponent>(Companion(ownerUid, Follower)!.Value), Is.False,
                "a materialised follower should be whole"));
    }

    [Test]
    public async Task AttachedCompanionStaysAtItsOffsetWhenItOrItsOwnerIsPushed()
    {
        var server = Pair.Server;
        var entMan = server.EntMan;
        var physicsSystem = server.System<SharedPhysicsSystem>();
        var transformSystem = server.System<SharedTransformSystem>();
        var ownerUid = await SpawnOwnerWithPerk();
        var riderUid = Companion(ownerUid, Rider)!.Value;
        var offset = Entry(ownerUid, Rider).Offset;
        var riderPhysics = entMan.GetComponent<PhysicsComponent>(riderUid);
        var ownerStart = Vector2.Zero;

        await server.WaitPost(() =>
        {
            ownerStart = transformSystem.GetWorldPosition(ownerUid);
            // The owner shoved hard (friction soon stops it), and the rider hit by a push that would throw it well clear.
            physicsSystem.ApplyLinearImpulse(ownerUid, new Vector2(entMan.GetComponent<PhysicsComponent>(ownerUid).Mass * 5f, 0f));
            physicsSystem.ApplyLinearImpulse(riderUid, new Vector2(0f, riderPhysics.Mass * 10f));
        });

        // Checked every tick, so a rider pushed off and put back later still shows.
        for (var i = 0; i < Ticks(0.5f); i++)
        {
            await Pair.RunTicksSync(1);
            await server.WaitAssertion(() =>
            {
                var riderTransform = entMan.GetComponent<TransformComponent>(riderUid);
                Assert.That(riderPhysics.LinearVelocity, Is.EqualTo(Vector2.Zero), $"a push should not set the rider moving (tick {i})");
                Assert.That(riderTransform.ParentUid, Is.EqualTo(ownerUid), $"the rider should stay on the owner (tick {i})");
                Assert.That(riderTransform.LocalPosition, Is.EqualTo(offset), $"the rider should stay at its offset (tick {i})");
            });
        }

        await server.WaitAssertion(() =>
            Assert.That((transformSystem.GetWorldPosition(ownerUid) - ownerStart).Length(), Is.GreaterThan(0.1f),
                "control: the shoved owner should have moved, carrying the rider"));
    }

    [Test]
    public async Task CompanionsAreEnemiesOfTheOwnersEnemiesButNotOneOfItsSidesPeople()
    {
        var server = Pair.Server;
        var entMan = server.EntMan;
        var ownerUid = await SpawnOwnerWithPerk();

        await server.WaitAssertion(() =>
        {
            var enemyUid = entMan.SpawnAtPosition(_enemy, entMan.GetComponent<TransformComponent>(ownerUid).Coordinates);
            var hostiles = new HashSet<EntityUid>(server.System<NpcFactionSystem>().GetNearbyHostiles(enemyUid, 5f));

            foreach (var index in new[] { Follower, Rider })
            {
                var companionUid = Companion(ownerUid, index)!.Value;
                Assert.That(entMan.HasComponent<ScenarioFactionMemberComponent>(companionUid), Is.False,
                    $"companion {index} should not be counted as one of the owner's side");
            }

            var followerUid = Companion(ownerUid, Follower)!.Value;
            Assert.That(hostiles, Does.Contain(followerUid), "the owner's enemies should still fight its companion");
            Assert.That(hostiles, Does.Contain(ownerUid), "control: the owner itself should be hostile to them");
        });
    }

    [Test]
    public async Task DestroyedCompanionComesBackAfterItsDelay()
    {
        var server = Pair.Server;
        var entMan = server.EntMan;
        var ownerUid = await SpawnOwnerWithPerk();
        var entry = Entry(ownerUid, Follower);
        EntityUid firstUid = default;

        await server.WaitAssertion(() =>
        {
            firstUid = Companion(ownerUid, Follower)!.Value;
            Hurt(firstUid, 50);
            Assert.That(Companion(ownerUid, Follower), Is.Null, "a destroyed follower should leave its slot empty");
            Assert.That(server.System<KsDissolveSystem>().IsDissolving(firstUid), "its wreck should dissolve");

            // Destroyed outright, without dying first, counts as well.
            entMan.DeleteEntity(Companion(ownerUid, Rider)!.Value);
            Assert.That(Companion(ownerUid, Rider), Is.Null, "a deleted rider should leave its slot empty");
        });

        // The wreck has dissolved, and the delay isn't up yet.
        var dissolved = (float)(entry.DissolveDuration + KsDissolveSystem.Linger).TotalSeconds + 0.5f;
        Assert.That(dissolved, Is.LessThan((float)entry.RespawnDelay.TotalSeconds), "the test perk's delay should outlast the dissolve");
        await Pair.RunTicksSync(Ticks(dissolved));
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(firstUid), "the wreck should be gone");
            Assert.That(Companion(ownerUid, Follower), Is.Null, "the follower should not be back before its delay");
        });

        await Pair.RunTicksSync(Ticks((float)entry.RespawnDelay.TotalSeconds - dissolved + 0.5f));
        await server.WaitAssertion(() =>
        {
            Assert.That(Companion(ownerUid, Follower), Is.Not.Null, "the follower should be back after its delay");
            Assert.That(Companion(ownerUid, Rider), Is.Not.Null, "the rider should be back after its delay");
        });
    }

    /// <summary>
    ///     Puts a mind in the owner, as a player's body has.
    /// </summary>
    private EntityUid GiveMind(EntityUid ownerUid)
    {
        var mindSystem = Pair.Server.System<MindSystem>();
        var mindUid = mindSystem.CreateMind(null).Owner;
        mindSystem.TransferTo(mindUid, ownerUid);
        return mindUid;
    }

    [Test]
    public async Task CompanionsStayThroughCritGoOnDeathAndReturnOnRevive()
    {
        var server = Pair.Server;
        var entMan = server.EntMan;
        var ownerUid = await SpawnOwnerWithPerk();
        var entry = Entry(ownerUid, Follower);
        var mobState = entMan.GetComponent<MobStateComponent>(ownerUid);
        EntityUid firstUid = default;

        await server.WaitAssertion(() =>
        {
            GiveMind(ownerUid);
            firstUid = Companion(ownerUid, Follower)!.Value;
            Hurt(ownerUid, 60);
            Assert.That(mobState.CurrentState, Is.EqualTo(MobState.Critical));
            Assert.That(Companion(ownerUid, Follower), Is.EqualTo(firstUid), "the follower should stay while the owner is critical");

            Hurt(ownerUid, 60);
            Assert.That(mobState.CurrentState, Is.EqualTo(MobState.Dead));
            Assert.That(Companion(ownerUid, Follower), Is.Null, "the follower should go when the owner dies");
            Assert.That(Companion(ownerUid, Rider), Is.Null, "the rider should go when the owner dies");
            Assert.That(server.System<KsDissolveSystem>().IsDissolving(firstUid), "it should dissolve");
        });

        // Past the respawn delay: still nothing while the owner is dead.
        await Pair.RunTicksSync(Ticks((float)entry.RespawnDelay.TotalSeconds + 0.5f));
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(firstUid), "the dissolved follower should be gone");
            Assert.That(Companion(ownerUid, Follower), Is.Null, "no follower should come back to a dead owner");

            Hurt(ownerUid, -120);
            Assert.That(mobState.CurrentState, Is.EqualTo(MobState.Alive));
            Assert.That(Companion(ownerUid, Follower), Is.Not.Null, "the follower should return when the owner is revived");
            Assert.That(Companion(ownerUid, Rider), Is.Not.Null, "the rider should return when the owner is revived");
        });
    }

    /// <summary>
    ///     A body its player left for another loses its companions, and doesn't get them back when revived; they come
    ///         back when the player returns to it.
    /// </summary>
    [Test]
    public async Task CompanionsGoWithThePlayerAndNotBackToABodyWithoutOne()
    {
        var server = Pair.Server;
        var entMan = server.EntMan;
        var ownerUid = await SpawnOwnerWithPerk();
        var mindSystem = server.System<MindSystem>();
        var mobStateSystem = server.System<MobStateSystem>();
        EntityUid mindUid = default;
        EntityUid otherBodyUid = default;

        await server.WaitAssertion(() =>
        {
            mindUid = GiveMind(ownerUid);
            Assert.That(Companion(ownerUid, Follower), Is.Not.Null, "the follower should be out");

            // The player moves on to another body.
            otherBodyUid = entMan.SpawnAtPosition(_enemy, entMan.GetComponent<TransformComponent>(ownerUid).Coordinates);
            mindSystem.TransferTo(mindUid, otherBodyUid);
            Assert.That(Companion(ownerUid, Follower), Is.Null, "the follower should go with the player leaving the body");
            Assert.That(Companion(ownerUid, Rider), Is.Null, "the rider should go with the player leaving the body");

            // The empty body dies and is revived.
            mobStateSystem.ChangeMobState(ownerUid, MobState.Dead);
            mobStateSystem.ChangeMobState(ownerUid, MobState.Alive);
            Assert.That(Companion(ownerUid, Follower), Is.Null, "a body with no player should not get its companions back when revived");

            // The player comes back to it.
            mindSystem.TransferTo(mindUid, ownerUid);
            Assert.That(Companion(ownerUid, Follower), Is.Not.Null, "the follower should come back with the player");
            Assert.That(Companion(ownerUid, Rider), Is.Not.Null, "the rider should come back with the player");
        });
    }

    /// <summary>
    ///     A companion let go is picked up or put in something while it dissolves, which calls the dissolve off: it is
    ///         deleted all the same.
    /// </summary>
    [Test]
    public async Task ReleasedCompanionPutInAContainerIsStillGone()
    {
        var server = Pair.Server;
        var entMan = server.EntMan;
        var ownerUid = await SpawnOwnerWithPerk();
        var entry = Entry(ownerUid, Follower);
        EntityUid followerUid = default;

        await server.WaitAssertion(() =>
        {
            followerUid = Companion(ownerUid, Follower)!.Value;
            Hurt(ownerUid, 200);
            Assert.That(server.System<KsDissolveSystem>().IsDissolving(followerUid), "the follower should dissolve with its owner dead");

            var boxUid = entMan.SpawnAtPosition(null, entMan.GetComponent<TransformComponent>(ownerUid).Coordinates);
            var containerSystem = server.System<SharedContainerSystem>();
            Assert.That(containerSystem.Insert(followerUid, containerSystem.EnsureContainer<Container>(boxUid, "ks-test-box")),
                "the dissolving follower should go in the box");
        });

        await Pair.RunTicksSync(Ticks((float)(entry.DissolveDuration + KsDissolveSystem.Linger).TotalSeconds + 0.5f));
        await server.WaitAssertion(() =>
            Assert.That(entMan.Deleted(followerUid), "a companion let go should end up gone, even put in a container"));
    }

    [Test]
    public async Task CompanionsAreDeletedWithThePerk()
    {
        var server = Pair.Server;
        var entMan = server.EntMan;
        var ownerUid = await SpawnOwnerWithPerk();
        EntityUid followerUid = default;
        EntityUid riderUid = default;

        await server.WaitPost(() =>
        {
            followerUid = Companion(ownerUid, Follower)!.Value;
            riderUid = Companion(ownerUid, Rider)!.Value;
            Assert.That(server.System<SharedKsPerkSystem>().RemovePerk(ownerUid, _perk));
        });
        await Pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(followerUid), "the follower should be deleted with the perk");
            Assert.That(entMan.Deleted(riderUid), "the rider should be deleted with the perk");
        });
    }
}
