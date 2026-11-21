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
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Companions;

/// <summary>
///     Companions (<see cref="KsCompanionSystem"/>), given by a test perk: they take their owner's sides, follow it or
///         hover by it, come back after being destroyed, stay through the owner's crit, go when it dies and return when
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

# A body as the shoulder turret's: hit by what is shot.
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
      mode: Hover
      offset: 0.25, 0.4
      hoverStiffness: 6
      hoverMaxSpeed: 5
      hoverSnapDistance: 3
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
            Assert.That(riderTransform.ParentUid, Is.Not.EqualTo(ownerUid), "the rider should hover by the owner, not ride on it");
            Assert.That(riderTransform.ParentUid, Is.EqualTo(entMan.GetComponent<TransformComponent>(ownerUid).ParentUid),
                "the rider should hover beside the owner, on the same grid");
            Assert.That(FromHoverPoint(ownerUid), Is.LessThan(HoverTolerance), "the rider should materialise at its hover point");
        });

        // Whole once it has finished.
        await Pair.RunTicksSync(Ticks((float)(Entry(ownerUid, Follower).MaterialiseDuration + KsDissolveSystem.Linger).TotalSeconds + 0.5f));
        await server.WaitAssertion(() =>
            Assert.That(entMan.HasComponent<KsDissolveComponent>(Companion(ownerUid, Follower)!.Value), Is.False,
                "a materialised follower should be whole"));
    }

    /// <summary>
    ///     How close to its hover point a settled rider is. Far below its offset, so a rider left at the owner's middle
    ///         or swung round with it is well outside.
    /// </summary>
    private const float HoverTolerance = 0.05f;

    /// <summary>
    ///     How far the rider is from where it should hover: the owner's spot plus its offset, unturned by the owner (the
    ///         test grid isn't rotated, so in world axes).
    /// </summary>
    private float FromHoverPoint(EntityUid ownerUid)
    {
        var transformSystem = Pair.Server.System<SharedTransformSystem>();
        var riderMapCoordinates = transformSystem.GetMapCoordinates(Companion(ownerUid, Rider)!.Value);
        var ownerMapCoordinates = transformSystem.GetMapCoordinates(ownerUid);
        if (riderMapCoordinates.MapId != ownerMapCoordinates.MapId)
            return float.PositiveInfinity;

        return (ownerMapCoordinates.Position + Entry(ownerUid, Rider).Offset - riderMapCoordinates.Position).Length();
    }

    /// <summary>
    ///     Runs ticks until the rider has settled at its hover point, up to a cap well past what its stiffness takes.
    /// </summary>
    private async Task<float> SettleRider(EntityUid ownerUid)
    {
        var entry = Entry(ownerUid, Rider);
        var cap = Ticks(10f / entry.HoverStiffness + 1f);
        var distance = float.PositiveInfinity;
        for (var i = 0; i < cap; i++)
        {
            await Pair.RunTicksSync(1);
            await Pair.Server.WaitPost(() => distance = FromHoverPoint(ownerUid));
            if (distance < HoverTolerance)
                break;
        }

        return distance;
    }

    /// <summary>
    ///     A hovering companion is beside its owner, not on it: the owner turning doesn't turn it nor swing it round,
    ///         and a push doesn't move it.
    /// </summary>
    [Test]
    public async Task HoveringCompanionKeepsItsOwnFacingAndSpotWhenTheOwnerTurnsOrItIsPushed()
    {
        var server = Pair.Server;
        var entMan = server.EntMan;
        var transformSystem = server.System<SharedTransformSystem>();
        var ownerUid = await SpawnOwnerWithPerk();
        var riderUid = Companion(ownerUid, Rider)!.Value;
        var riderRotation = Angle.Zero;

        await server.WaitPost(() =>
        {
            riderRotation = transformSystem.GetWorldRotation(riderUid);
            transformSystem.SetWorldRotation(ownerUid, transformSystem.GetWorldRotation(ownerUid) + Angle.FromDegrees(90));
            // Hit by a push that would throw a body that could be pushed well clear.
            var riderPhysics = entMan.GetComponent<PhysicsComponent>(riderUid);
            server.System<SharedPhysicsSystem>().ApplyLinearImpulse(riderUid, new Vector2(0f, riderPhysics.Mass * 10f), body: riderPhysics);
        });

        // Checked every tick, so a rider moved off and brought back later still shows.
        for (var i = 0; i < Ticks(0.5f); i++)
        {
            await Pair.RunTicksSync(1);
            await server.WaitAssertion(() =>
            {
                Assert.That(Angle.ShortestDistance(transformSystem.GetWorldRotation(riderUid), riderRotation).Degrees, Is.EqualTo(0).Within(0.01),
                    $"the rider should not turn with its owner (tick {i})");
                Assert.That(FromHoverPoint(ownerUid), Is.LessThan(HoverTolerance),
                    $"the rider should stay at its hover point, unturned by the owner and unmoved by a push (tick {i})");
            });
        }
    }

    /// <summary>
    ///     A hovering companion trails its walking owner, never jumping, and settles at its hover point when the owner
    ///         stops; an owner teleported, or taken to another map, has it put there at once.
    /// </summary>
    [Test]
    public async Task HoveringCompanionTrailsAWalkingOwnerAndIsPutBackByATeleport()
    {
        var server = Pair.Server;
        var entMan = server.EntMan;
        var transformSystem = server.System<SharedTransformSystem>();
        var ownerUid = await SpawnOwnerWithPerk();
        var riderUid = Companion(ownerUid, Rider)!.Value;
        var entry = Entry(ownerUid, Rider);
        var tickTime = (float)server.ResolveDependency<IGameTiming>().TickPeriod.TotalSeconds;

        // Walked at half its top speed: it trails by speed / stiffness, which must stay inside the snap distance.
        var walkSpeed = entry.HoverMaxSpeed * 0.5f;
        Assert.That(walkSpeed / entry.HoverStiffness, Is.LessThan(entry.HoverSnapDistance * 0.5f),
            "the test entry's rider should trail a walk well inside its snap distance");

        var maxLag = 0f;
        var maxStep = 0f;
        var riderPosition = Vector2.Zero;
        await server.WaitPost(() => riderPosition = transformSystem.GetWorldPosition(riderUid));
        for (var i = 0; i < Ticks(1f); i++)
        {
            await server.WaitPost(() =>
                transformSystem.SetWorldPosition(ownerUid, transformSystem.GetWorldPosition(ownerUid) + new Vector2(walkSpeed * tickTime, 0f)));
            await Pair.RunTicksSync(1);
            await server.WaitPost(() =>
            {
                var newPosition = transformSystem.GetWorldPosition(riderUid);
                maxStep = MathF.Max(maxStep, (newPosition - riderPosition).Length());
                riderPosition = newPosition;
                maxLag = MathF.Max(maxLag, FromHoverPoint(ownerUid));
            });
        }

        Assert.That(maxLag, Is.GreaterThan(HoverTolerance), "the rider should trail a walking owner, not be carried by it");
        Assert.That(maxStep, Is.LessThanOrEqualTo(entry.HoverMaxSpeed * tickTime + 0.001f),
            "the rider should fly after a walking owner, never faster than its top speed nor jumping");
        Assert.That(await SettleRider(ownerUid), Is.LessThan(HoverTolerance), "the rider should settle at its hover point once the owner stops");

        // Teleported further than the snap distance: put there on the next update.
        await server.WaitPost(() =>
            transformSystem.SetWorldPosition(ownerUid, transformSystem.GetWorldPosition(ownerUid) + new Vector2(0f, entry.HoverSnapDistance + 2f)));
        await Pair.RunTicksSync(1);
        await server.WaitAssertion(() =>
            Assert.That(FromHoverPoint(ownerUid), Is.LessThan(HoverTolerance), "the rider should be put by an owner teleported away"));

        // Taken to another map.
        var otherMap = await Pair.CreateTestMap();
        await server.WaitPost(() => transformSystem.SetCoordinates(ownerUid, new EntityCoordinates(otherMap.Grid, 0.5f, 0.5f)));
        await Pair.RunTicksSync(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(transformSystem.GetMapId(riderUid), Is.EqualTo(otherMap.MapId), "the rider should go with an owner taken to another map");
            Assert.That(FromHoverPoint(ownerUid), Is.LessThan(HoverTolerance), "the rider should be at its hover point on the new map");
            Assert.That(entMan.GetComponent<TransformComponent>(riderUid).ParentUid, Is.Not.EqualTo(ownerUid),
                "the rider should still not ride on the owner");
        });
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
