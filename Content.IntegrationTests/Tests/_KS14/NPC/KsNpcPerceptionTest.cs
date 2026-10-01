#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;
using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Squad;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared._KS14.NPC;
using Content.Shared.NPC.Systems;
using Content.Shared.Stealth;
using Content.Shared.Stealth.Components;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     <see cref="NpcPerceptionSystem"/>: what an NPC believes about the hostiles around it. Hostiles in containers
///         are never seen; one seen hiding is known to be there, and one vanishing beside a locker is suspected to
///         be in it. A lost hostile is remembered where it was, not wherever it is now, and guessed onward from
///         there. Sightings are called out to the squad.
/// </summary>
/// <remarks>
///     Observers are put to sleep, so only <c>UpdateNow</c> moves their perception along and each test decides
///         exactly what every look sees. Light detection is off here, so everything in line of sight is lit.
/// </remarks>
public sealed class KsNpcPerceptionTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string Locker = "ClosetSteelBase";

    [TestPrototypes]
    private const string Prototypes = @"
- type: htnCompound
  id: KsRecheckTestRoot
  branches:
  - tasks:
    - !type:HTNPrimitiveTask
      recheckPreconditions: true
      preconditions:
      - !type:KeyExistsPrecondition
        key: KsRecheckTestKey
      operator: !type:StaticWaitOperator
        key: KsRecheckTestWait

- type: htnCompound
  id: KsNoRecheckTestRoot
  branches:
  - tasks:
    - !type:HTNPrimitiveTask
      preconditions:
      - !type:KeyExistsPrecondition
        key: KsRecheckTestKey
      operator: !type:StaticWaitOperator
        key: KsRecheckTestWait

- type: entity
  parent: KsSquadTestMobLoner
  id: KsRecheckTestMob
  components:
  - type: HTN
    enabled: true
    rootTask:
      task: KsRecheckTestRoot
    blackboard:
      KsRecheckTestKey: !type:Bool
        true
      KsRecheckTestWait: !type:Single
        100

- type: entity
  parent: KsRecheckTestMob
  id: KsNoRecheckTestMob
  components:
  - type: HTN
    enabled: true
    rootTask:
      task: KsNoRecheckTestRoot
    blackboard:
      KsRecheckTestKey: !type:Bool
        true
      KsRecheckTestWait: !type:Single
        100
