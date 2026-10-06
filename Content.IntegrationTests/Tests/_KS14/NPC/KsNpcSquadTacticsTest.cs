#nullable enable
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Doors;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Squad;
using Content.Server._KS14.NPC.Squad.Tactics;
using Content.Server.NPC.Systems;
using Content.Shared._KS14.NPC;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     <see cref="NpcSquadTacticsSystem"/>: hunting a hostile the squad has lost - watching for it, stacking up on the
///         ways into its room, going in, searching, giving up - and regrouping when nothing is going on.
/// </summary>
/// <remarks>
///     Every test stages the beliefs it needs with <see cref="NpcPerceptionSystem.SetContact"/> and drives the system
///         with <see cref="NpcSquadTacticsSystem.UpdateNow"/>, paused otherwise, so nothing moves on between asserts.
///         The room is 7x6 tiles, with an airlock on its west wall and another on its east wall.
/// </remarks>
public sealed class KsNpcSquadTacticsTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    /// <summary>
    ///     Squad mobs and a mob that never leads, all of which skip the watch: straight on to going in after it.
    /// </summary>
    private const string NoWatchMob = "KsTacticsTestMobNoWatch";
    private const string NoWatchLonerMob = "KsTacticsTestMobNoWatchLoner";
    private const string HastyLonerMob = "KsTacticsTestMobHastyLoner";
    private const string CautiousMob = "KsTacticsTestMobCautious";
    private const string Caution = "KsTacticsTestCaution";
    private const string SecurityMob = "KsTacticsTestMobSecurity";
    private const string SecurityDoor = "AirlockSecurityLocked";
    private const string CommandDoor = "AirlockCommandLocked";

    // Not static: the YAML linter validates every static ProtoId field, and this one is in the private submodule.
    private readonly Robust.Shared.Prototypes.ProtoId<Content.Shared.Humanoid.Prototypes.RandomHumanoidSettingsPrototype> _operativeNt = "KsOperativeNt";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsTacticsTestMobNoWatch
  components:
  - type: Hands # to open lockers with: a locker nobody could open is not searched
  - type: NpcSquadMember
    tactics:
      watchTime: 0
      maxStageDistance: 40 # the walk round the test room to its far door is about 21 tiles

- type: entity
  parent: KsTacticsTestMobNoWatch
  id: KsTacticsTestMobCohesive
  components:
  - type: NpcSquadMember
    tactics:
      searchCohesion: 10
      searchCohesionRange: 16

- type: entity
  parent: KsTacticsTestMobNoWatch
  id: KsTacticsTestMobNoWatchLoner
  components:
  - type: NpcSquadMember
    canLead: false
    tactics:
      watchTime: 0

- type: entity
  parent: KsTacticsTestMobNoWatch
  id: KsTacticsTestMobHastyLoner
  components:
  - type: NpcSquadMember
    canLead: false
    tactics:
      watchTime: 0
      searchCoverage: 0.6

- type: npcMeter
  id: KsTacticsTestCaution
  max: 100
  decayPerSecond: 0

- type: entity
  parent: KsTacticsTestMobNoWatch
  id: KsTacticsTestMobCautious
  components:
  - type: NpcSquadMember
    tactics:
      watchTime: 0
      cautionMeter: KsTacticsTestCaution
      cautiousHuntThreshold: 30
      regroupDistance: 8
      cautiousRegroupDistance: 3

- type: entity
  parent: KsTacticsTestMobNoWatch
  id: KsTacticsTestMobSecurity
  components:
  - type: Access
    tags:
    - Security

- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsTacticsTestMobInheritParent
  components:
  - type: NpcSquadMember
    cover:
      wallPreference: 0.3
      killZoneAvoidance: 0.7
    tactics:
      watchTime: 7s
      huntTimeout: 33s

- type: entity
  parent: KsTacticsTestMobInheritParent
  id: KsTacticsTestMobInheritChild
  components:
  - type: NpcSquadMember
    cover:
      wallPreference: 0.9
    tactics:
      watchTime: 1s