";

    #region Containers

    /// <summary>
    ///     A hostile seen getting into a locker is never seen inside it, but is known to be there. Slipping out
    ///         unseen leaves the NPC believing it is still inside, until the locker is seen open and empty.
    /// </summary>
    [Test]
    public async Task TestSeenHidingIsConcealedUntilFoundEmpty()
    {
        var scene = await SetUpScene(targetTile: (5, 0), lockerTile: (5, 1));
        var (entManager, perceptionSystem) = (scene.EntManager, scene.PerceptionSystem);
        var entityStorageSystem = entManager.System<SharedEntityStorageSystem>();

        await Pair.Server.WaitAssertion(() =>
        {
            perceptionSystem.UpdateNow(scene.ObserverUid);
            Assert.That(perceptionSystem.IsVisible(scene.ObserverUid, scene.TargetUid), "the target starts in plain sight");

            Assert.That(entityStorageSystem.Insert(scene.TargetUid, scene.LockerUid!.Value), "the target should fit in the locker");
            perceptionSystem.UpdateNow(scene.ObserverUid);

            perceptionSystem.TryGetContact(scene.ObserverUid, scene.TargetUid, out var contact);
            Assert.Multiple(() =>
            {
                Assert.That(perceptionSystem.IsVisible(scene.ObserverUid, scene.TargetUid), Is.False, "a target in a locker is never seen");
                Assert.That(contact.State, Is.EqualTo(NpcContactState.Concealed), "a target seen getting in should be known to be in there");
                Assert.That(contact.ContainerUid, Is.EqualTo(scene.LockerUid), "...in that locker");
            });

            // Out unseen, and off well out of sight.
            entityStorageSystem.Remove(scene.TargetUid, scene.LockerUid.Value);
            MoveOutOfSight(entManager, scene);
            perceptionSystem.UpdateNow(scene.ObserverUid);
            perceptionSystem.TryGetContact(scene.ObserverUid, scene.TargetUid, out contact);
            Assert.That(contact.State, Is.EqualTo(NpcContactState.Concealed), "nobody saw it leave, so it is still believed to be inside");

            entityStorageSystem.OpenStorage(scene.LockerUid.Value);
            perceptionSystem.UpdateNow(scene.ObserverUid);
            perceptionSystem.TryGetContact(scene.ObserverUid, scene.TargetUid, out contact);
            Assert.That(contact.State, Is.EqualTo(NpcContactState.Lost), "a locker seen open and empty means it is not in there");
        });
    }

    /// <summary>
    ///     A hostile that got into a locker after it was already out of sight is just lost: nobody saw where it went.
    /// </summary>
    [Test]
    public async Task TestUnseenHidingIsOnlyLost()
    {
        // The locker is well away from where the target was last seen, so nothing suggests it hid there.
        var scene = await SetUpScene(targetTile: (5, 0), lockerTile: (2, -3));
        var (entManager, perceptionSystem) = (scene.EntManager, scene.PerceptionSystem);

        await Pair.Server.WaitAssertion(() =>
        {
            perceptionSystem.UpdateNow(scene.ObserverUid);
            MoveOutOfSight(entManager, scene);
            perceptionSystem.UpdateNow(scene.ObserverUid);

            entManager.System<SharedEntityStorageSystem>().Insert(scene.TargetUid, scene.LockerUid!.Value);
            perceptionSystem.UpdateNow(scene.ObserverUid);

            perceptionSystem.TryGetContact(scene.ObserverUid, scene.TargetUid, out var contact);
            Assert.That(contact.State, Is.EqualTo(NpcContactState.Lost));
        });
    }

    /// <summary>
    ///     A hostile carried off in something that is not a locker - a bag on someone's back - is not concealed
    ///         anywhere anyone could search, so it is just lost.
    /// </summary>
    [Test]
    public async Task TestCarriedTargetIsNotConcealed()
    {
        var scene = await SetUpScene(targetTile: (5, 0));
        var (entManager, perceptionSystem) = (scene.EntManager, scene.PerceptionSystem);
        var containerSystem = entManager.System<SharedContainerSystem>();

        await Pair.Server.WaitAssertion(() =>
        {
            perceptionSystem.UpdateNow(scene.ObserverUid);

            var carrierUid = entManager.SpawnEntity(null, entManager.GetComponent<TransformComponent>(scene.TargetUid).Coordinates);
            var carried = containerSystem.EnsureContainer<Container>(carrierUid, "ks_carried");
            Assert.That(containerSystem.Insert(scene.TargetUid, carried));

            perceptionSystem.UpdateNow(scene.ObserverUid);
            perceptionSystem.TryGetContact(scene.ObserverUid, scene.TargetUid, out var contact);
            Assert.That(contact.State, Is.EqualTo(NpcContactState.Lost));
        });
    }

    /// <summary>
    ///     A hostile that vanishes in plain sight right beside a closed locker is suspected to be in it - and once
    ///         the locker is seen open and empty, forgotten. Control: <see cref="TestUnseenHidingIsOnlyLost"/>,
    ///         with the locker further off.
    /// </summary>
    [Test]
    public async Task TestVanishingBesideLockerIsSuspected()
    {
        var scene = await SetUpScene(targetTile: (5, 0), lockerTile: (5, 1));
        var (entManager, perceptionSystem) = (scene.EntManager, scene.PerceptionSystem);

        await Pair.Server.WaitAssertion(() =>
        {
            perceptionSystem.UpdateNow(scene.ObserverUid);
            MoveOutOfSight(entManager, scene);
            perceptionSystem.UpdateNow(scene.ObserverUid);

            perceptionSystem.TryGetContact(scene.ObserverUid, scene.TargetUid, out var contact);
            Assert.That(contact.State, Is.EqualTo(NpcContactState.Suspected), "gone in plain sight beside a locker");
            Assert.That(contact.ContainerUid, Is.EqualTo(scene.LockerUid));

            entManager.System<SharedEntityStorageSystem>().OpenStorage(scene.LockerUid!.Value);
            perceptionSystem.UpdateNow(scene.ObserverUid);
            Assert.That(perceptionSystem.TryGetContact(scene.ObserverUid, scene.TargetUid, out _), Is.False,
                "a suspicion shown wrong should be dropped");
        });
    }

    #endregion

    #region Sight

    /// <summary>
    ///     A cloaked hostile is not seen at all, even in plain sight.
    /// </summary>
    [Test]
    public async Task TestCloakedTargetIsNotSeen()
    {
        var scene = await SetUpScene(targetTile: (5, 0));
        var (entManager, perceptionSystem) = (scene.EntManager, scene.PerceptionSystem);

        await Pair.Server.WaitAssertion(() =>
        {
            entManager.EnsureComponent<StealthComponent>(scene.TargetUid);
            entManager.System<SharedStealthSystem>().SetVisibility(scene.TargetUid, -1f);

            perceptionSystem.UpdateNow(scene.ObserverUid);
            Assert.That(perceptionSystem.TryGetContact(scene.ObserverUid, scene.TargetUid, out _), Is.False, "a cloaked target should go unseen");

            entManager.System<SharedStealthSystem>().SetVisibility(scene.TargetUid, 1f);
            perceptionSystem.UpdateNow(scene.ObserverUid);
            Assert.That(perceptionSystem.IsVisible(scene.ObserverUid, scene.TargetUid), "uncloaked, it should be seen");
        });
    }

    /// <summary>
    ///     A hostile that stops being one while still in plain sight - here, told to be ignored - is forgotten, not
    ///         remembered as lost or suspected to be hiding.
    /// </summary>
    [Test]
    public async Task TestTargetNoLongerHostileIsForgotten()
    {
        // A locker beside it, so that treating it as having vanished would make it Suspected.
        var scene = await SetUpScene(targetTile: (5, 0), lockerTile: (5, 1));
        var (entManager, perceptionSystem) = (scene.EntManager, scene.PerceptionSystem);

        await Pair.Server.WaitAssertion(() =>
        {
            perceptionSystem.UpdateNow(scene.ObserverUid);
            Assert.That(perceptionSystem.IsVisible(scene.ObserverUid, scene.TargetUid));

            entManager.System<NpcFactionSystem>().IgnoreEntity(scene.ObserverUid, scene.TargetUid);
            perceptionSystem.UpdateNow(scene.ObserverUid);

            Assert.That(perceptionSystem.TryGetContact(scene.ObserverUid, scene.TargetUid, out var contact), Is.False,
                $"a target no longer hostile should be forgotten, was {contact.State}");
        });
    }

    #endregion

    #region Memory

    /// <summary>
    ///     A lost hostile is remembered where it was last seen, and that memory does not follow it about. This used
    ///         to be the case only by accident - coordinates attached to the hostile itself - and so it tracked the
    ///         hostile through walls instead.
    /// </summary>
    [Test]
    public async Task TestLostTargetIsRememberedWhereItWasSeen()
    {
        var scene = await SetUpScene(targetTile: (5, 0));
        var (entManager, perceptionSystem) = (scene.EntManager, scene.PerceptionSystem);
        var transformSystem = entManager.System<SharedTransformSystem>();

        await Pair.Server.WaitAssertion(() =>
        {
            var seenAt = transformSystem.GetMapCoordinates(scene.TargetUid).Position;

            perceptionSystem.UpdateNow(scene.ObserverUid);
            MoveOutOfSight(entManager, scene);
            perceptionSystem.UpdateNow(scene.ObserverUid);

            // Somewhere else again, still out of sight.
            transformSystem.SetCoordinates(scene.TargetUid, new EntityCoordinates(scene.GridUid, new Vector2(-4.5f, 40.5f)));
            perceptionSystem.UpdateNow(scene.ObserverUid);

            Assert.That(perceptionSystem.TryGetBelievedCoordinates(scene.ObserverUid, scene.TargetUid, out var believed, out var state, out _));
            Assert.Multiple(() =>
            {
                Assert.That(state, Is.EqualTo(NpcContactState.Lost));
                Assert.That(believed.EntityId, Is.Not.EqualTo(scene.TargetUid), "the memory must not be attached to the target");
                Assert.That(transformSystem.ToMapCoordinates(believed).Position, Is.EqualTo(seenAt).Using(VectorComparer),
                    "it should be remembered where it was last seen");
            });
        });
    }

    /// <summary>
    ///     <c>CopyKeyOperator</c> with <c>snapshot</c> copies where coordinates point now, so a copy of a target's
    ///         coordinates stays put; without it the copy follows the target (the control).
    /// </summary>
    [Test]
    public async Task TestCopyKeySnapshotStaysPut()
    {
        var scene = await SetUpScene(targetTile: (5, 0));
        var entManager = scene.EntManager;
        var transformSystem = entManager.System<SharedTransformSystem>();
        var snapshot = new CopyKeyOperator { OriginKey = "TargetCoordinates", TargetKey = "Copied", Snapshot = true };
        var reference = new CopyKeyOperator { OriginKey = "TargetCoordinates", TargetKey = "Copied" };

        await Pair.Server.WaitAssertion(() =>
        {
            entManager.EntitySysManager.DependencyCollection.InjectDependencies(snapshot, oneOff: true);
            entManager.EntitySysManager.DependencyCollection.InjectDependencies(reference, oneOff: true);

            var seenAt = transformSystem.GetMapCoordinates(scene.TargetUid).Position;
            var blackboard = new NPCBlackboard();
            blackboard.SetValue(NPCBlackboard.Owner, scene.ObserverUid);
            blackboard.SetValue("TargetCoordinates", new EntityCoordinates(scene.TargetUid, Vector2.Zero));

#pragma warning disable RA0004 // completes synchronously
            var snapshotCoordinates = (EntityCoordinates)snapshot.Plan(blackboard, default).Result.Effects!["Copied"];
            var referenceCoordinates = (EntityCoordinates)reference.Plan(blackboard, default).Result.Effects!["Copied"];
#pragma warning restore RA0004

            transformSystem.SetCoordinates(scene.TargetUid, new EntityCoordinates(scene.GridUid, new Vector2(-4.5f, -4.5f)));

            Assert.Multiple(() =>
            {
                Assert.That(transformSystem.ToMapCoordinates(snapshotCoordinates).Position, Is.EqualTo(seenAt).Using(VectorComparer),
                    "a snapshot should stay where the target was");
                Assert.That(transformSystem.ToMapCoordinates(referenceCoordinates).Position, Is.Not.EqualTo(seenAt).Using(VectorComparer),
                    "a plain copy follows the target");
            });
        });
    }

    /// <summary>
    ///     A lost hostile is guessed to have carried on the way it was going - but not through a wall.
    /// </summary>
    [Test]
    public async Task TestDeadReckoningStopsAtWalls()
    {
        var scene = await SetUpScene(targetTile: (1, 0), observerTile: (0, 3), wallTile: (4, 0), secondTargetTile: (1, -2), extraWallTile: (6, 0));
        var (entManager, perceptionSystem) = (scene.EntManager, scene.PerceptionSystem);
        var physicsSystem = entManager.System<SharedPhysicsSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();

        await Pair.Server.WaitPost(() =>
        {
            // Both running east as they are seen, then gone.
            physicsSystem.SetLinearVelocity(scene.TargetUid, new Vector2(3f, 0f));
            physicsSystem.SetLinearVelocity(scene.SecondTargetUid!.Value, new Vector2(3f, 0f));
            perceptionSystem.UpdateNow(scene.ObserverUid);
            physicsSystem.SetLinearVelocity(scene.TargetUid, Vector2.Zero);
            physicsSystem.SetLinearVelocity(scene.SecondTargetUid.Value, Vector2.Zero);

            MoveOutOfSight(entManager, scene);
            transformSystem.SetCoordinates(scene.SecondTargetUid.Value, new EntityCoordinates(scene.GridUid, new Vector2(-4.5f, 40.5f)));
            perceptionSystem.UpdateNow(scene.ObserverUid);
        });

        await Pair.RunTicksSync(45); // 1.5s: 4.5 tiles on at their speed

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(perceptionSystem.TryGetPredictedCoordinates(scene.ObserverUid, scene.TargetUid, out var blocked));
            Assert.That(perceptionSystem.TryGetPredictedCoordinates(scene.ObserverUid, scene.SecondTargetUid!.Value, out var open));

            var blockedX = transformSystem.ToMapCoordinates(blocked).Position.X - transformSystem.GetMapCoordinates(scene.GridUid).Position.X;
            var openX = transformSystem.ToMapCoordinates(open).Position.X - transformSystem.GetMapCoordinates(scene.GridUid).Position.X;

            Assert.Multiple(() =>
            {
                Assert.That(openX, Is.EqualTo(1.5f + 4.5f).Within(0.3f), "with nothing in the way, it is guessed 4.5 tiles on");
                Assert.That(blockedX, Is.LessThan(4f), "the guess should stop short of the first wall, not the one behind it");
                Assert.That(blockedX, Is.GreaterThan(1.5f), "...but still be on its way there");
            });
        });
    }

    #endregion

    #region Squad

    /// <summary>
    ///     A sighting is called out: a squadmate that cannot see the hostile itself learns where it is, without
    ///         seeing it, and the squad's threat moves there.
    /// </summary>
    [Test]
    public async Task TestSightingIsCalledOutToSquad()
    {
        var (entManager, spotterUid, listenerUid, targetUid, _) = await SetUpSquad(secondTarget: false);
        var perceptionSystem = entManager.System<NpcPerceptionSystem>();
        var squadSystem = entManager.System<NpcSquadSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();

        await Pair.Server.WaitPost(() => perceptionSystem.UpdateNow(spotterUid));
        await Pair.RunTicksSync(24); // past the reaction time: only reacted-to hostiles are called out

        await Pair.Server.WaitAssertion(() =>
        {
            perceptionSystem.UpdateNow(spotterUid);

            Assert.That(perceptionSystem.TryGetContact(listenerUid, targetUid, out var heard), "the squadmate should have heard the callout");
            Assert.That(squadSystem.TryGetSquad(spotterUid, out var squadEntity));

            Assert.Multiple(() =>
            {
                Assert.That(heard.State, Is.EqualTo(NpcContactState.Reported));
                Assert.That(perceptionSystem.IsVisible(listenerUid, targetUid), Is.False, "being told is not seeing");
                Assert.That(transformSystem.ToMapCoordinates(squadEntity!.Value.Comp.ThreatCoordinates!.Value).Position,
                    Is.EqualTo(transformSystem.GetMapCoordinates(targetUid).Position).Using(VectorComparer),
                    "the squad's threat should be the sighting");
            });
        });
    }

    /// <summary>
    ///     With two hostiles in sight, callouts keep the squad's threat on the caller's main one - the nearest -
    ///         rather than swinging between them with each report.
    /// </summary>
    [Test]
    public async Task TestCalloutsKeepSquadThreatSteady()
    {
        var (entManager, spotterUid, _, targetUid, secondTargetUid) = await SetUpSquad(secondTarget: true);
        var perceptionSystem = entManager.System<NpcPerceptionSystem>();
        var squadSystem = entManager.System<NpcSquadSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();

        await Pair.Server.WaitPost(() => perceptionSystem.UpdateNow(spotterUid));

        for (var i = 0; i < 4; i++)
        {
            await Pair.RunTicksSync(32); // past the callout interval
            await Pair.Server.WaitAssertion(() =>
            {
                perceptionSystem.UpdateNow(spotterUid);
                Assert.That(perceptionSystem.TryGetContact(spotterUid, secondTargetUid!.Value, out var secondContact) && secondContact.Reacted,
                    "both hostiles should be in sight and reacted to");

                squadSystem.TryGetSquad(spotterUid, out var squadEntity);
                Assert.That(transformSystem.ToMapCoordinates(squadEntity!.Value.Comp.ThreatCoordinates!.Value).Position,
                    Is.EqualTo(transformSystem.GetMapCoordinates(targetUid).Position).Using(VectorComparer),
                    "the threat should stay on the nearest hostile");
            });
        }
    }

    #endregion

    #region Planner

    /// <summary>
    ///     A running task marked <c>recheckPreconditions</c> fails as soon as one of its preconditions stops
    ///         holding; the same task without it carries on (the control).
    /// </summary>
    [Test]
    [TestCase("KsRecheckTestMob", true)]
    [TestCase("KsNoRecheckTestMob", false)]
    public async Task TestRecheckPreconditionsStopsRunningTask(string prototype, bool expectStopped)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid mobUid = default;

        await server.WaitPost(() =>
        {
            var gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-2, -2), new Vector2i(2, 2)).Owner;
            mobUid = SpawnAt(entManager, prototype, gridUid, 0, 0);
        });

        await Pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var htnComponent = entManager.GetComponent<HTNComponent>(mobUid);
            Assert.That(htnComponent.Plan, Is.Not.Null, "the wait should be running");

            htnComponent.Blackboard.Remove<bool>("KsRecheckTestKey");
            htnComponent.PlanAccumulator = 100f; // no ordinary replan in the way
        });

        await Pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var htnComponent = entManager.GetComponent<HTNComponent>(mobUid);
            if (expectStopped)
            {
                Assert.That(htnComponent.Plan, Is.Null, "the task should have stopped once its precondition broke");
                Assert.That(htnComponent.PlanAccumulator, Is.LessThan(100f), "and asked for a new plan straight away");
            }
            else
                Assert.That(htnComponent.Plan, Is.Not.Null, "without a recheck, a running task carries on");
        });
    }

    #endregion

    /// <summary>
    ///     An update with two hostiles in sight allocates next to nothing once warmed up: it runs five times a second
    ///         for every operative.
    /// </summary>
    /// <remarks>
    ///     Two things used to allocate here: boxed <c>HashSet.Overlaps</c> enumerators in the hostile gather (160
    ///         bytes), and the engine's occluder ray query building a fresh list of trees for every line-of-sight
    ///         check (about 100 bytes a ray), which perception now does itself. The bound leaves room for nothing.
    /// </remarks>
    [Test]
    public async Task TestPerceptionUpdateBarelyAllocates()
    {
        var scene = await SetUpScene(targetTile: (5, 0), secondTargetTile: (5, -3), lockerTile: (2, -4));
        var perceptionSystem = scene.PerceptionSystem;

        await Pair.Server.WaitAssertion(() =>
        {
            for (var i = 0; i < 10; i++)
            {
                perceptionSystem.UpdateNow(scene.ObserverUid);
            }

            const int updates = 200;
            var before = System.GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < updates; i++)
            {
                perceptionSystem.UpdateNow(scene.ObserverUid);
            }

            var perUpdate = (System.GC.GetAllocatedBytesForCurrentThread() - before) / (double)updates;
            TestContext.Out.WriteLine($"perception update allocates {perUpdate:F1} bytes");
            Assert.That(perUpdate, Is.LessThan(16), "a perception update should allocate nothing once warmed up");
        });
    }

    /// <summary>
    ///     Perception's own line-of-sight check, which skips the engine query's allocation, gives the same answer as
    ///         the examine check it stands in for - through walls, past them, from inside one, and into one.
    /// </summary>
    [Test]
    public async Task TestLineOfSightMatchesExamine()
    {
        var scene = await SetUpScene(targetTile: (9, 4));
        var (entManager, perceptionSystem) = (scene.EntManager, scene.PerceptionSystem);
        var examineSystem = entManager.System<Content.Shared.Examine.ExamineSystemShared>();
        var transformSystem = entManager.System<SharedTransformSystem>();

        await Pair.Server.WaitPost(() =>
        {
            // A broken wall down the middle, and a few posts.
            for (var y = -5; y <= 5; y++)
            {
                if (y != 0)
                    SpawnAt(entManager, "WallSolid", scene.GridUid, 3, y);
            }

            SpawnAt(entManager, "WallSolid", scene.GridUid, 6, 2);
            SpawnAt(entManager, "WallSolid", scene.GridUid, 6, -2);
            SpawnAt(entManager, "WallSolid", scene.GridUid, -2, 3);
        });

        await Pair.RunTicksSync(5);

        await Pair.Server.WaitAssertion(() =>
        {
            var mapId = transformSystem.GetMapCoordinates(scene.GridUid).MapId;
            var random = new System.Random(1234);
            var blocked = 0;

            for (var i = 0; i < 400; i++)
            {
                // Some on tile centres - inside the walls - and some anywhere.
                var from = new MapCoordinates(RandomPoint(random), mapId);
                var to = new MapCoordinates(RandomPoint(random), mapId);
                var range = i % 4 == 0 ? 6f : 30f;

                var expected = examineSystem.InRangeUnOccluded(from, to, range, null);
                Assert.That(perceptionSystem.InLineOfSight(from, to, range), Is.EqualTo(expected),
                    $"from {from.Position} to {to.Position} within {range}");

                if (!expected)
                    blocked++;
            }

            Assert.That(blocked, Is.GreaterThan(50), "enough of the rays should be blocked for this to mean anything");
        });

        static Vector2 RandomPoint(System.Random random)
        {
            return random.Next(2) == 0
                ? new Vector2(random.Next(-5, 11) + 0.5f, random.Next(-5, 6) + 0.5f)
                : new Vector2((float)(random.NextDouble() * 16 - 5), (float)(random.NextDouble() * 11 - 5));
        }
    }

    private static readonly IEqualityComparer<Vector2> VectorComparer =
        EqualityComparer<Vector2>.Create((a, b) => (a - b).LengthSquared() < 0.01f, _ => 0);

    private sealed record Scene(
        IEntityManager EntManager,
        NpcPerceptionSystem PerceptionSystem,
        EntityUid GridUid,
        EntityUid ObserverUid,
        EntityUid TargetUid,
        EntityUid? SecondTargetUid,
        EntityUid? LockerUid);

    /// <summary>
    ///     A sleeping loner observer and a hostile in plain sight, optionally with a second hostile, a locker and a
    ///         wall, on an open grid from (-5, -5) to (10, 5).
    /// </summary>
    private async Task<Scene> SetUpScene(
        (int X, int Y) targetTile,
        (int X, int Y)? lockerTile = null,
        (int X, int Y) observerTile = default,
        (int X, int Y)? wallTile = null,
        (int X, int Y)? extraWallTile = null,
        (int X, int Y)? secondTargetTile = null)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();

        EntityUid gridUid = default;
        EntityUid observerUid = default;
        EntityUid targetUid = default;
        EntityUid? secondTargetUid = null;
        EntityUid? lockerUid = null;

        await server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-5, -5), new Vector2i(10, 5)).Owner;
            observerUid = SpawnAt(entManager, LonerMob, gridUid, observerTile.X, observerTile.Y);

            if (lockerTile is { } locker)
                lockerUid = SpawnAt(entManager, Locker, gridUid, locker.X, locker.Y);

            if (wallTile is { } wall)
                SpawnAt(entManager, "WallSolid", gridUid, wall.X, wall.Y);

            if (extraWallTile is { } extraWall)
                SpawnAt(entManager, "WallSolid", gridUid, extraWall.X, extraWall.Y);
        });

        await Pair.RunTicksSync(10);

        // Hostiles only once the observer is asleep, so nothing has looked at them before the test does.
        await server.WaitPost(() =>
        {
            entManager.System<NPCSystem>().SleepNPC(observerUid);
            targetUid = SpawnAt(entManager, NanoTrasenMob, gridUid, targetTile.X, targetTile.Y);

            if (secondTargetTile is { } second)
                secondTargetUid = SpawnAt(entManager, NanoTrasenMob, gridUid, second.X, second.Y);
        });

        return new Scene(entManager, entManager.System<NpcPerceptionSystem>(), gridUid, observerUid, targetUid, secondTargetUid, lockerUid);
    }

    /// <summary>
    ///     Off the grid and far out of range: gone, as far as the observer can tell.
    /// </summary>
    private static void MoveOutOfSight(IEntityManager entManager, Scene scene)
    {
        var transformSystem = entManager.System<SharedTransformSystem>();
        transformSystem.SetCoordinates(scene.TargetUid, new EntityCoordinates(scene.GridUid, new Vector2(0.5f, 40.5f)));
    }

    /// <summary>
    ///     Two squadmates four tiles apart - a spotter and a listener - asleep, with the squad formed, and a hostile
    ///         only the spotter is close enough to see. Optionally a second, further one.
    /// </summary>
    private async Task<(IEntityManager EntManager, EntityUid SpotterUid, EntityUid ListenerUid, EntityUid TargetUid, EntityUid? SecondTargetUid)> SetUpSquad(bool secondTarget)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();

        EntityUid gridUid = default;
        EntityUid spotterUid = default;
        EntityUid listenerUid = default;
        EntityUid targetUid = default;
        EntityUid? secondTargetUid = null;

        await server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-8, -5), new Vector2i(10, 5)).Owner;
            spotterUid = SpawnAt(entManager, SyndicateMob, gridUid, 0, 0);
            listenerUid = SpawnAt(entManager, SyndicateMob, gridUid, -4, 0);
        });

        await Pair.RunTicksSync(90);

        await server.WaitPost(() =>
        {
            var npcSystem = entManager.System<NPCSystem>();
            npcSystem.SleepNPC(spotterUid);
            npcSystem.SleepNPC(listenerUid);

            var squadSystem = entManager.System<NpcSquadSystem>();
            Assert.That(squadSystem.TryGetSquad(spotterUid, out var spotterSquad) &&
                squadSystem.TryGetSquad(listenerUid, out var listenerSquad) &&
                spotterSquad.Value.Owner == listenerSquad.Value.Owner, "the two should have formed a squad");

            // 7 tiles from the spotter, 11 from the listener: past its 10-tile vision.
            targetUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 7, 0);
            if (secondTarget)
                secondTargetUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 8, 3);
        });

        return (entManager, spotterUid, listenerUid, targetUid, secondTargetUid);
    }
}