";

    /// <summary>
    ///     A child prototype that changes one cover or tactics setting keeps the rest of its parent's: the settings
    ///         merge, rather than the child's replacing the parent's outright and resetting what it left out.
    /// </summary>
    [Test]
    public async Task TestSquadSettingsMergeWithParent()
    {
        var entManager = Pair.Server.ResolveDependency<IEntityManager>();

        await Pair.Server.WaitAssertion(() =>
        {
            var childUid = entManager.SpawnEntity("KsTacticsTestMobInheritChild", MapCoordinates.Nullspace);
            var squadMemberComponent = entManager.GetComponent<NpcSquadMemberComponent>(childUid);

            Assert.Multiple(() =>
            {
                Assert.That(squadMemberComponent.Cover.WallPreference, Is.EqualTo(0.9f), "the child's own cover setting");
                Assert.That(squadMemberComponent.Cover.KillZoneAvoidance, Is.EqualTo(0.7f), "the parent's, which the child left alone");
                Assert.That(squadMemberComponent.Tactics.WatchTime, Is.EqualTo(System.TimeSpan.FromSeconds(1)), "the child's own tactics setting");
                Assert.That(squadMemberComponent.Tactics.HuntTimeout, Is.EqualTo(System.TimeSpan.FromSeconds(33)), "the parent's, which the child left alone");
            });

            entManager.DeleteEntity(childUid);
        });
    }

    /// <summary>
    ///     The operatives' squad settings come out of their several parents as intended: NT operatives always clear a
    ///         disturbance methodically, Syndicate ones only once cautious, elites keep everything they do not change
    ///         from the regular operative, and only elites lead. The operative prototypes are in the private submodule.
    /// </summary>
    [Test]
    public async Task TestOperativeSquadSettings()
    {
        var protoManager = Pair.Server.ResolveDependency<Robust.Shared.Prototypes.IPrototypeManager>();
        if (!protoManager.HasIndex(_operativeNt))
            Assert.Ignore("the operative prototypes are in the private _KsModule submodule, which is not present");

        var factory = Pair.Server.ResolveDependency<IComponentFactory>();

        NpcSquadMemberComponent Read(string id)
        {
            var settings = protoManager.Index<Content.Shared.Humanoid.Prototypes.RandomHumanoidSettingsPrototype>(id);
            return (NpcSquadMemberComponent) settings.Components![factory.GetComponentName<NpcSquadMemberComponent>()].Component;
        }

        await Pair.Server.WaitAssertion(() =>
        {
            var regularNt = Read("KsOperativeNt");
            var eliteNt = Read("KsEliteOperativeNt");
            var regularSyndicate = Read("KsOperativeSyn");
            var eliteSyndicate = Read("KsEliteOperativeSyn");

            Assert.Multiple(() =>
            {
                Assert.That(regularNt.Tactics.CautiousHuntThreshold, Is.Zero, "NT always clears disturbances");
                Assert.That(eliteNt.Tactics.CautiousHuntThreshold, Is.Zero, "NT elites too");
                Assert.That(regularSyndicate.Tactics.CautiousHuntThreshold, Is.EqualTo(30f), "Syndicate only once cautious");
                Assert.That(eliteSyndicate.Tactics.CautiousHuntThreshold, Is.EqualTo(30f), "Syndicate elites too");

                Assert.That(eliteNt.Tactics.WatchTime, Is.EqualTo(System.TimeSpan.FromSeconds(2)), "NT elites keep the elite watch");
                Assert.That(regularNt.Tactics.WatchTime, Is.EqualTo(System.TimeSpan.FromSeconds(4)), "regular NT keep the regular watch");
                Assert.That(eliteSyndicate.Tactics.CautionMeter?.Id, Is.EqualTo("KsCaution"), "elites keep the regular operative's caution meter");
                Assert.That(eliteSyndicate.Cover.KillZoneAvoidance, Is.EqualTo(0.8f), "elites keep the regular operative's cover");

                Assert.That(eliteNt.CanLead && eliteSyndicate.CanLead, "elites lead");
                Assert.That(regularNt.CanLead || regularSyndicate.CanLead, Is.False, "regulars do not");
            });
        });
    }

    /// <summary>
    ///     Just lost: a member that was on the move goes after it, to where it should be by now; one that was holding
    ///         stays where it is and watches that way.
    /// </summary>
    [Test]
    public async Task TestLossSendsMoversAfterItAndHoldersWatching()
    {
        var scene = await SetUpSquadOutsideRoom();

        await Pair.Server.WaitAssertion(() =>
        {
            StageLost(scene, scene.LeaderUid, moving: true);
            StageLost(scene, scene.MemberUid, moving: false);
            scene.TacticsSystem.UpdateNow();

            Assert.That(scene.TacticsSystem.GetHunt(scene.SquadUid)?.Phase, Is.EqualTo(NpcHuntPhase.Watch));
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.LeaderUid, out var moverOrder) && moverOrder.Kind == NpcOrderKind.Investigate,
                $"a member on the move should go after it, but has {moverOrder.Kind}");
            Assert.That(moverOrder.Coordinates.TryDistance(scene.EntManager, scene.TransformSystem, scene.LastKnownCoordinates, out var distance) && distance < 0.1f,
                "it should go to where the hostile should be by now - where it was, for one that stood still");
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.MemberUid, out var holderOrder) && holderOrder.Kind == NpcOrderKind.Watch,
                $"a member that was holding should stay and watch, but has {holderOrder.Kind}");
        });
    }

    /// <summary>
    ///     Once the watch is over, a squad outside the room it went into stacks up on its ways in - one member on each,
    ///         so they come at it from both sides - and only goes in once everyone is in place.
    /// </summary>
    [Test]
    public async Task TestSquadFlanksThenGoesInTogether()
    {
        var scene = await SetUpSquadOutsideRoom(squadMob: NoWatchMob);

        await Pair.Server.WaitAssertion(() =>
        {
            StageLost(scene, scene.LeaderUid, moving: false);
            StageLost(scene, scene.MemberUid, moving: false);
            scene.TacticsSystem.UpdateNow();

            var hunt = scene.TacticsSystem.GetHunt(scene.SquadUid);
            Assert.That(hunt?.Phase, Is.EqualTo(NpcHuntPhase.Stage), "the squad should be stacking up");
            Assert.That(hunt!.Entrances, Has.Count.EqualTo(2), "the room has two ways in");

            Assert.That(scene.TacticsSystem.TryGetOrder(scene.LeaderUid, out var leaderOrder) && leaderOrder.Kind == NpcOrderKind.Stage);
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.MemberUid, out var memberOrder) && memberOrder.Kind == NpcOrderKind.Stage);
            Assert.That(hunt.StagedMembers[scene.LeaderUid].EntranceIndex, Is.Not.EqualTo(hunt.StagedMembers[scene.MemberUid].EntranceIndex),
                "with two ways in, the two members should take one each");

            var leaderSpot = hunt.Entrances[hunt.StagedMembers[scene.LeaderUid].EntranceIndex].StageCoordinates;
            var memberSpot = hunt.Entrances[hunt.StagedMembers[scene.MemberUid].EntranceIndex].StageCoordinates;

            // Not everyone is in place yet: they wait.
            scene.TacticsSystem.UpdateNow();
            Assert.That(hunt.Phase, Is.EqualTo(NpcHuntPhase.Stage), "nobody should go in before everyone is in place");

            scene.TransformSystem.SetCoordinates(scene.LeaderUid, leaderSpot);
            scene.TacticsSystem.UpdateNow();
            Assert.That(hunt.Phase, Is.EqualTo(NpcHuntPhase.Stage), "nobody should go in while one member is still on its way");

            // Whichever of them was sent round the room, being at its spot is being in place: a member is never sent
            //      back to a turn it no longer needs.
            scene.TransformSystem.SetCoordinates(scene.MemberUid, memberSpot);
            scene.TacticsSystem.UpdateNow();
            Assert.That(hunt.Phase, Is.EqualTo(NpcHuntPhase.Entry), "with everyone in place, they should go in");
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.LeaderUid, out leaderOrder) && leaderOrder.Kind == NpcOrderKind.Enter);
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.MemberUid, out memberOrder) && memberOrder.Kind == NpcOrderKind.Enter);
        });
    }

    /// <summary>
    ///     With more ways in than members, the ones taken are those nearest where the hostile should be - within reason
    ///         of the walk there. Both members come up to a three-door room from the north: one takes the north door,
    ///         right in front of them, and the other the west or east door, whichever the hostile is nearer. Without a
    ///         preference, east wins either way, being a tile less to walk.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestEntrancesNearTheTargetArePreferred(bool targetWest)
    {
        var scene = await SetUpSquadOutsideRoom(squadMob: NoWatchMob, squadAt: [new Vector2i(3, 9), new Vector2i(4, 9)], northDoor: true);
        var targetCoordinates = new EntityCoordinates(scene.GridUid, targetWest ? new Vector2(0.5f, 2.5f) : new Vector2(5.5f, 3.5f));

        await Pair.Server.WaitAssertion(() =>
        {
            var now = IoCManager.Resolve<IGameTiming>().CurTime;
            foreach (var memberUid in new[] { scene.LeaderUid, scene.MemberUid })
            {
                scene.PerceptionSystem.SetContact(memberUid, scene.TargetUid, new NpcContact(NpcContactState.Lost,
                    now, now, now, targetCoordinates, default, null, Reacted: true, ReactAt: now, ObserverWasMoving: false));
            }

            scene.TacticsSystem.UpdateNow();

            var hunt = scene.TacticsSystem.GetHunt(scene.SquadUid);
            Assert.That(hunt?.Phase, Is.EqualTo(NpcHuntPhase.Stage), "the squad should be stacking up");
            Assert.That(hunt!.Entrances, Has.Count.EqualTo(3), "the room has three ways in");

            var taken = hunt.StagedMembers.Values.Select(staging => hunt.Entrances[staging.EntranceIndex].StageCoordinates.Position).ToList();
            Assert.Multiple(() =>
            {
                Assert.That(taken.Select(position => position).Distinct().Count(), Is.EqualTo(2), "the two should still split up");
                Assert.That(taken.Any(position => position.Y > 6f), "one should take the north door, right in front of them");
                Assert.That(taken.Any(position => targetWest ? position.X < 0f : position.X > 7f),
                    $"the other should take the {(targetWest ? "west" : "east")} door, nearer the hostile; took {string.Join(", ", taken)}");
            });
        });
    }

    /// <summary>
    ///     A member is only sent to lead a way in it can get through. With the west door asking for security access and
    ///         the east for command, the member with security access leads the west door, and the one with no access at
    ///         all stacks up behind it rather than at the east door it could never open.
    /// </summary>
    [Test]
    public async Task TestMemberWithoutAccessStacksBehindOneWithIt()
    {
        var scene = await SetUpSquadOutsideRoom(squadMob: SecurityMob, secondMob: NoWatchMob, westDoor: SecurityDoor, eastDoor: CommandDoor);

        await Pair.Server.WaitAssertion(() =>
        {
            var (securityUid, plainUid) = SplitBySecurity(scene);
            StageLost(scene, scene.LeaderUid, moving: false);
            StageLost(scene, scene.MemberUid, moving: false);
            scene.TacticsSystem.UpdateNow();

            var hunt = scene.TacticsSystem.GetHunt(scene.SquadUid);
            Assert.That(hunt?.Phase, Is.EqualTo(NpcHuntPhase.Stage), "the squad should be stacking up");

            var securityStaging = hunt!.StagedMembers[securityUid];
            var plainStaging = hunt.StagedMembers[plainUid];
            Assert.Multiple(() =>
            {
                Assert.That(securityStaging.IsLead, "the member with the access should lead");
                Assert.That(hunt.Entrances[securityStaging.EntranceIndex].StageCoordinates.Position.X, Is.LessThan(0f), "at the west door, which it can open");
                Assert.That(plainStaging.IsLead, Is.False, "the member without access leads nothing");
                Assert.That(plainStaging.EntranceIndex, Is.EqualTo(securityStaging.EntranceIndex), "and stacks up behind the one that can get it open");
            });
        });
    }

    /// <summary>
    ///     A member carrying jaws of life leads the door nobody can open, and forces it once everyone is in place: it is
    ///         ordered to breach that door with them, while the rest wait. Once the door is open - or gone, destroyed by
    ///         somebody else - they all go in, and nobody is left trying to force it.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task TestToolBearerForcesTheDoorNobodyCanOpen(bool destroyed)
    {
        var scene = await SetUpSquadOutsideRoom(squadMob: SecurityMob, secondMob: NoWatchMob, westDoor: SecurityDoor, eastDoor: CommandDoor);
        EntityUid jawsUid = default;

        await Pair.Server.WaitAssertion(() =>
        {
            var (securityUid, plainUid) = SplitBySecurity(scene);
            jawsUid = GiveInHand(scene.EntManager, plainUid, TestJawsOfLife);

            StageLost(scene, scene.LeaderUid, moving: false);
            StageLost(scene, scene.MemberUid, moving: false);
            scene.TacticsSystem.UpdateNow();

            var hunt = scene.TacticsSystem.GetHunt(scene.SquadUid)!;
            var plainStaging = hunt.StagedMembers[plainUid];
            var eastEntrance = hunt.Entrances[plainStaging.EntranceIndex];
            Assert.Multiple(() =>
            {
                Assert.That(plainStaging.IsLead && plainStaging.Method == NpcBreachMethod.Pry, "the member with the jaws should lead, prying");
                Assert.That(eastEntrance.StageCoordinates.Position.X, Is.GreaterThan(6f), "at the east door, which nobody can open");
                Assert.That(hunt.StagedMembers[securityUid].Method, Is.EqualTo(NpcBreachMethod.None), "the other opens its door by hand");
            });

            // Everyone in place: the jaws come out.
            scene.TransformSystem.SetCoordinates(securityUid, hunt.Entrances[hunt.StagedMembers[securityUid].EntranceIndex].StageCoordinates);
            scene.TransformSystem.SetCoordinates(plainUid, eastEntrance.StageCoordinates);
            scene.TacticsSystem.UpdateNow();

            Assert.That(hunt.Phase, Is.EqualTo(NpcHuntPhase.Breach), "with a door to force, they should breach first");
            Assert.Multiple(() =>
            {
                Assert.That(scene.TacticsSystem.TryGetOrder(plainUid, out var breachOrder) && breachOrder.Kind == NpcOrderKind.Breach &&
                    breachOrder.TargetUid == eastEntrance.DoorUid && breachOrder.ToolUid == jawsUid,
                    "the member with the jaws should be told to force the east door with them");
                Assert.That(scene.TacticsSystem.TryGetOrder(securityUid, out var waitOrder) && waitOrder.Kind == NpcOrderKind.Stage,
                    "the other should wait");
            });

            if (destroyed)
                scene.EntManager.DeleteEntity(eastEntrance.DoorUid!.Value);
            else
                scene.EntManager.System<Content.Shared.Doors.Systems.SharedDoorSystem>().StartOpening(eastEntrance.DoorUid!.Value);

            scene.TacticsSystem.UpdateNow();
            Assert.Multiple(() =>
            {
                Assert.That(hunt.Phase, Is.EqualTo(NpcHuntPhase.Entry), destroyed ? "with the door gone, they should go in" : "with the door forced, they should go in");
                Assert.That(scene.TacticsSystem.TryGetOrder(plainUid, out var enterOrder) && enterOrder.Kind == NpcOrderKind.Enter,
                    "the one with the jaws should go in too, not keep at the door");
            });
        });
    }

    /// <summary>
    ///     A room nobody can get into - every door asks for access none of them have, and none of them carries anything
    ///         to force one - is not stacked up on at all: the squad gives up on it. That holds with an open firelock
    ///         under each door too: the airlock is what stands in the way, not the firelock.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task TestNoWayInGivesUp(bool firelocks)
    {
        var scene = await SetUpSquadOutsideRoom(squadMob: NoWatchMob, westDoor: CommandDoor, eastDoor: CommandDoor, firelocks: firelocks);

        await Pair.Server.WaitAssertion(() =>
        {
            StageLost(scene, scene.LeaderUid, moving: false);
            StageLost(scene, scene.MemberUid, moving: false);
            scene.TacticsSystem.UpdateNow();

            Assert.That(scene.TacticsSystem.GetHunt(scene.SquadUid)?.Phase, Is.EqualTo(NpcHuntPhase.Exhausted),
                "with no way in, the squad should give up rather than stack up on doors it cannot open");
        });
    }

    /// <summary>
    ///     A lead found its door will not open for it after all - it was fooled - so the ways in are handed out again:
    ///         the member with the jaws now leads, and the one that was fooled stacks up behind it.
    /// </summary>
    [Test]
    public async Task TestFooledLeadIsReplaced()
    {
        var scene = await SetUpSquadOutsideRoom(squadMob: SecurityMob, secondMob: NoWatchMob, westDoor: SecurityDoor, eastDoor: CommandDoor);

        await Pair.Server.WaitAssertion(() =>
        {
            var (securityUid, plainUid) = SplitBySecurity(scene);
            GiveInHand(scene.EntManager, plainUid, TestJawsOfLife);

            StageLost(scene, scene.LeaderUid, moving: false);
            StageLost(scene, scene.MemberUid, moving: false);
            scene.TacticsSystem.UpdateNow();

            var hunt = scene.TacticsSystem.GetHunt(scene.SquadUid)!;
            var westDoorUid = hunt.Entrances[hunt.StagedMembers[securityUid].EntranceIndex].DoorUid!.Value;

            scene.EntManager.System<NpcDoorSystem>().ReportRefused(securityUid, westDoorUid);
            scene.TacticsSystem.UpdateNow();

            Assert.Multiple(() =>
            {
                Assert.That(hunt.ReassignedForDoors, "the ways in should have been handed out again");
                Assert.That(hunt.StagedMembers[securityUid].IsLead, Is.False, "the fooled member has no way in of its own any more");
                Assert.That(hunt.StagedMembers[plainUid].IsLead && hunt.StagedMembers[plainUid].Method == NpcBreachMethod.Pry,
                    "the member with the jaws should lead, prying");
            });
        });
    }

    /// <summary>
    ///     A hunted hostile that dies where none of the squad can see is still hunted: they cannot know. Once one of them
    ///         sees the body, the hunt is over.
    /// </summary>
    [Test]
    public async Task TestHuntEndsOnlyOnceTheDeathIsKnown()
    {
        var scene = await SetUpSquadOutsideRoom(squadMob: NoWatchMob);

        await Pair.Server.WaitAssertion(() =>
        {
            StageLost(scene, scene.LeaderUid, moving: false);
            StageLost(scene, scene.MemberUid, moving: false);
            scene.TacticsSystem.UpdateNow();
            var hunt = scene.TacticsSystem.GetHunt(scene.SquadUid);
            Assert.That(hunt, Is.Not.Null, "the squad should be hunting");

            // Dies in the far corner, out of everyone's sight.
            scene.EntManager.System<MobStateSystem>().ChangeMobState(scene.TargetUid, MobState.Dead);
            scene.PerceptionSystem.UpdateNow(scene.LeaderUid);
            scene.PerceptionSystem.UpdateNow(scene.MemberUid);
            scene.TacticsSystem.UpdateNow();
            // The same hunt, carrying on - not ended and started afresh from the contacts they still have.
            Assert.That(scene.TacticsSystem.GetHunt(scene.SquadUid), Is.SameAs(hunt), "nobody saw it die: the hunt goes on");

            // One of them comes across the body.
            var targetCoordinates = scene.EntManager.GetComponent<TransformComponent>(scene.TargetUid).Coordinates;
            scene.TransformSystem.SetCoordinates(scene.MemberUid, targetCoordinates.Offset(new Vector2(-1f, 0f)));
            scene.PerceptionSystem.UpdateNow(scene.MemberUid);
            scene.TacticsSystem.UpdateNow();
            Assert.That(scene.TacticsSystem.GetHunt(scene.SquadUid), Is.Null, "with the body found, the hunt should be over");
        });
    }

    /// <summary>
    ///     A lead that is already through its door is not replaced when the door turns on it afterwards: it got in, so
    ///         the ways in stay as they are.
    /// </summary>
    [Test]
    public async Task TestLeadAlreadyInsideIsNotReplaced()
    {
        var scene = await SetUpSquadOutsideRoom(squadMob: SecurityMob, secondMob: NoWatchMob, westDoor: SecurityDoor, eastDoor: CommandDoor);

        await Pair.Server.WaitAssertion(() =>
        {
            var (securityUid, plainUid) = SplitBySecurity(scene);
            GiveInHand(scene.EntManager, plainUid, TestJawsOfLife);

            StageLost(scene, scene.LeaderUid, moving: false);
            StageLost(scene, scene.MemberUid, moving: false);
            scene.TacticsSystem.UpdateNow();

            var hunt = scene.TacticsSystem.GetHunt(scene.SquadUid)!;
            var westDoorUid = hunt.Entrances[hunt.StagedMembers[securityUid].EntranceIndex].DoorUid!.Value;

            // Through the door and into the room, and then the door refuses it.
            scene.TransformSystem.SetCoordinates(securityUid, new EntityCoordinates(scene.GridUid, new Vector2(1.5f, 2.5f)));
            scene.EntManager.System<NpcDoorSystem>().ReportRefused(securityUid, westDoorUid);
            scene.TacticsSystem.UpdateNow();

            Assert.Multiple(() =>
            {
                Assert.That(hunt.ReassignedForDoors, Is.False, "a lead already inside should not have the ways in handed out again");
                Assert.That(hunt.StagedMembers[securityUid].IsLead, "it should still lead");
            });
        });
    }

    /// <summary>
    ///     The scene's two squad members, the one with security access first.
    /// </summary>
    private static (EntityUid SecurityUid, EntityUid PlainUid) SplitBySecurity(Scene scene)
    {
        return scene.EntManager.HasComponent<Content.Shared.Access.Components.AccessComponent>(scene.LeaderUid)
            ? (scene.LeaderUid, scene.MemberUid)
            : (scene.MemberUid, scene.LeaderUid);
    }

    private static EntityUid GiveInHand(IEntityManager entManager, EntityUid npcUid, string prototype)
    {
        var handsSystem = entManager.System<Content.Shared.Hands.EntitySystems.SharedHandsSystem>();
        handsSystem.AddHand(npcUid, "right", Content.Shared.Hands.Components.HandLocation.Right);
        var itemUid = entManager.SpawnEntity(prototype, entManager.GetComponent<TransformComponent>(npcUid).Coordinates);
        Assert.That(handsSystem.TryPickup(npcUid, itemUid, "right"), $"it should be holding the {prototype}");
        return itemUid;
    }

    /// <summary>
    ///     The member sent to the door on the far side of the room goes round the room to get there, never through it:
    ///         walking past where the hostile is hiding to stack up on the other door gives the game away. Its order is
    ///         the first turn on the way round, not the far door itself, which the pathfinder would reach by cutting
    ///         straight across the room.
    /// </summary>
    [Test]
    public async Task TestFarDoorIsReachedRoundTheRoom()
    {
        var scene = await SetUpSquadOutsideRoom(squadMob: NoWatchMob);

        await Pair.Server.WaitAssertion(() =>
        {
            StageLost(scene, scene.LeaderUid, moving: false);
            StageLost(scene, scene.MemberUid, moving: false);
            scene.TacticsSystem.UpdateNow();

            var hunt = scene.TacticsSystem.GetHunt(scene.SquadUid)!;
            var farUid = hunt.StagedMembers.Single(pair => hunt.Entrances[pair.Value.EntranceIndex].StageCoordinates.Position.X > 3f).Key;
            var farStaging = hunt.StagedMembers[farUid];

            Assert.That(farStaging.Waypoints, Is.Not.Empty, "the far door is not in a straight line round the room, so there should be turns");
            Assert.That(farStaging.Waypoints.Any(tile => hunt.RoomTiles.Contains(tile) || hunt.ThresholdTiles.Contains(tile)), Is.False,
                "no turn on the way round should be in the room, or in one of its doorways");

            // Every stretch between turns stays outside the room too.
            var from = scene.EntManager.GetComponent<TransformComponent>(farUid).Coordinates.Position;
            foreach (var tile in farStaging.Waypoints.Append(hunt.Entrances[farStaging.EntranceIndex].StageTile))
            {
                var to = new Vector2(tile.X + 0.5f, tile.Y + 0.5f);
                for (var step = 0f; step <= 1f; step += 0.05f)
                {
                    var point = Vector2.Lerp(from, to, step);
                    var pointTile = new Vector2i((int) System.MathF.Floor(point.X), (int) System.MathF.Floor(point.Y));
                    Assert.That(hunt.RoomTiles.Contains(pointTile), Is.False, $"the way round crosses the room at {pointTile}");
                }

                from = to;
            }

            Assert.That(scene.TacticsSystem.TryGetOrder(farUid, out var order) && order.Kind == NpcOrderKind.Stage);
            Assert.That(order.Coordinates.TryDistance(scene.EntManager, scene.TransformSystem, hunt.Entrances[farStaging.EntranceIndex].StageCoordinates, out var toDoor) && toDoor > 1f,
                "it should be sent to its first turn, not straight at the far door");
        });
    }

    /// <summary>
    ///     An order's facing is kept relative to its grid, not to the world, so it still points into the room after the
    ///         grid has turned: facing it, put into the world with the grid's rotation, points from the waiting spot to
    ///         the point inside the door, however the grid is turned.
    /// </summary>
    [Test]
    public async Task TestOrderFacingTurnsWithTheGrid()
    {
        var scene = await SetUpSquadOutsideRoom(squadMob: NoWatchMob);

        await Pair.Server.WaitAssertion(() =>
        {
            // Turned before the order is given, and again after: a facing kept in world space is wrong either way.
            scene.TransformSystem.SetLocalRotation(scene.GridUid, Angle.FromDegrees(90));
            StageLost(scene, scene.LeaderUid, moving: false);
            StageLost(scene, scene.MemberUid, moving: false);
            scene.TacticsSystem.UpdateNow();

            var hunt = scene.TacticsSystem.GetHunt(scene.SquadUid)!;
            var nearUid = hunt.StagedMembers.First(pair => pair.Value.Waypoints.Count == 0).Key;
            var entrance = hunt.Entrances[hunt.StagedMembers[nearUid].EntranceIndex];

            scene.TransformSystem.SetLocalRotation(scene.GridUid, Angle.FromDegrees(135));
            scene.TacticsSystem.UpdateNow();

            Assert.That(scene.TacticsSystem.TryGetOrder(nearUid, out var order) && order.Kind == NpcOrderKind.Stage);

            var stageWorld = scene.TransformSystem.ToMapCoordinates(entrance.StageCoordinates).Position;
            var entryWorld = scene.TransformSystem.ToMapCoordinates(entrance.EntryCoordinates).Position;
            var intoRoom = (entryWorld - stageWorld).ToWorldAngle();
            var worldFacing = order.Facing + scene.TransformSystem.GetWorldRotation(order.Coordinates.EntityId);

            Assert.That(System.Math.Abs(Angle.ShortestDistance(worldFacing, intoRoom).Degrees), Is.LessThan(1),
                $"facing {worldFacing.Degrees:F0} in the world should point into the room, at {intoRoom.Degrees:F0}");
        });
    }

    /// <summary>
    ///     A room is swept until every tile of its floor has been seen. In an L-shaped room entered along its long
    ///         side, the short leg is out of sight of the door: a member that has looked round the long side is sent on
    ///         into the leg, and the room only counts as searched once the leg has been seen too.
    /// </summary>
    [Test]
    public async Task TestLShapedRoomIsSweptIntoTheLeg()
    {
        var scene = await SetUpLonerOutsideLRoom(NoWatchLonerMob);

        await Pair.Server.WaitAssertion(() =>
        {
            StageLost(scene, scene.LonerUid, moving: false);
            scene.TacticsSystem.UpdateNow();

            var hunt = scene.TacticsSystem.GetHunt(scene.LonerUid)!;
            Assert.That(hunt.Phase, Is.EqualTo(NpcHuntPhase.Search));
            Assert.That(hunt.UnseenTiles, Has.Count.EqualTo(hunt.RoomTiles.Count), "nothing in the room can be seen from outside it");

            // In through the door: the long side is in plain view, the leg is round the corner.
            scene.TransformSystem.SetCoordinates(scene.LonerUid, new EntityCoordinates(scene.GridUid, new Vector2(2.5f, 0.5f)));
            scene.TacticsSystem.UpdateNow();

            Assert.That(hunt.Phase, Is.EqualTo(NpcHuntPhase.Search), "with the leg not yet seen, the room is not searched");
            Assert.That(hunt.UnseenTiles.All(IsInLeg), "everything left unseen should be in the leg");
            Assert.That(hunt.UnseenTiles, Is.Not.Empty);
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.LonerUid, out var order) && order.Kind == NpcOrderKind.Search);

            var orderTile = new Vector2i((int) System.MathF.Floor(order.Coordinates.Position.X), (int) System.MathF.Floor(order.Coordinates.Position.Y));
            Assert.That(IsInLeg(orderTile), $"it should be sent into the leg, but is sent to {orderTile}");

            scene.TransformSystem.SetCoordinates(scene.LonerUid, new EntityCoordinates(scene.GridUid, new Vector2(7.5f, 4.5f)));
            scene.TacticsSystem.UpdateNow();
            Assert.That(hunt.Phase, Is.EqualTo(NpcHuntPhase.Exhausted), "with the leg seen too, the room has been searched");
        });
    }

    /// <summary>
    ///     How thorough a search is can be turned down: an NPC that only needs to see most of a room gives up on the L
    ///         without going round its corner.
    /// </summary>
    [Test]
    public async Task TestSearchCoverageSetsHowThorough()
    {
        var scene = await SetUpLonerOutsideLRoom(HastyLonerMob);

        await Pair.Server.WaitAssertion(() =>
        {
            StageLost(scene, scene.LonerUid, moving: false);
            scene.TacticsSystem.UpdateNow();

            scene.TransformSystem.SetCoordinates(scene.LonerUid, new EntityCoordinates(scene.GridUid, new Vector2(2.5f, 0.5f)));
            scene.TacticsSystem.UpdateNow();

            var hunt = scene.TacticsSystem.GetHunt(scene.LonerUid)!;
            Assert.That(hunt.UnseenTiles, Is.Not.Empty, "the leg is still out of sight");
            Assert.That(hunt.Phase, Is.EqualTo(NpcHuntPhase.Exhausted), "having seen 60% of the room is enough for this one");
        });
    }

    /// <summary>
    ///     Sweeping a long hall with a nook off it, the nook out of sight of everyone and nearer than the far end of the
    ///         hall. Each member goes for the nearest unseen floor, so the nook gets someone, out of the other's sight. With <see cref="NpcSquadTacticsSettings.SearchCohesion"/>, both carry on down the hall, which the
    ///         other can see.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestCohesiveSquadSweepsInSightOfEachOther(bool cohesive)
    {
        var scene = await SetUpSquadInHallWithNook(cohesive ? "KsTacticsTestMobCohesive" : NoWatchMob);

        await Pair.Server.WaitAssertion(() =>
        {
            StageLost(scene, scene.LeaderUid, moving: false);
            scene.TacticsSystem.UpdateNow();

            var hunt = scene.TacticsSystem.GetHunt(scene.SquadUid)!;
            Assert.That(hunt.Phase, Is.EqualTo(NpcHuntPhase.Search), "already inside, the squad should search at once");
            Assert.That(hunt.UnseenTiles.Any(IsInNook), "the back of the nook should be out of sight");
            Assert.That(hunt.Sweeps, Has.Count.EqualTo(2), "both should be sweeping");

            var intoTheNook = hunt.Sweeps.Values.Count(sweep => IsInNook(sweep.Tile));
            if (cohesive)
                Assert.That(intoTheNook, Is.Zero, "keeping in sight of each other, neither should go into the nook yet");
            else
                Assert.That(intoTheNook, Is.Positive, "each for the nearest unseen floor, the nook should get someone");
        });
    }

    /// <summary>
    ///     The nook off the hall in <see cref="SetUpSquadInHallWithNook"/>, past the opening: out of sight of the hall.
    /// </summary>
    private static bool IsInNook(Vector2i tile)
    {
        return tile.X is >= 7 and <= 11 && tile.Y is >= 4 and <= 6;
    }

    /// <summary>
    ///     A hall 21 tiles by 3 with an airlock at its west end, and a nook off its north side: a two-wide opening at
    ///         x 5-6 into a space running east behind the hall's wall. A squad of two inside the hall, at its west end
    ///         and below the opening, a hostile far away, and the tactics system paused.
    /// </summary>
    private async Task<Scene> SetUpSquadInHallWithNook(string squadMob)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var tacticsSystem = entManager.System<NpcSquadTacticsSystem>();
        var map = await Pair.CreateTestMap();

        EntityUid gridUid = default, firstUid = default, secondUid = default, targetUid = default;

        await server.WaitPost(() =>
        {
            tacticsSystem.UpdatesPaused = true;
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-12, -12), new Vector2i(24, 12)).Owner;

            for (var x = -1; x <= 21; x++)
            {
                for (var y = -1; y <= 7; y++)
                {
                    var hall = x is >= 0 and <= 20 && y is >= 0 and <= 2;
                    var opening = x is >= 5 and <= 6 && y == 3;
                    var nook = x is >= 5 and <= 11 && y is >= 4 and <= 6;
                    if (hall || opening || nook)
                        continue;

                    if (x == -1 && y == 1)
                        SpawnPoweredDoorAt(entManager, "Airlock", gridUid, x, y);
                    else
                        SpawnAt(entManager, "WallSolid", gridUid, x, y);
                }
            }

            firstUid = SpawnAt(entManager, squadMob, gridUid, 0, 1);
            secondUid = SpawnAt(entManager, squadMob, gridUid, 5, 1);
            targetUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 11, -11);
        });

        await Pair.RunTicksSync(150);

        EntityUid squadUid = default, leaderUid = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.System<NpcSquadSystem>().TryGetSquad(firstUid, out var squadEntity), "the two should form a squad");
            squadUid = squadEntity!.Value.Owner;
            leaderUid = squadEntity.Value.Comp.Leader!.Value;
        });

        return new Scene(entManager,
            tacticsSystem,
            entManager.System<NpcPerceptionSystem>(),
            entManager.System<SharedTransformSystem>(),
            gridUid,
            squadUid,
            leaderUid,
            leaderUid == firstUid ? secondUid : firstUid,
            LonerUid: default,
            targetUid,
            new EntityCoordinates(gridUid, new Vector2(3.5f, 1.5f)));
    }

    /// <summary>
    ///     The short leg of the L room: three wide, up from the right-hand end of the long side.
    /// </summary>
    private static bool IsInLeg(Vector2i tile)
    {
        return tile.X is >= 6 and <= 8 && tile.Y is >= 3 and <= 7;
    }

    /// <summary>
    ///     A squad already in the room it went into does not stack up outside: it starts searching. Closed lockers come
    ///         first, since that is where someone who vanished most likely went, and anything a member can see from
    ///         close enough counts as searched straight away. The locker is only searched once it has been opened.
    ///         With nothing left, the squad gives up: everyone forgets the hostile and holds the area.
    /// </summary>
    [Test]
    public async Task TestSearchChecksLockersAndGivesUp()
    {
        var scene = await SetUpSquadOutsideRoom(squadMob: NoWatchMob);
        EntityUid lockerUid = default;

        await Pair.Server.WaitAssertion(() =>
        {
            // On the north wall: its tile is the wall's, which room detection never counts as part of the room.
            lockerUid = SpawnAt(scene.EntManager, "ClosetWall", scene.GridUid, 3, 6);
            scene.TransformSystem.SetCoordinates(scene.LeaderUid, new EntityCoordinates(scene.GridUid, new Vector2(1.5f, 1.5f)));
            scene.TransformSystem.SetCoordinates(scene.MemberUid, new EntityCoordinates(scene.GridUid, new Vector2(3.5f, 3.5f)));

            StageLost(scene, scene.LeaderUid, moving: false);
            StageLost(scene, scene.MemberUid, moving: false);
            scene.TacticsSystem.UpdateNow();

            var hunt = scene.TacticsSystem.GetHunt(scene.SquadUid);
            Assert.That(hunt?.Phase, Is.EqualTo(NpcHuntPhase.Search), "a squad already inside should start searching");
            Assert.That(hunt!.SearchPoints[0].StorageUid, Is.EqualTo(lockerUid), "the closed locker should be searched first");
            Assert.That(hunt.UnseenTiles, Is.Empty, "the whole of a plain box room is in view of the members inside it");
            Assert.That(hunt.SearchPoints.Where(point => point.StorageUid == null).All(point => point.Cleared),
                "everything in plain view of the members should count as searched already");
            Assert.That(hunt.SearchPoints[0].Cleared, Is.False, "a closed locker is not searched by looking at it");

            // The member nearest the locker is sent to open it.
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.MemberUid, out var order) && order.Kind == NpcOrderKind.Search && order.TargetUid == lockerUid,
                $"the member nearest the locker should be sent to open it, but has {order.Kind}");

            scene.EntManager.System<SharedEntityStorageSystem>().OpenStorage(lockerUid);
            scene.TacticsSystem.UpdateNow();

            Assert.That(hunt.Phase, Is.EqualTo(NpcHuntPhase.Exhausted), "with everything searched, the squad should give up");
            Assert.Multiple(() =>
            {
                Assert.That(scene.PerceptionSystem.TryGetContact(scene.LeaderUid, scene.TargetUid, out _), Is.False, "the leader should forget the hostile");
                Assert.That(scene.PerceptionSystem.TryGetContact(scene.MemberUid, scene.TargetUid, out _), Is.False, "the member should forget the hostile");
                Assert.That(scene.TacticsSystem.TryGetOrder(scene.LeaderUid, out var holdOrder) && holdOrder.Kind == NpcOrderKind.HoldArea,
                    "the squad should hold the area a while");
            });
        });
    }

    /// <summary>
    ///     A spot is only searched once someone has actually seen it: a member just the other side of the wall has not,
    ///         however close it is. And an NPC on its own never stacks up - it goes straight to searching.
    /// </summary>
    [Test]
    public async Task TestLonerSearchesAndWallsHideSpots()
    {
        var scene = await SetUpSquadOutsideRoom(lonerAt: new Vector2i(3, -3));

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(scene.EntManager.GetComponent<NpcSquadMemberComponent>(scene.LonerUid).Squad, Is.Null,
                "the loner should not have joined anyone");
        });

        await Pair.Server.WaitAssertion(() =>
        {
            scene.EntManager.System<NPCSystem>().WakeNPC(scene.LonerUid);
            StageLost(scene, scene.LonerUid, moving: false);
            scene.TacticsSystem.UpdateNow();

            var hunt = scene.TacticsSystem.GetHunt(scene.LonerUid);
            Assert.That(hunt?.Phase, Is.EqualTo(NpcHuntPhase.Search), "an NPC on its own should go straight to searching");
            Assert.That(hunt!.SearchPoints.All(point => !point.Cleared),
                "nothing in the room can be seen from outside it, so nothing should count as searched");
            Assert.That(hunt.UnseenTiles, Has.Count.EqualTo(hunt.RoomTiles.Count), "none of the room's floor either");
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.LonerUid, out var order) && order.Kind == NpcOrderKind.Search,
                $"it should be sent to search, but has {order.Kind}");

            scene.TransformSystem.SetCoordinates(scene.LonerUid, new EntityCoordinates(scene.GridUid, new Vector2(3.5f, 2.5f)));
            scene.TacticsSystem.UpdateNow();

            Assert.That(hunt.Phase, Is.EqualTo(NpcHuntPhase.Exhausted),
                "from inside an empty room, everything is in sight, so the search should be over");
        });
    }

    /// <summary>
    ///     A hunted hostile seen again calls the hunt off: it is not lost any more, and combat takes over.
    /// </summary>
    [Test]
    public async Task TestHostileSeenAgainEndsHunt()
    {
        var scene = await SetUpSquadOutsideRoom(squadMob: NoWatchMob);

        await Pair.Server.WaitAssertion(() =>
        {
            StageLost(scene, scene.LeaderUid, moving: false);
            StageLost(scene, scene.MemberUid, moving: false);
            scene.TacticsSystem.UpdateNow();
            Assert.That(scene.TacticsSystem.GetHunt(scene.SquadUid), Is.Not.Null);

            var now = IoCManager.Resolve<IGameTiming>().CurTime;
            scene.PerceptionSystem.SetContact(scene.MemberUid, scene.TargetUid, new NpcContact(NpcContactState.Visible,
                now, now, now, scene.LastKnownCoordinates, default, null, Reacted: true, ReactAt: now));
            scene.TacticsSystem.UpdateNow();

            Assert.That(scene.TacticsSystem.GetHunt(scene.SquadUid), Is.Null, "a hostile in sight is not being hunted");
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.LeaderUid, out _), Is.False, "the hunt's orders should be called off");
        });
    }

    /// <summary>
    ///     With nothing going on, a member that has strayed far from its leader goes back to it. One close by, the
    ///         leader itself, and one that has just seen something, do not.
    /// </summary>
    [Test]
    public async Task TestStrayMemberRegroupsWhenQuiet()
    {
        var scene = await SetUpSquadOutsideRoom();

        await Pair.Server.WaitAssertion(() =>
        {
            var leaderCoordinates = scene.EntManager.GetComponent<TransformComponent>(scene.LeaderUid).Coordinates;

            scene.TacticsSystem.UpdateNow();
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.MemberUid, out _), Is.False, "a member beside its leader has no need to regroup");

            scene.TransformSystem.SetCoordinates(scene.MemberUid, leaderCoordinates.Offset(new Vector2(0f, -10f)));
            scene.TacticsSystem.UpdateNow();
            Assert.Multiple(() =>
            {
                Assert.That(scene.TacticsSystem.TryGetOrder(scene.MemberUid, out var order) && order.Kind == NpcOrderKind.Regroup,
                    "a stray member should regroup on its leader");
                Assert.That(scene.TacticsSystem.TryGetOrder(scene.LeaderUid, out _), Is.False, "the leader has nobody to regroup on");
            });

            // Something going on: it stays where it is.
            StageLost(scene, scene.MemberUid, moving: false, reacted: false);
            scene.TacticsSystem.UpdateNow();
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.MemberUid, out _), Is.False,
                "a member that has just seen something should not be pulled away from it");
        });
    }

    /// <summary>
    ///     A disturbance - something heard, nobody seen - is only hunted by a cautious squad. A calm one goes to it as it
    ///         always has; one whose leader is cautious enough stacks up on the room and searches it, with no watch first
    ///         since nobody lost sight of anyone, and nobody to forget at the end.
    /// </summary>
    [Test]
    public async Task TestCautiousSquadHuntsDisturbances()
    {
        var scene = await SetUpSquadOutsideRoom(squadMob: CautiousMob);
        var meterSystem = scene.EntManager.System<Content.Server._KS14.NPC.Meters.NpcMeterSystem>();

        await Pair.Server.WaitAssertion(() =>
        {
            var disturbance = new EntityCoordinates(scene.GridUid, new Vector2(3.5f, 2.5f));

            scene.TacticsSystem.NoteDisturbance(scene.MemberUid, disturbance);
            scene.TacticsSystem.UpdateNow();
            Assert.That(scene.TacticsSystem.GetHunt(scene.SquadUid), Is.Null, "a calm squad just goes to a disturbance");

            meterSystem.Set(scene.LeaderUid, Caution, 50f);
            scene.TacticsSystem.NoteDisturbance(scene.MemberUid, disturbance);
            scene.TacticsSystem.UpdateNow();

            var hunt = scene.TacticsSystem.GetHunt(scene.SquadUid);
            Assert.That(hunt, Is.Not.Null, "a squad whose leader is cautious should hunt it");
            Assert.That(hunt!.TargetUid, Is.Null, "there is nobody to hunt, only somewhere");
            Assert.That(hunt.Phase, Is.EqualTo(NpcHuntPhase.Stage), "with nobody to watch for, it should go straight to stacking up");
        });
    }

    /// <summary>
    ///     The more cautious a member, the less it strays from its leader before going back to it.
    /// </summary>
    [Test]
    public async Task TestCautiousMemberStraysLess()
    {
        var scene = await SetUpSquadOutsideRoom(squadMob: CautiousMob);
        var meterSystem = scene.EntManager.System<Content.Server._KS14.NPC.Meters.NpcMeterSystem>();

        await Pair.Server.WaitAssertion(() =>
        {
            var leaderCoordinates = scene.EntManager.GetComponent<TransformComponent>(scene.LeaderUid).Coordinates;

            // Six tiles off: within a calm member's eight, past a cautious one's three.
            scene.TransformSystem.SetCoordinates(scene.MemberUid, leaderCoordinates.Offset(new Vector2(0f, -6f)));
            scene.TacticsSystem.UpdateNow();
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.MemberUid, out _), Is.False, "a calm member can be six tiles off");

            meterSystem.Set(scene.MemberUid, Caution, 100f);
            scene.TacticsSystem.UpdateNow();
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.MemberUid, out var order) && order.Kind == NpcOrderKind.Regroup,
                "a cautious member should come back in");
        });
    }

    /// <summary>
    ///     A member holding its spot in the squad's cover of the leader's room is not regrouped, however far that spot is
    ///         from the leader and however cautious the member: otherwise it walks in to the leader, the hold sends it
    ///         back out, and round it goes.
    /// </summary>
    [Test]
    public async Task TestMemberHoldingCoverIsNotRegrouped()
    {
        var scene = await SetUpSquadOutsideRoom(squadMob: CautiousMob);
        var meterSystem = scene.EntManager.System<Content.Server._KS14.NPC.Meters.NpcMeterSystem>();
        var coverSystem = scene.EntManager.System<NpcSquadCoverSystem>();

        await Pair.Server.WaitAssertion(() =>
        {
            // The leader in a corner of the room, the member out at its spot covering one of the doors.
            scene.TransformSystem.SetCoordinates(scene.LeaderUid, new EntityCoordinates(scene.GridUid, new Vector2(0.5f, 0.5f)));
            Assert.That(coverSystem.TryGetAssignment(scene.MemberUid, out var assignment), "the member should have a spot covering the room");
            scene.TransformSystem.SetCoordinates(scene.MemberUid, assignment.Coordinates);

            meterSystem.Set(scene.MemberUid, Caution, 100f);
            var spotDistance = (scene.TransformSystem.GetWorldPosition(scene.MemberUid) - scene.TransformSystem.GetWorldPosition(scene.LeaderUid)).Length();
            Assert.That(spotDistance, Is.GreaterThan(3f), "the spot should be further from the leader than a cautious member strays");

            scene.TacticsSystem.UpdateNow();
            Assert.That(scene.TacticsSystem.TryGetOrder(scene.MemberUid, out _), Is.False,
                "a member holding its spot in the room is with the squad, and should be left there");
        });
    }

    /// <summary>
    ///     An update with nothing to hunt allocates nothing: it runs for every squad twice a second.
    /// </summary>
    [Test]
    public async Task TestQuietUpdateDoesNotAllocate()
    {
        var scene = await SetUpSquadOutsideRoom();

        await Pair.Server.WaitAssertion(() =>
        {
            // Warm up: first-use allocations - component storage, lazily built lists - are not per update.
            for (var i = 0; i < 20; i++)
            {
                scene.TacticsSystem.UpdateNow();
            }

            const int updates = 200;
            var before = System.GC.GetAllocatedBytesForCurrentThread();

            for (var i = 0; i < updates; i++)
            {
                scene.TacticsSystem.UpdateNow();
            }

            var perUpdate = (System.GC.GetAllocatedBytesForCurrentThread() - before) / (double) updates;
            Assert.That(perUpdate, Is.LessThan(16), $"a quiet update allocated {perUpdate:F1} bytes");
        });
    }

    private sealed record Scene(
        IEntityManager EntManager,
        NpcSquadTacticsSystem TacticsSystem,
        NpcPerceptionSystem PerceptionSystem,
        SharedTransformSystem TransformSystem,
        EntityUid GridUid,
        EntityUid SquadUid,
        EntityUid LeaderUid,
        EntityUid MemberUid,
        EntityUid LonerUid,
        EntityUid TargetUid,
        EntityCoordinates LastKnownCoordinates);

    /// <summary>
    ///     Gives <paramref name="memberUid"/> a hostile it lost just now, last seen standing in the middle of the room.
    /// </summary>
    private static void StageLost(Scene scene, EntityUid memberUid, bool moving, bool reacted = true)
    {
        var now = IoCManager.Resolve<IGameTiming>().CurTime;
        scene.PerceptionSystem.SetContact(memberUid, scene.TargetUid, new NpcContact(NpcContactState.Lost,
            now, now, now, scene.LastKnownCoordinates, default, null, reacted, ReactAt: now, ObserverWasMoving: moving));
    }

    /// <summary>
    ///     An L-shaped room - a long side nine tiles by three, and a leg three by five going up from its right-hand end -
    ///         with one airlock, in the middle of the long side's bottom wall, and an NPC on its own outside it. The
    ///         hostile was last seen just inside the door.
    /// </summary>
    private async Task<Scene> SetUpLonerOutsideLRoom(string lonerMob)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var tacticsSystem = entManager.System<NpcSquadTacticsSystem>();
        var map = await Pair.CreateTestMap();

        EntityUid gridUid = default;
        EntityUid lonerUid = default;
        EntityUid targetUid = default;

        await server.WaitPost(() =>
        {
            tacticsSystem.UpdatesPaused = true;
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-12, -12), new Vector2i(12, 12)).Owner;

            for (var x = -1; x <= 9; x++)
            {
                for (var y = -1; y <= 8; y++)
                {
                    var longSide = x is >= 0 and <= 8 && y is >= 0 and <= 2;
                    if (longSide || IsInLeg(new Vector2i(x, y)))
                        continue;

                    if (x == 2 && y == -1)
                        SpawnPoweredDoorAt(entManager, "Airlock", gridUid, x, y);
                    else
                        SpawnAt(entManager, "WallSolid", gridUid, x, y);
                }
            }

            lonerUid = SpawnAt(entManager, lonerMob, gridUid, 2, -4);
            targetUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 11, -11);
        });

        await Pair.RunTicksSync(150);

        await server.WaitPost(() => entManager.System<NPCSystem>().WakeNPC(lonerUid));

        return new Scene(entManager,
            tacticsSystem,
            entManager.System<NpcPerceptionSystem>(),
            entManager.System<SharedTransformSystem>(),
            gridUid,
            SquadUid: default,
            LeaderUid: default,
            MemberUid: default,
            lonerUid,
            targetUid,
            new EntityCoordinates(gridUid, new Vector2(2.5f, 1.5f)));
    }

    /// <summary>
    ///     The room, a squad of two waiting just outside its west airlock, a hostile far away, and the tactics system
    ///         paused. Optionally an NPC on its own, which never joins a squad and skips the watch.
    /// </summary>
    private async Task<Scene> SetUpSquadOutsideRoom(string squadMob = SyndicateMob,
        Vector2i? lonerAt = null,
        Vector2i[]? squadAt = null,
        bool northDoor = false,
        string westDoor = "Airlock",
        string eastDoor = "Airlock",
        string? secondMob = null,
        bool firelocks = false)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var tacticsSystem = entManager.System<NpcSquadTacticsSystem>();
        var map = await Pair.CreateTestMap();

        EntityUid gridUid = default;
        EntityUid firstUid = default;
        EntityUid secondUid = default;
        EntityUid lonerUid = default;
        EntityUid targetUid = default;

        await server.WaitPost(() =>
        {
            tacticsSystem.UpdatesPaused = true;
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-12, -12), new Vector2i(12, 12)).Owner;

            for (var x = -1; x <= 7; x++)
            {
                for (var y = -1; y <= 6; y++)
                {
                    if (x is > -1 and < 7 && y is > -1 and < 6)
                        continue;

                    var door = x == -1 && y == 2 ? westDoor
                        : x == 7 && y == 3 ? eastDoor
                        : northDoor && x == 3 && y == 6 ? "Airlock"
                        : null;
                    // A firelock under a door, as on a station: spawned first, so it is the first door found there.
                    if (door != null && firelocks)
                        SpawnAt(entManager, "Firelock", gridUid, x, y);

                    if (door != null)
                        SpawnPoweredDoorAt(entManager, door, gridUid, x, y);
                    else
                        SpawnAt(entManager, "WallSolid", gridUid, x, y);
                }
            }

            var firstTile = squadAt?[0] ?? new Vector2i(-4, 2);
            var secondTile = squadAt?[1] ?? new Vector2i(-4, 3);
            firstUid = SpawnAt(entManager, squadMob, gridUid, firstTile.X, firstTile.Y);
            secondUid = SpawnAt(entManager, secondMob ?? squadMob, gridUid, secondTile.X, secondTile.Y);

            if (lonerAt is { } lonerTile)
                lonerUid = SpawnAt(entManager, NoWatchLonerMob, gridUid, lonerTile.X, lonerTile.Y);

            targetUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 11, -11);
        });

        // Long enough for the navmesh to build and the squad to form.
        await Pair.RunTicksSync(150);

        EntityUid squadUid = default;
        EntityUid leaderUid = default;

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.System<NpcSquadSystem>().TryGetSquad(firstUid, out var squadEntity), "the two should form a squad");
            squadUid = squadEntity!.Value.Owner;
            leaderUid = squadEntity.Value.Comp.Leader!.Value;
        });

        return new Scene(entManager,
            tacticsSystem,
            entManager.System<NpcPerceptionSystem>(),
            entManager.System<SharedTransformSystem>(),
            gridUid,
            squadUid,
            leaderUid,
            leaderUid == firstUid ? secondUid : firstUid,
            lonerUid,
            targetUid,
            new EntityCoordinates(gridUid, new Vector2(3.5f, 2.5f)));
    }
}
