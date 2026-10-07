#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._KS14.NPC.Doors;
using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Doors;
using Content.Server._KS14.NPC.Squad;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
using Content.Shared._KS14.CCVar;
using Content.Shared.Access;
using Content.Shared.Access.Systems;
using Content.Shared.Charges.Systems;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Hands.Components;
using Content.Shared.NPC;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     NPCs and doors (<see cref="NpcDoorSystem"/>): what an NPC believes it can open - going by what a door shows, so it
///         can be fooled - what it can force a door with, remembering doors that fooled it for its whole squad, forcing
///         a door and putting the tool away after (<see cref="BreachDoorOperator"/>), and pathfinding round bolted
///         doors.
/// </summary>
public sealed class KsNpcDoorTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string SecurityMob = "KsDoorTestMobSecurity";
    private const string NoAccessMob = "KsDoorTestMobNoAccess";
    private const string ForgetfulMob = "KsDoorTestMobForgetful";
    private const string WalkerMob = "KsDoorTestMobWalker";
    private const string BreacherMob = "KsDoorTestMobBreacher";
    private const string BlockedWalkerMob = "KsDoorTestMobBlockedWalker";
    private const string PatientWalkerMob = "KsDoorTestMobPatientWalker";
    private const string RoundaboutWalkerMob = "KsDoorTestMobRoundaboutWalker";
    private const string SecretiveMob = "KsDoorTestMobSecretive";
    private const string DressedBreacherMob = "KsDoorTestMobDressedBreacher";
    private const string Wieldable = "KsDoorTestWieldable";
    private const string EmptyHandedWalkerMob = "KsDoorTestMobEmptyHandedWalker";
    private const string EngineerWalkerMob = "KsDoorTestMobEngineerWalker";
    private const string PryingWalkerMob = "KsDoorTestMobPryingWalker";
    private const string SecurityDoor = "AirlockSecurityLocked";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsDoorTestMobSecurity
  components:
  - type: Hands
  - type: Access
    tags:
    - Security

- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsDoorTestMobNoAccess
  components:
  - type: Hands

- type: entity
  parent: KsDoorTestMobSecurity
  id: KsDoorTestMobForgetful
  components:
  - type: NpcDoorUser
    forgetAfter: 1s

- type: entity
  parent: KsDoorTestMobSecurity
  id: KsDoorTestMobSecretive
  components:
  - type: NpcDoorUser
    warnsSquad: false

- type: entity
  parent: KsDoorTestMobSecurity
  id: KsDoorTestMobWalker
  components:
  - type: InputMover
  - type: MobMover
  - type: MovementSpeedModifier

- type: entity
  parent: KsDoorTestMobNoAccess
  id: KsDoorTestMobBreacher
  components:
  - type: DoAfter

- type: entity
  parent: KsDoorTestMobBreacher
  id: KsDoorTestMobBlockedWalker
  components:
  - type: InputMover
  - type: MobMover
  - type: MovementSpeedModifier
  - type: NpcDoorUser
    breachWhenBlocked: true

- type: entity
  parent: KsDoorTestMobBreacher
  id: KsDoorTestMobDressedBreacher
  components:
  - type: Inventory
    templateId: human
  - type: ContainerContainer

- type: entity
  parent: KsDoorTestMobNoAccess
  id: KsDoorTestMobEmptyHandedWalker
  components:
  - type: InputMover
  - type: MobMover
  - type: MovementSpeedModifier
  - type: NpcDoorUser
    blockedForgetAfter: 30s

- type: entity
  id: KsDoorTestWieldable
  components:
  - type: Item
    size: Large
  - type: Wieldable

- type: htnCompound
  id: KsDoorTestBackgroundMoveRoot
  branches:
  - tasks:
    - !type:HTNPrimitiveTask
      operator: !type:MoveToOperator
        shutdownState: PlanFinished
        pathfindInPlanning: false
        removeKeyOnFinish: false
        targetKey: KsDoorTestTarget
    - !type:HTNPrimitiveTask
      operator: !type:StaticWaitOperator
        key: KsDoorTestWait

- type: htnCompound
  id: KsDoorTestForegroundMoveRoot
  branches:
  - tasks:
    - !type:HTNPrimitiveTask
      operator: !type:MoveToOperator
        pathfindInPlanning: false
        removeKeyOnFinish: false
        targetKey: KsDoorTestTarget
    - !type:HTNPrimitiveTask
      operator: !type:StaticWaitOperator
        key: KsDoorTestWait

- type: entity
  parent: Airlock
  id: KsDoorTestSlowAirlock
  components:
  - type: Door
    openTimeOne: 5

- type: entity
  parent: KsDoorTestMobBlockedWalker
  id: KsDoorTestMobRoundaboutWalker
  components:
  - type: NpcDoorUser
    maxDetourExtraDistance: 60

- type: entity
  parent: KsDoorTestMobNoAccess
  id: KsDoorTestMobEngineerWalker
  components:
  - type: Access
    tags:
    - Engineering
  - type: InputMover
  - type: MobMover
  - type: MovementSpeedModifier
  - type: NpcDoorUser
    maxDetourExtraDistance: 60

- type: entity
  parent: KsDoorTestMobNoAccess
  id: KsDoorTestMobPryingWalker
  components:
  - type: InputMover
  - type: MobMover
  - type: MovementSpeedModifier
  - type: DoAfter
  - type: Prying
    speedModifier: 10
  - type: NpcDoorUser
    maxDetourExtraDistance: 60 # would go round, if going round were for it

- type: entity
  parent: KsDoorTestMobBlockedWalker
  id: KsDoorTestMobPatientWalker
  components:
  - type: NpcDoorUser
    breachWhenBlocked: false
";

    /// <summary>
    ///     An NPC goes by the access a door was built with - what examining it shows - not what it was changed to since.
    ///         With the access, it believes it can open the door, and still does once the door's access is changed
    ///         behind its back; without, it does not. Emergency access lets anyone through; bolts, nobody.
    /// </summary>
    [Test]
    public async Task TestBeliefFollowsAdvertisedAccess()
    {
        var (entManager, gridUid) = await SetUpGrid();
        var doorSystem = entManager.System<NpcDoorSystem>();
        EntityUid doorUid = default, securityUid = default, nobodyUid = default;

        await Pair.Server.WaitPost(() =>
        {
            doorUid = SpawnPoweredDoorAt(entManager, SecurityDoor, gridUid, 2, 0);
            securityUid = SpawnAt(entManager, SecurityMob, gridUid, 0, 0);
            nobodyUid = SpawnAt(entManager, NoAccessMob, gridUid, 0, 2);
        });

        await Pair.RunTicksSync(10);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(doorSystem.GetDoorAccess(securityUid, doorUid), Is.EqualTo(NpcDoorAccess.Openable), "it has the access");
                Assert.That(doorSystem.GetDoorAccess(nobodyUid, doorUid), Is.EqualTo(NpcDoorAccess.Locked), "it does not");
            });

            // The door's access changed behind their backs: it no longer lets security through.
            var accessReaderSystem = entManager.System<AccessReaderSystem>();
            Assert.That(accessReaderSystem.GetMainAccessReader(doorUid, out var readerEntity));
            accessReaderSystem.TrySetAccesses(readerEntity!.Value, new List<ProtoId<AccessLevelPrototype>> { "Command" });

            Assert.Multiple(() =>
            {
                Assert.That(entManager.System<SharedDoorSystem>().CanOpen(doorUid, user: securityUid, quiet: true), Is.False,
                    "the door should really refuse it now");
                Assert.That(doorSystem.GetDoorAccess(securityUid, doorUid), Is.EqualTo(NpcDoorAccess.Openable),
                    "it should still believe what the door shows, and be fooled");
            });

            var airlockSystem = entManager.System<SharedAirlockSystem>();
            airlockSystem.SetEmergencyAccess((doorUid, entManager.GetComponent<AirlockComponent>(doorUid)), true);
            Assert.That(doorSystem.GetDoorAccess(nobodyUid, doorUid), Is.EqualTo(NpcDoorAccess.Openable), "emergency access lets anyone through");
            airlockSystem.SetEmergencyAccess((doorUid, entManager.GetComponent<AirlockComponent>(doorUid)), false);

            entManager.System<SharedDoorSystem>().SetBoltsDown((doorUid, entManager.GetComponent<DoorBoltComponent>(doorUid)), true);
            Assert.That(doorSystem.GetDoorAccess(securityUid, doorUid), Is.EqualTo(NpcDoorAccess.Locked), "bolts stop everyone");
        });
    }

    /// <summary>
    ///     What forces a door is asked the way the game asks it: a crowbar pries only an unpowered door, jaws of life any,
    ///         and an access breaker a powered one, while it has charges left.
    /// </summary>
    [TestCase("KsTestCrowbar", false, true)]
    [TestCase("KsTestCrowbar", true, false)]
    [TestCase("KsTestJawsOfLife", false, true)]
    [TestCase("KsTestJawsOfLife", true, true)]
    [TestCase("KsTestAccessBreaker", true, true)]
    [TestCase("KsTestAccessBreaker", false, false)]
    public async Task TestBreachTools(string tool, bool powered, bool expected)
    {
        var (entManager, gridUid) = await SetUpGrid();
        var doorSystem = entManager.System<NpcDoorSystem>();
        EntityUid doorUid = default, npcUid = default, toolUid = default;

        await Pair.Server.WaitPost(() =>
        {
            doorUid = powered ? SpawnPoweredDoorAt(entManager, SecurityDoor, gridUid, 2, 0) : SpawnAt(entManager, SecurityDoor, gridUid, 2, 0);
            npcUid = SpawnAt(entManager, NoAccessMob, gridUid, 0, 0);
            toolUid = GiveInHand(entManager, npcUid, tool);
        });

        await Pair.RunTicksSync(10);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(doorSystem.TryGetBreachTool(npcUid, doorUid, out var foundUid, out _), Is.EqualTo(expected),
                $"{tool} on a {(powered ? "powered" : "unpowered")} door");
            if (expected)
                Assert.That(foundUid, Is.EqualTo(toolUid));

            if (tool == TestAccessBreaker && expected)
            {
                entManager.System<SharedChargesSystem>().SetCharges(toolUid, 0);
                Assert.That(doorSystem.TryGetBreachTool(npcUid, doorUid, out _, out _), Is.False, "not once its charges are spent");
            }
        });
    }

    /// <summary>
    ///     A door that refuses an NPC that believed it would open is a no-go for it and for its squadmate, and it has
    ///         something to say about it; the squadmate does not, and the same door is not reported twice. The memory
    ///         fades.
    /// </summary>
    [Test]
    public async Task TestRefusedDoorIsRememberedBySquad()
    {
        var (entManager, gridUid) = await SetUpGrid();
        var doorSystem = entManager.System<NpcDoorSystem>();
        EntityUid doorUid = default, firstUid = default, secondUid = default;

        await Pair.Server.WaitPost(() =>
        {
            doorUid = SpawnPoweredDoorAt(entManager, SecurityDoor, gridUid, 3, 0);
            firstUid = SpawnAt(entManager, ForgetfulMob, gridUid, 0, 0);
            secondUid = SpawnAt(entManager, ForgetfulMob, gridUid, 0, 1);
        });

        await Pair.RunTicksSync(90); // for the squad to form

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(entManager.System<NpcSquadSystem>().TryGetSquad(firstUid, out var squadEntity) && squadEntity.Value.Comp.Members.Count == 2,
                "the two should be in one squad");

            doorSystem.ReportRefused(firstUid, doorUid);
            Assert.Multiple(() =>
            {
                Assert.That(doorSystem.IsNoGo(firstUid, doorUid), "the one refused should remember the door");
                Assert.That(doorSystem.IsNoGo(secondUid, doorUid), "and so should its squadmate");
                Assert.That(doorSystem.GetDoorAccess(secondUid, doorUid), Is.EqualTo(NpcDoorAccess.Locked), "a no-go door is not one to count on");
                Assert.That(doorSystem.HasPendingRefusal(firstUid, System.TimeSpan.FromSeconds(5)), "the one refused should say so");
                Assert.That(doorSystem.HasPendingRefusal(secondUid, System.TimeSpan.FromSeconds(5)), Is.False, "its squadmate has nothing to say");
            });

            doorSystem.ClearPendingRefusal(firstUid);
            doorSystem.ReportRefused(firstUid, doorUid);
            Assert.That(doorSystem.HasPendingRefusal(firstUid, System.TimeSpan.FromSeconds(5)), Is.False, "a known no-go is not reported again");
        });

        await Pair.RunTicksSync(45); // past the second it is remembered for

        await Pair.Server.WaitAssertion(() =>
            Assert.That(doorSystem.IsNoGo(firstUid, doorUid), Is.False, "the memory should fade"));
    }

    /// <summary>
    ///     One that does not warn its squad keeps a door that fooled it to itself: its squadmate still counts on it.
    /// </summary>
    [Test]
    public async Task TestDoorWarningCanBeSwitchedOff()
    {
        var (entManager, gridUid) = await SetUpGrid();
        var doorSystem = entManager.System<NpcDoorSystem>();
        EntityUid doorUid = default, firstUid = default, secondUid = default;

        await Pair.Server.WaitPost(() =>
        {
            doorUid = SpawnPoweredDoorAt(entManager, SecurityDoor, gridUid, 3, 0);
            firstUid = SpawnAt(entManager, SecretiveMob, gridUid, 0, 0);
            secondUid = SpawnAt(entManager, SecurityMob, gridUid, 0, 1);
        });

        await Pair.RunTicksSync(90); // for the squad to form

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(entManager.System<NpcSquadSystem>().TryGetSquad(firstUid, out var squadEntity) && squadEntity.Value.Comp.Members.Count == 2,
                "the two should be in one squad");

            doorSystem.ReportRefused(firstUid, doorUid);
            Assert.Multiple(() =>
            {
                Assert.That(doorSystem.IsNoGo(firstUid, doorUid), "the one refused should remember the door");
                Assert.That(doorSystem.IsNoGo(secondUid, doorUid), Is.False, "its squadmate should not have been told");
            });
        });
    }

    /// <summary>
    ///     Live: an NPC walking through a door whose access was taken from it - while it still shows the old access - is
    ///         refused, and knows the door for a no-go from then on.
    /// </summary>
    [Test]
    public async Task TestWalkingIntoAFoolingDoorMarksIt()
    {
        var (entManager, gridUid) = await SetUpGrid();
        var doorSystem = entManager.System<NpcDoorSystem>();
        EntityUid doorUid = default, walkerUid = default;

        await Pair.Server.WaitPost(() =>
        {
            // A wall across the grid, with the door the only way through.
            for (var y = -6; y <= 6; y++)
            {
                if (y == 0)
                    doorUid = SpawnPoweredDoorAt(entManager, SecurityDoor, gridUid, 2, 0);
                else
                    SpawnAt(entManager, "WallSolid", gridUid, 2, y);
            }

            walkerUid = SpawnAt(entManager, WalkerMob, gridUid, 0, 0);
        });

        await Pair.RunTicksSync(90); // power and navmesh

        await Pair.Server.WaitPost(() =>
        {
            var accessReaderSystem = entManager.System<AccessReaderSystem>();
            accessReaderSystem.GetMainAccessReader(doorUid, out var readerEntity);
            accessReaderSystem.TrySetAccesses(readerEntity!.Value, new List<ProtoId<AccessLevelPrototype>> { "Command" });

            var htnComponent = entManager.GetComponent<HTNComponent>(walkerUid);
            htnComponent.Blackboard.SetValue(NPCBlackboard.NavInteract, true);
            entManager.System<NPCSteeringSystem>().Register(walkerUid, new EntityCoordinates(gridUid, new Vector2(4.5f, 0.5f)));
            entManager.System<NPCSystem>().WakeNPC(walkerUid, htnComponent);
        });

        await Pair.RunTicksSync(120);

        await Pair.Server.WaitAssertion(() =>
            Assert.That(doorSystem.IsNoGo(walkerUid, doorUid), "walking into the door should have shown it up as a no-go"));
    }

    /// <summary>
    ///     The other way round from being fooled: a security door changed to let engineers through still shows security
    ///         access, so an engineer believes it is locked to it. With a way round, it goes round, never having tried the
    ///         door - it cannot know it would open. With none, the door is the only way: it tries it, and gets through.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestNewlyGrantedAccessIsFoundOutByTrying(bool wayRound)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default, walkerUid = default, doorUid = default;

        await server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-3, -1), new Vector2i(7, 13)).Owner;

            for (var y = -1; y <= (wayRound ? 12 : 13); y++)
            {
                if (y == 0)
                    doorUid = SpawnPoweredDoorAt(entManager, SecurityDoor, gridUid, 2, 0);
                else
                    SpawnAt(entManager, "WallSolid", gridUid, 2, y);
            }

            walkerUid = SpawnAt(entManager, EngineerWalkerMob, gridUid, 0, 0);
        });

        await Pair.RunTicksSync(90); // power and navmesh

        await server.WaitPost(() =>
        {
            var accessReaderSystem = entManager.System<AccessReaderSystem>();
            accessReaderSystem.GetMainAccessReader(doorUid, out var readerEntity);
            accessReaderSystem.TrySetAccesses(readerEntity!.Value, new List<ProtoId<AccessLevelPrototype>> { "Engineering" });
            Assert.That(accessReaderSystem.IsAllowed(walkerUid, doorUid), "the door should let it through now");
            Assert.That(entManager.System<NpcDoorSystem>().GetDoorAccess(walkerUid, doorUid), Is.EqualTo(NpcDoorAccess.Locked),
                "but it should not know that");

            var htnComponent = entManager.GetComponent<HTNComponent>(walkerUid);
            htnComponent.Blackboard.SetValue(NPCBlackboard.NavInteract, true);
            entManager.System<NPCSteeringSystem>().Register(walkerUid, new EntityCoordinates(gridUid, new Vector2(4.5f, 0.5f)));
            entManager.System<NPCSystem>().WakeNPC(walkerUid, htnComponent);
        });

        // Tracked as it goes: the door shuts again behind it.
        var doorOpened = false;
        for (var i = 0; i < 30; i++)
        {
            await Pair.RunTicksSync(30);
            await server.WaitPost(() => doorOpened |= entManager.GetComponent<DoorComponent>(doorUid).State != DoorState.Closed);
        }

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(doorOpened, Is.EqualTo(!wayRound),
                    wayRound ? "with a way round, it should never have tried the door" : "with no way round, it should have tried the door");
                Assert.That(entManager.System<SharedTransformSystem>().GetWorldPosition(walkerUid).X, Is.GreaterThan(3f),
                    "it should have got there");
            });
        });
    }

    /// <summary>
    ///     An NPC that pries its way through - a xeno, say, with <c>NavPry</c> - is not put off by a door it believes is
    ///         locked: it pries it, as it always has, rather than taking the long way round.
    /// </summary>
    [Test]
    public async Task TestPryingNpcDoesNotGoRoundALockedDoor()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default, walkerUid = default, doorUid = default;

        await server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-3, -1), new Vector2i(7, 13)).Owner;

            // A gap at the top, round which is far longer.
            for (var y = -1; y <= 12; y++)
            {
                if (y == 0)
                    doorUid = SpawnAt(entManager, SecurityDoor, gridUid, 2, 0); // unpowered: pried by anything that pries
                else
                    SpawnAt(entManager, "WallSolid", gridUid, 2, y);
            }

            walkerUid = SpawnAt(entManager, PryingWalkerMob, gridUid, 0, 0);
        });

        await Pair.RunTicksSync(90); // navmesh

        await server.WaitPost(() =>
        {
            Assert.That(entManager.System<NpcDoorSystem>().GetDoorAccess(walkerUid, doorUid), Is.EqualTo(NpcDoorAccess.Locked));

            var htnComponent = entManager.GetComponent<HTNComponent>(walkerUid);
            htnComponent.Blackboard.SetValue(NPCBlackboard.NavInteract, true);
            htnComponent.Blackboard.SetValue(NPCBlackboard.NavPry, true);
            entManager.System<NPCSteeringSystem>().Register(walkerUid, new EntityCoordinates(gridUid, new Vector2(4.5f, 0.5f)));
            entManager.System<NPCSystem>().WakeNPC(walkerUid, htnComponent);
        });

        var doorOpened = false;
        for (var i = 0; i < 20; i++)
        {
            await Pair.RunTicksSync(30);
            await server.WaitPost(() => doorOpened |= entManager.GetComponent<DoorComponent>(doorUid).State != DoorState.Closed);
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(doorOpened, "it should have pried the door, not gone round");
            Assert.That(entManager.System<SharedTransformSystem>().GetWorldPosition(walkerUid).X, Is.GreaterThan(3f), "and got there through it");
        });
    }

    /// <summary>
    ///     Forcing a door: the tool comes out of the belt it is carried in into a free hand, the door is pried open, and
    ///         the tool goes back into the belt with the hand that held the weapon active again. Cut short, the tool goes
    ///         back all the same.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestBreachDrawsUsesAndStowsTheTool(bool finish)
    {
        var (entManager, gridUid) = await SetUpGrid();
        var breachDoorOperator = new BreachDoorOperator();
        EntityUid doorUid = default, npcUid = default, crowbarUid = default, beltUid = default;
        var blackboard = new NPCBlackboard();

        await Pair.Server.WaitPost(() =>
        {
            entManager.EntitySysManager.DependencyCollection.InjectDependencies(breachDoorOperator, oneOff: true);

            doorUid = SpawnAt(entManager, SecurityDoor, gridUid, 1, 0); // unpowered: a crowbar pries it
            npcUid = SpawnAt(entManager, BreacherMob, gridUid, 0, 0);

            var handsSystem = entManager.System<SharedHandsSystem>();
            handsSystem.AddHand(npcUid, "right", HandLocation.Right);
            handsSystem.AddHand(npcUid, "left", HandLocation.Left);
            handsSystem.AddHand(npcUid, "middle", HandLocation.Middle);

            var weaponUid = entManager.SpawnEntity("WeaponPistolMk58", entManager.GetComponent<TransformComponent>(npcUid).Coordinates);
            Assert.That(handsSystem.TryPickup(npcUid, weaponUid, "right"));
            beltUid = entManager.SpawnEntity("ClothingBeltUtility", entManager.GetComponent<TransformComponent>(npcUid).Coordinates);
            Assert.That(handsSystem.TryPickup(npcUid, beltUid, "left"));
            crowbarUid = entManager.SpawnEntity("KsTestCrowbar", entManager.GetComponent<TransformComponent>(npcUid).Coordinates);
            Assert.That(entManager.System<SharedStorageSystem>().Insert(beltUid, crowbarUid, out _, playSound: false), "the crowbar should go in the belt");
            handsSystem.TrySetActiveHand(npcUid, "right"); // false if it already is
            Assert.That(handsSystem.GetActiveHand(npcUid), Is.EqualTo("right"));

            blackboard.SetValue(NPCBlackboard.Owner, npcUid);
            blackboard.SetValue("OrderTarget", doorUid);
            blackboard.SetValue("OrderTool", crowbarUid);
        });

        await Pair.RunTicksSync(5);

        await Pair.Server.WaitAssertion(() =>
        {
            breachDoorOperator.Startup(blackboard);
            var handsSystem = entManager.System<SharedHandsSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(handsSystem.IsHolding(npcUid, crowbarUid, out var hand) && hand == "middle", "the crowbar should be drawn into the free hand");
                Assert.That(handsSystem.GetActiveHand(npcUid), Is.EqualTo("middle"), "and used from it");
            });
        });

        var status = Content.Server.NPC.HTN.HTNOperatorStatus.Continuing;
        for (var i = 0; finish && i < 300 && status == Content.Server.NPC.HTN.HTNOperatorStatus.Continuing; i++)
        {
            await Pair.RunTicksSync(1);
            await Pair.Server.WaitPost(() => status = breachDoorOperator.Update(blackboard, 1f / 30f));
        }

        await Pair.Server.WaitAssertion(() =>
        {
            if (finish)
            {
                Assert.That(status, Is.EqualTo(Content.Server.NPC.HTN.HTNOperatorStatus.Finished), "the door should have been pried open");
                Assert.That(entManager.GetComponent<DoorComponent>(doorUid).State, Is.AnyOf(DoorState.Opening, DoorState.Open));
            }

            breachDoorOperator.TaskShutdown(blackboard, finish ? Content.Server.NPC.HTN.HTNOperatorStatus.Finished : Content.Server.NPC.HTN.HTNOperatorStatus.Failed);

            var handsSystem = entManager.System<SharedHandsSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(entManager.GetComponent<StorageComponent>(beltUid).Container.Contains(crowbarUid), "the crowbar should be back in the belt");
                Assert.That(handsSystem.GetActiveHand(npcUid), Is.EqualTo("right"), "with the weapon's hand active again");
                Assert.That(entManager.HasComponent<NpcBreachingComponent>(npcUid), Is.False, "and nothing left over");
            });
        });
    }

    /// <summary>
    ///     Live, outside any squad order: an NPC whose only way on is a door that will not open for it - it has no
    ///         power - forces it with the crowbar on its belt, and puts the crowbar back after. One told not to
    ///         (<see cref="NpcDoorUserComponent.BreachWhenBlocked"/>) leaves the door shut.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestBlockedWayIsForced(bool breachWhenBlocked)
    {
        var (entManager, gridUid) = await SetUpGrid();
        EntityUid doorUid = default, walkerUid = default, crowbarUid = default, beltUid = default;

        await Pair.Server.WaitPost(() =>
        {
            // A wall across the grid, with the door the only way through.
            for (var y = -6; y <= 6; y++)
            {
                if (y == 0)
                    doorUid = SpawnAt(entManager, SecurityDoor, gridUid, 2, 0); // unpowered: a crowbar pries it
                else
                    SpawnAt(entManager, "WallSolid", gridUid, 2, y);
            }

            walkerUid = SpawnAt(entManager, breachWhenBlocked ? BlockedWalkerMob : PatientWalkerMob, gridUid, 0, 0);

            var handsSystem = entManager.System<SharedHandsSystem>();
            handsSystem.AddHand(walkerUid, "right", HandLocation.Right);
            handsSystem.AddHand(walkerUid, "left", HandLocation.Left);
            beltUid = entManager.SpawnEntity("ClothingBeltUtility", entManager.GetComponent<TransformComponent>(walkerUid).Coordinates);
            Assert.That(handsSystem.TryPickup(walkerUid, beltUid, "left"));
            crowbarUid = entManager.SpawnEntity("KsTestCrowbar", entManager.GetComponent<TransformComponent>(walkerUid).Coordinates);
            Assert.That(entManager.System<SharedStorageSystem>().Insert(beltUid, crowbarUid, out _, playSound: false), "the crowbar should go in the belt");
        });

        await Pair.RunTicksSync(90); // navmesh

        await Pair.Server.WaitPost(() =>
        {
            var htnComponent = entManager.GetComponent<HTNComponent>(walkerUid);
            htnComponent.Blackboard.SetValue(NPCBlackboard.NavInteract, true);
            entManager.System<NPCSteeringSystem>().Register(walkerUid, new EntityCoordinates(gridUid, new Vector2(4.5f, 0.5f)));
            entManager.System<NPCSystem>().WakeNPC(walkerUid, htnComponent);
        });

        await Pair.RunTicksSync(240);

        await Pair.Server.WaitAssertion(() =>
        {
            var doorState = entManager.GetComponent<DoorComponent>(doorUid).State;
            if (!breachWhenBlocked)
            {
                Assert.That(doorState, Is.EqualTo(DoorState.Closed), "told not to force doors, it should have left it shut");
                return;
            }

            Assert.Multiple(() =>
            {
                Assert.That(doorState, Is.AnyOf(DoorState.Opening, DoorState.Open), "it should have pried the door open");
                Assert.That(entManager.GetComponent<StorageComponent>(beltUid).Container.Contains(crowbarUid), "and put the crowbar back");
                Assert.That(entManager.HasComponent<NpcBreachingComponent>(walkerUid), Is.False, "with nothing left over");
            });
        });
    }

    /// <summary>
    ///     A door shut to the NPC is a shortcut, not the only way: there is a way round, longer than the door is worth to
    ///         the pathfinder, so its first path runs through the door. It goes round rather than force the door, which
    ///         stays shut, with the crowbar still on its belt. <see cref="TestBlockedWayIsForced"/> is the other half:
    ///         with no way round, it forces the door. This walker goes further out of its way than most, as the way round
    ///         here is long; see <see cref="TestLongWayRoundIsNotTaken"/>.
    /// </summary>
    [Test]
    public async Task TestShortcutDoorIsGoneRoundNotForced()
    {
        var (entManager, walkerUid, crowbarUid, beltUid, doorUids) = await WalkPastWall(RoundaboutWalkerMob, doorRows: [0]);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entManager.GetComponent<DoorComponent>(doorUids[0]).State, Is.EqualTo(DoorState.Closed),
                    "with a way round, it should have left the door shut");
                Assert.That(entManager.GetComponent<StorageComponent>(beltUid).Container.Contains(crowbarUid),
                    "and never taken the crowbar out");
                Assert.That(entManager.System<SharedTransformSystem>().GetWorldPosition(walkerUid).X, Is.GreaterThan(3f),
                    "it should have got there the long way");
            });
        });
    }

    /// <summary>
    ///     The same wall, walked by an NPC that goes no more than 15 tiles out of its way: round by the gap is further
    ///         than that, so it forces the door.
    /// </summary>
    [Test]
    public async Task TestLongWayRoundIsNotTaken()
    {
        var (entManager, walkerUid, _, _, doorUids) = await WalkPastWall(BlockedWalkerMob, doorRows: [0]);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entManager.GetComponent<DoorComponent>(doorUids[0]).State, Is.Not.EqualTo(DoorState.Closed),
                    "the way round is too long: it should have forced the door");
                Assert.That(entManager.System<SharedTransformSystem>().GetWorldPosition(walkerUid).X, Is.GreaterThan(3f),
                    "and got there through it");
            });
        });
    }

    /// <summary>
    ///     A way round that runs through another door shut to the NPC is no way round: it forces the door in its way,
    ///         not the other. Going round that one as well used to walk it from door to door until there was nothing
    ///         left to go round, and then it forced whichever it was standing at. No gap at the top: the pathfinder
    ///         would rather walk round it than through a door it has to pry, so the other door would never be offered.
    /// </summary>
    [Test]
    public async Task TestWayRoundThroughAnotherShutDoorIsNotTaken()
    {
        var (entManager, walkerUid, _, _, doorUids) = await WalkPastWall(RoundaboutWalkerMob, doorRows: [0, 4], gapAtTop: false);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entManager.GetComponent<DoorComponent>(doorUids[0]).State, Is.Not.EqualTo(DoorState.Closed),
                    "it should have forced the door in its way");
                Assert.That(entManager.GetComponent<DoorComponent>(doorUids[1]).State, Is.EqualTo(DoorState.Closed),
                    "and not the one on the way round");
                Assert.That(entManager.System<SharedTransformSystem>().GetWorldPosition(walkerUid).X, Is.GreaterThan(3f),
                    "and got there through it");
            });
        });
    }

    /// <summary>
    ///     An NPC with a crowbar on its belt walks from one side of a wall up a small grid to the other. The wall has
    ///         unpowered security doors in it at <paramref name="doorRows"/>, and with <paramref name="gapAtTop"/>, a gap
    ///         at the top, round which is far longer.
    /// </summary>
    private async Task<(IEntityManager EntManager, EntityUid WalkerUid, EntityUid CrowbarUid, EntityUid BeltUid, List<EntityUid> DoorUids)>
        WalkPastWall(string walkerMob, int[] doorRows, bool gapAtTop = true)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        var doorUids = new List<EntityUid>();
        EntityUid gridUid = default, walkerUid = default, crowbarUid = default, beltUid = default;

        await server.WaitPost(() =>
        {
            // Small, so the pathfinder's node limit is not what decides it.
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-3, -1), new Vector2i(7, 13)).Owner;

            for (var y = -1; y <= (gapAtTop ? 12 : 13); y++)
            {
                if (System.Array.IndexOf(doorRows, y) >= 0)
                    doorUids.Add(SpawnAt(entManager, SecurityDoor, gridUid, 2, y));
                else
                    SpawnAt(entManager, "WallSolid", gridUid, 2, y);
            }

            walkerUid = SpawnAt(entManager, walkerMob, gridUid, 0, 0);

            var handsSystem = entManager.System<SharedHandsSystem>();
            handsSystem.AddHand(walkerUid, "right", HandLocation.Right);
            handsSystem.AddHand(walkerUid, "left", HandLocation.Left);
            beltUid = entManager.SpawnEntity("ClothingBeltUtility", entManager.GetComponent<TransformComponent>(walkerUid).Coordinates);
            Assert.That(handsSystem.TryPickup(walkerUid, beltUid, "left"));
            crowbarUid = entManager.SpawnEntity("KsTestCrowbar", entManager.GetComponent<TransformComponent>(walkerUid).Coordinates);
            Assert.That(entManager.System<SharedStorageSystem>().Insert(beltUid, crowbarUid, out _, playSound: false), "the crowbar should go in the belt");
        });

        await Pair.RunTicksSync(90); // navmesh

        await server.WaitPost(() =>
        {
            var htnComponent = entManager.GetComponent<HTNComponent>(walkerUid);
            htnComponent.Blackboard.SetValue(NPCBlackboard.NavInteract, true);
            entManager.System<NPCSteeringSystem>().Register(walkerUid, new EntityCoordinates(gridUid, new Vector2(4.5f, 0.5f)));
            entManager.System<NPCSystem>().WakeNPC(walkerUid, htnComponent);
        });

        await Pair.RunTicksSync(900);
        return (entManager, walkerUid, crowbarUid, beltUid, doorUids);
    }

    /// <summary>
    ///     A move that is the plan's task at hand forces the door in its way. One carried on in the background - while a
    ///         later task, a wait standing in for a reload, has the NPC's hands - does not: forcing it there takes the tool
    ///         out under the reload, which throws it on the floor.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task TestOnlyAForegroundMoveForcesTheDoor(bool background)
    {
        var (entManager, gridUid) = await SetUpGrid();
        EntityUid doorUid = default, walkerUid = default, crowbarUid = default, beltUid = default;

        await Pair.Server.WaitPost(() =>
        {
            for (var y = -6; y <= 6; y++)
            {
                if (y == 0)
                    doorUid = SpawnAt(entManager, SecurityDoor, gridUid, 2, 0); // unpowered: a crowbar pries it
                else
                    SpawnAt(entManager, "WallSolid", gridUid, 2, y);
            }

            walkerUid = SpawnAt(entManager, BlockedWalkerMob, gridUid, 0, 0);

            var handsSystem = entManager.System<SharedHandsSystem>();
            handsSystem.AddHand(walkerUid, "right", HandLocation.Right);
            handsSystem.AddHand(walkerUid, "left", HandLocation.Left);
            beltUid = entManager.SpawnEntity("ClothingBeltUtility", entManager.GetComponent<TransformComponent>(walkerUid).Coordinates);
            Assert.That(handsSystem.TryPickup(walkerUid, beltUid, "left"));
            crowbarUid = entManager.SpawnEntity("KsTestCrowbar", entManager.GetComponent<TransformComponent>(walkerUid).Coordinates);
            Assert.That(entManager.System<SharedStorageSystem>().Insert(beltUid, crowbarUid, out _, playSound: false), "the crowbar should go in the belt");
        });

        await Pair.RunTicksSync(90); // navmesh

        await Pair.Server.WaitPost(() =>
        {
            var htnComponent = entManager.GetComponent<HTNComponent>(walkerUid);
            htnComponent.Blackboard.SetValue(NPCBlackboard.NavInteract, true);
            htnComponent.Blackboard.SetValue("KsDoorTestTarget", new EntityCoordinates(gridUid, new Vector2(4.5f, 0.5f)));
            htnComponent.Blackboard.SetValue("KsDoorTestWait", 60f);
            htnComponent.RootTask = new HTNCompoundTask { Task = background ? "KsDoorTestBackgroundMoveRoot" : "KsDoorTestForegroundMoveRoot" };
            entManager.System<HTNSystem>().SetHTNEnabled((walkerUid, htnComponent), true);
            entManager.System<NPCSystem>().WakeNPC(walkerUid, htnComponent);
        });

        await Pair.RunTicksSync(240);

        await Pair.Server.WaitAssertion(() =>
        {
            var doorState = entManager.GetComponent<DoorComponent>(doorUid).State;
            Assert.Multiple(() =>
            {
                if (background)
                {
                    Assert.That(entManager.HasComponent<Content.Server.NPC.Components.NPCSteeringComponent>(walkerUid), Is.True,
                        "it should still be trying to get there in the background, or the move ended for some other reason");
                    Assert.That(doorState, Is.EqualTo(DoorState.Closed), "moving in the background, it should have left the door shut");
                }
                else
                {
                    Assert.That(doorState, Is.AnyOf(DoorState.Opening, DoorState.Open), "with the move its task, it should have forced the door");
                }

                Assert.That(entManager.GetComponent<StorageComponent>(beltUid).Container.Contains(crowbarUid), "the crowbar should be in the belt either way");
            });
        });
    }

    /// <summary>
    ///     A powered door forced open with jaws of life shuts again behind the NPC. Walking back the way it came, it
    ///         forces it again, rather than bumping into it until it gives up.
    /// </summary>
    [Test]
    public async Task TestDoorForcedOnTheWayInIsForcedOnTheWayBack()
    {
        var (entManager, gridUid) = await SetUpGrid();
        EntityUid doorUid = default, walkerUid = default;

        await Pair.Server.WaitPost(() =>
        {
            for (var y = -6; y <= 6; y++)
            {
                if (y == 0)
                    doorUid = SpawnPoweredDoorAt(entManager, SecurityDoor, gridUid, 2, 0); // powered: only jaws of life pry it
                else
                    SpawnAt(entManager, "WallSolid", gridUid, 2, y);
            }

            walkerUid = SpawnAt(entManager, BlockedWalkerMob, gridUid, 0, 0);

            var handsSystem = entManager.System<SharedHandsSystem>();
            handsSystem.AddHand(walkerUid, "right", HandLocation.Right);
            handsSystem.AddHand(walkerUid, "left", HandLocation.Left);
            var beltUid = entManager.SpawnEntity("ClothingBeltUtility", entManager.GetComponent<TransformComponent>(walkerUid).Coordinates);
            Assert.That(handsSystem.TryPickup(walkerUid, beltUid, "left"));
            var jawsUid = entManager.SpawnEntity("KsTestJawsOfLife", entManager.GetComponent<TransformComponent>(walkerUid).Coordinates);
            Assert.That(entManager.System<SharedStorageSystem>().Insert(beltUid, jawsUid, out _, playSound: false), "the jaws should go in the belt");
        });

        await Pair.RunTicksSync(90); // navmesh, and the door powered

        var steeringSystem = entManager.System<NPCSteeringSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();

        await Pair.Server.WaitPost(() =>
        {
            var htnComponent = entManager.GetComponent<HTNComponent>(walkerUid);
            htnComponent.Blackboard.SetValue(NPCBlackboard.NavInteract, true);
            steeringSystem.Register(walkerUid, new EntityCoordinates(gridUid, new Vector2(4.5f, 0.5f)));
            entManager.System<NPCSystem>().WakeNPC(walkerUid, htnComponent);
        });

        await Pair.RunTicksSync(600); // jaws of life take a while on a powered door

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(transformSystem.GetWorldPosition(walkerUid).X, Is.GreaterThan(3f), "it should have forced its way through");
            steeringSystem.Unregister(walkerUid);
        });

        // Until the door has shut behind it.
        for (var i = 0; i < 40 && entManager.GetComponent<DoorComponent>(doorUid).State != DoorState.Closed; i++)
        {
            await Pair.RunTicksSync(30);
        }

        Assert.That(entManager.GetComponent<DoorComponent>(doorUid).State, Is.EqualTo(DoorState.Closed), "the door should have shut again");

        await Pair.Server.WaitPost(() => steeringSystem.Register(walkerUid, new EntityCoordinates(gridUid, new Vector2(-0.5f, 0.5f))));
        await Pair.RunTicksSync(600);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(transformSystem.GetWorldPosition(walkerUid).X, Is.LessThan(1f), "it should have forced its way back through");
        });
    }

    /// <summary>
    ///     Forcing a door with both hands full of a wielded weapon and the tool on the belt. Wielding fills the second
    ///         hand with a virtual item, which is only deleted at the end of the tick once the weapon is unwielded, so
    ///         unwielding and then looking for an empty hand found none: the breach was given up at once, the weapon
    ///         wielded again, and the whole thing tried again the next tick, for ever. The tool has to be out in the
    ///         same tick the breach starts, and everything as it was once the door is open.
    /// </summary>
    [Test]
    public async Task TestBreachWithAWieldedWeapon()
    {
        var (entManager, gridUid) = await SetUpGrid();
        var doorSystem = entManager.System<NpcDoorSystem>();
        var handsSystem = entManager.System<SharedHandsSystem>();
        EntityUid doorUid = default, npcUid = default, crowbarUid = default, beltUid = default, weaponUid = default;

        await Pair.Server.WaitPost(() =>
        {
            doorUid = SpawnAt(entManager, SecurityDoor, gridUid, 1, 0); // unpowered: a crowbar pries it
            npcUid = SpawnAt(entManager, DressedBreacherMob, gridUid, 0, 0);
            handsSystem.AddHand(npcUid, "right", HandLocation.Right);
            handsSystem.AddHand(npcUid, "left", HandLocation.Left);

            var coordinates = entManager.GetComponent<TransformComponent>(npcUid).Coordinates;
            beltUid = entManager.SpawnEntity("ClothingBeltUtility", coordinates);
            Assert.That(entManager.System<Content.Shared.Inventory.InventorySystem>().TryEquip(npcUid, beltUid, "belt", silent: true, force: true),
                "the belt should be worn");
            crowbarUid = entManager.SpawnEntity("KsTestCrowbar", coordinates);
            Assert.That(entManager.System<SharedStorageSystem>().Insert(beltUid, crowbarUid, out _, playSound: false), "the crowbar should go in the belt");

            weaponUid = entManager.SpawnEntity(Wieldable, coordinates);
            Assert.That(handsSystem.TryPickup(npcUid, weaponUid, "right"));
            handsSystem.TrySetActiveHand(npcUid, "right"); // false if it already is
            Assert.That(entManager.System<SharedWieldableSystem>().TryWield((weaponUid, entManager.GetComponent<WieldableComponent>(weaponUid)), npcUid),
                "the weapon should be wielded, filling both hands");
        });

        await Pair.RunTicksSync(5);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(doorSystem.TryStartBreach(npcUid, doorUid, crowbarUid, NpcDoorSystem.DefaultBreachTimeout, fromSteering: false),
                "the breach should start with both hands full");
            Assert.Multiple(() =>
            {
                Assert.That(handsSystem.IsHolding(npcUid, crowbarUid, out var hand) && hand == handsSystem.GetActiveHand(npcUid),
                    "the crowbar should be out, in the active hand, at once");
                Assert.That(entManager.GetComponent<WieldableComponent>(weaponUid).Wielded, Is.False, "the weapon unwielded to free a hand");
                Assert.That(handsSystem.IsHolding(npcUid, weaponUid), "but still held");
            });
        });

        for (var i = 0; i < 300; i++)
        {
            await Pair.RunTicksSync(1);
            var done = false;
            await Pair.Server.WaitPost(() => done = !doorSystem.IsBreaching(npcUid));
            if (done)
                break;
        }

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entManager.GetComponent<DoorComponent>(doorUid).State, Is.AnyOf(DoorState.Opening, DoorState.Open), "the door should be pried open");
                Assert.That(entManager.GetComponent<StorageComponent>(beltUid).Container.Contains(crowbarUid), "the crowbar back in the belt");
                Assert.That(handsSystem.IsHolding(npcUid, weaponUid, out var hand) && hand == handsSystem.GetActiveHand(npcUid),
                    "the weapon in the active hand");
                Assert.That(entManager.GetComponent<WieldableComponent>(weaponUid).Wielded, "and wielded again");
            });
        });
    }

    /// <summary>
    ///     Live: the shortest way runs through a door that will not open for it - unpowered - and it has nothing to force
    ///         it with. It sees that at the door, and goes the long way round rather than walking back into the same door
    ///         for ever: the door is a wall to its own paths for a while.
    /// </summary>
    [Test]
    public async Task TestDoorThatBeatItIsGoneRound()
    {
        var (entManager, gridUid) = await SetUpGrid();
        var doorSystem = entManager.System<NpcDoorSystem>();
        EntityUid doorUid = default, walkerUid = default;

        await Pair.Server.WaitPost(() =>
        {
            // A wall across the grid: a public airlock with no power in the middle, and a gap at the far end.
            for (var y = -6; y <= 5; y++)
            {
                if (y == 0)
                    doorUid = SpawnAt(entManager, "Airlock", gridUid, 2, 0);
                else
                    SpawnAt(entManager, "WallSolid", gridUid, 2, y);
            }

            walkerUid = SpawnAt(entManager, EmptyHandedWalkerMob, gridUid, 0, 0);
        });

        await Pair.RunTicksSync(90); // navmesh

        await Pair.Server.WaitPost(() =>
        {
            var htnComponent = entManager.GetComponent<HTNComponent>(walkerUid);
            htnComponent.Blackboard.SetValue(NPCBlackboard.NavInteract, true);
            entManager.System<NPCSteeringSystem>().Register(walkerUid, new EntityCoordinates(gridUid, new Vector2(4.5f, 0.5f)));
            entManager.System<NPCSystem>().WakeNPC(walkerUid, htnComponent);
        });

        // Steering takes the door out of its paths at the door.
        var reachedDoor = false;
        var avoidedDoorUids = new List<EntityUid>();
        for (var i = 0; i < 300 && !reachedDoor; i++)
        {
            await Pair.RunTicksSync(1);
            await Pair.Server.WaitPost(() =>
            {
                avoidedDoorUids.Clear();
                doorSystem.GetBlockedDoors(walkerUid, avoidedDoorUids);
                reachedDoor = avoidedDoorUids.Contains(doorUid);
            });
        }

        Assert.That(reachedDoor, "it should have seen the door would not let it through");

        // Off again, as a fresh move would: steering that has given up stays given up.
        await Pair.Server.WaitPost(() =>
        {
            var steeringSystem = entManager.System<NPCSteeringSystem>();
            steeringSystem.Unregister(walkerUid);
            steeringSystem.Register(walkerUid, new EntityCoordinates(gridUid, new Vector2(4.5f, 0.5f)));
        });
        await Pair.RunTicksSync(600);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(entManager.GetComponent<DoorComponent>(doorUid).State, Is.EqualTo(DoorState.Closed), "the door never opened");
            Assert.That(entManager.GetComponent<TransformComponent>(walkerUid).LocalPosition.X, Is.GreaterThan(3f),
                "it should have gone round, through the gap");
        });
    }

    /// <summary>
    ///     A breach steering started for a door in the way ends the moment steering does - the walk replaced by something
    ///         more pressing - with the tool back on the belt there and then. Left to the end of the tick, whatever came
    ///         next ran its first tasks with the tool still in hand, and could drop it.
    /// </summary>
    [Test]
    public async Task TestSteeringBreachEndsWithSteering()
    {
        var (entManager, gridUid) = await SetUpGrid();
        var doorSystem = entManager.System<NpcDoorSystem>();
        EntityUid walkerUid = default, crowbarUid = default, beltUid = default;

        await Pair.Server.WaitPost(() =>
        {
            for (var y = -6; y <= 6; y++)
            {
                if (y == 0)
                    SpawnAt(entManager, SecurityDoor, gridUid, 2, 0); // unpowered: a crowbar pries it
                else
                    SpawnAt(entManager, "WallSolid", gridUid, 2, y);
            }

            walkerUid = SpawnAt(entManager, BlockedWalkerMob, gridUid, 0, 0);
            var handsSystem = entManager.System<SharedHandsSystem>();
            handsSystem.AddHand(walkerUid, "right", HandLocation.Right);
            handsSystem.AddHand(walkerUid, "left", HandLocation.Left);
            beltUid = entManager.SpawnEntity("ClothingBeltUtility", entManager.GetComponent<TransformComponent>(walkerUid).Coordinates);
            Assert.That(handsSystem.TryPickup(walkerUid, beltUid, "left"));
            crowbarUid = entManager.SpawnEntity("KsTestCrowbar", entManager.GetComponent<TransformComponent>(walkerUid).Coordinates);
            Assert.That(entManager.System<SharedStorageSystem>().Insert(beltUid, crowbarUid, out _, playSound: false));
        });

        await Pair.RunTicksSync(90); // navmesh

        await Pair.Server.WaitPost(() =>
        {
            var htnComponent = entManager.GetComponent<HTNComponent>(walkerUid);
            htnComponent.Blackboard.SetValue(NPCBlackboard.NavInteract, true);
            entManager.System<NPCSteeringSystem>().Register(walkerUid, new EntityCoordinates(gridUid, new Vector2(4.5f, 0.5f)));
            entManager.System<NPCSystem>().WakeNPC(walkerUid, htnComponent);
        });

        var breaching = false;
        for (var i = 0; i < 240 && !breaching; i++)
        {
            await Pair.RunTicksSync(1);
            await Pair.Server.WaitPost(() => breaching = doorSystem.IsBreaching(walkerUid));
        }

        Assert.That(breaching, "it should have started forcing the door on its way");

        await Pair.Server.WaitAssertion(() =>
        {
            entManager.System<NPCSteeringSystem>().Unregister(walkerUid);
            Assert.Multiple(() =>
            {
                Assert.That(doorSystem.IsBreaching(walkerUid), Is.False, "the breach should end with the walk, at once");
                Assert.That(entManager.GetComponent<StorageComponent>(beltUid).Container.Contains(crowbarUid), "with the crowbar back in the belt");
            });
        });
    }

    /// <summary>
    ///     Shutters over a window are not a way through, whatever opens the shutters: the window is still there. The
    ///         navmesh used to call any tile with a door on it a door, and so a way through - for paths, and for squad
    ///         tactics' rooms, which take their doorways from it. Shutters on their own are a door.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestShuttersOverAWindowAreNoDoorway(bool window)
    {
        var (entManager, gridUid) = await SetUpGrid();
        var pathfindingSystem = entManager.System<PathfindingSystem>();

        await Pair.Server.WaitPost(() =>
        {
            SpawnAt(entManager, "ShuttersNormal", gridUid, 2, 0);
            if (window)
                SpawnAt(entManager, "Window", gridUid, 2, 0);
        });

        await Pair.RunTicksSync(90);

        await Pair.Server.WaitAssertion(() =>
        {
            var poly = pathfindingSystem.GetPoly(new EntityCoordinates(gridUid, new Vector2(2.5f, 0.5f)));
            Assert.That(poly, Is.Not.Null);
            Assert.That((poly!.Data.Flags & PathfindingBreadcrumbFlag.Door) != 0, Is.EqualTo(!window),
                window ? "with a window under them, the shutters are no doorway" : "on their own, shutters are a door");
        });
    }

    /// <summary>
    ///     Live: shutters shut across the only way, which nobody opens by hand. An NPC with a crowbar pries them, as
    ///         anyone could, rather than skipping them for not being a door it could open.
    /// </summary>
    [Test]
    public async Task TestShuttersInTheWayArePried()
    {
        var (entManager, gridUid) = await SetUpGrid();
        EntityUid shuttersUid = default, walkerUid = default;

        await Pair.Server.WaitPost(() =>
        {
            for (var y = -6; y <= 6; y++)
            {
                if (y == 0)
                    shuttersUid = SpawnAt(entManager, "ShuttersNormal", gridUid, 2, 0);
                else
                    SpawnAt(entManager, "WallSolid", gridUid, 2, y);
            }

            walkerUid = SpawnAt(entManager, BlockedWalkerMob, gridUid, 0, 0);
            var handsSystem = entManager.System<SharedHandsSystem>();
            handsSystem.AddHand(walkerUid, "right", HandLocation.Right);
            handsSystem.AddHand(walkerUid, "left", HandLocation.Left);
            var crowbarUid = entManager.SpawnEntity("KsTestCrowbar", entManager.GetComponent<TransformComponent>(walkerUid).Coordinates);
            Assert.That(handsSystem.TryPickup(walkerUid, crowbarUid, "left"));
        });

        await Pair.RunTicksSync(90);

        await Pair.Server.WaitPost(() =>
        {
            Assert.That(entManager.GetComponent<DoorComponent>(shuttersUid).State, Is.EqualTo(DoorState.Closed), "the shutters should start shut");
            var htnComponent = entManager.GetComponent<HTNComponent>(walkerUid);
            htnComponent.Blackboard.SetValue(NPCBlackboard.NavInteract, true);
            entManager.System<NPCSteeringSystem>().Register(walkerUid, new EntityCoordinates(gridUid, new Vector2(4.5f, 0.5f)));
            entManager.System<NPCSystem>().WakeNPC(walkerUid, htnComponent);
        });

        await Pair.RunTicksSync(300);

        await Pair.Server.WaitAssertion(() =>
            Assert.That(entManager.GetComponent<DoorComponent>(shuttersUid).State, Is.AnyOf(DoorState.Opening, DoorState.Open),
                "it should have pried the shutters open"));
    }

    /// <summary>
    ///     Somebody else gets the door open, or destroys it, while an NPC is still prying at it. The breach ends there,
    ///         with the crowbar put away - and the door left open: prying a door that is already open shuts it.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task TestDoorOpenedOrDestroyedMidBreach(bool destroyed)
    {
        var (entManager, gridUid) = await SetUpGrid();
        var doorSystem = entManager.System<NpcDoorSystem>();
        EntityUid doorUid = default, npcUid = default, crowbarUid = default, beltUid = default;

        await Pair.Server.WaitPost(() =>
        {
            doorUid = SpawnAt(entManager, SecurityDoor, gridUid, 1, 0); // unpowered: a crowbar pries it
            npcUid = SpawnAt(entManager, BreacherMob, gridUid, 0, 0);
            var handsSystem = entManager.System<SharedHandsSystem>();
            handsSystem.AddHand(npcUid, "right", HandLocation.Right);
            handsSystem.AddHand(npcUid, "left", HandLocation.Left);
            var coordinates = entManager.GetComponent<TransformComponent>(npcUid).Coordinates;
            beltUid = entManager.SpawnEntity("ClothingBeltUtility", coordinates);
            Assert.That(handsSystem.TryPickup(npcUid, beltUid, "left"));
            crowbarUid = entManager.SpawnEntity("KsTestCrowbar", coordinates);
            Assert.That(entManager.System<SharedStorageSystem>().Insert(beltUid, crowbarUid, out _, playSound: false));
        });

        await Pair.RunTicksSync(5);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(doorSystem.TryStartBreach(npcUid, doorUid, crowbarUid, NpcDoorSystem.DefaultBreachTimeout, fromSteering: false));

            if (destroyed)
                entManager.DeleteEntity(doorUid);
            else
                entManager.System<SharedDoorSystem>().StartOpening(doorUid);
        });

        await Pair.RunTicksSync(150); // well past the pry

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(doorSystem.IsBreaching(npcUid), Is.False, "the breach should be over");
                Assert.That(entManager.GetComponent<StorageComponent>(beltUid).Container.Contains(crowbarUid), "with the crowbar back in the belt");
                if (!destroyed)
                    Assert.That(entManager.GetComponent<DoorComponent>(doorUid).State, Is.AnyOf(DoorState.Opening, DoorState.Open),
                        "and the door left open, not pried shut again");
            });
        });
    }

    /// <summary>
    ///     Shutters and blast doors never open by hand. Shutters can be pried, power or no power; an access breaker only
    ///         strips their access, which opens nothing, so it is no way through them. Blast doors cannot be pried at all.
    /// </summary>
    [TestCase("ShuttersNormal", "KsTestCrowbar", true)]
    [TestCase("ShuttersNormal", "KsTestJawsOfLife", true)]
    [TestCase("ShuttersNormal", "KsTestAccessBreaker", false)]
    [TestCase("BlastDoor", "KsTestCrowbar", false)]
    [TestCase("BlastDoor", "KsTestJawsOfLife", false)]
    [TestCase("BlastDoor", "KsTestAccessBreaker", false)]
    public async Task TestShutterAndBlastDoorTools(string door, string tool, bool expected)
    {
        var (entManager, gridUid) = await SetUpGrid();
        var doorSystem = entManager.System<NpcDoorSystem>();
        EntityUid doorUid = default, npcUid = default;

        await Pair.Server.WaitPost(() =>
        {
            doorUid = SpawnPoweredDoorAt(entManager, door, gridUid, 2, 0);
            npcUid = SpawnAt(entManager, NoAccessMob, gridUid, 0, 0);
            GiveInHand(entManager, npcUid, tool);
        });

        await Pair.RunTicksSync(10);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(entManager.GetComponent<DoorComponent>(doorUid).State, Is.EqualTo(DoorState.Closed), $"the {door} should start shut");
            Assert.Multiple(() =>
            {
                Assert.That(doorSystem.GetDoorAccess(npcUid, doorUid), Is.EqualTo(NpcDoorAccess.Locked), "nobody opens it by hand");
                Assert.That(doorSystem.TryGetBreachTool(npcUid, doorUid, out _, out _), Is.EqualTo(expected), $"{tool} on {door}");
            });
        });
    }

    /// <summary>
    ///     A bolted door is a wall to pathfinding, and a door again once unbolted. One bolted open - what an access
    ///         breaker leaves - is no wall at all.
    /// </summary>
    [Test]
    public async Task TestBoltedDoorIsAWallToPathfinding()
    {
        var (entManager, gridUid) = await SetUpGrid();
        var pathfindingSystem = entManager.System<PathfindingSystem>();
        EntityUid doorUid = default, npcUid = default;

        await Pair.Server.WaitPost(() =>
        {
            for (var y = -6; y <= 6; y++)
            {
                if (y == 0)
                    doorUid = SpawnPoweredDoorAt(entManager, "Airlock", gridUid, 2, 0);
                else
                    SpawnAt(entManager, "WallSolid", gridUid, 2, y);
            }

            npcUid = SpawnAt(entManager, SecurityMob, gridUid, 0, 0);
        });

        await Pair.RunTicksSync(90);

        Assert.That(await FindPath(entManager, pathfindingSystem, npcUid, gridUid), Is.EqualTo(PathResult.Path), "the door should be a way through");

        await Pair.Server.WaitPost(() =>
            entManager.System<SharedDoorSystem>().SetBoltsDown((doorUid, entManager.GetComponent<DoorBoltComponent>(doorUid)), true));
        await Pair.RunTicksSync(30);

        Assert.That(await FindPath(entManager, pathfindingSystem, npcUid, gridUid), Is.EqualTo(PathResult.NoPath), "bolted, it should not be");

        await Pair.Server.WaitPost(() =>
            entManager.System<SharedDoorSystem>().SetBoltsDown((doorUid, entManager.GetComponent<DoorBoltComponent>(doorUid)), false));
        await Pair.RunTicksSync(30);

        Assert.That(await FindPath(entManager, pathfindingSystem, npcUid, gridUid), Is.EqualTo(PathResult.Path), "unbolted, it should be again");

        await Pair.Server.WaitPost(() => entManager.System<SharedDoorSystem>().StartOpening(doorUid));
        await Pair.RunTicksSync(60);
        await Pair.Server.WaitPost(() =>
            entManager.System<SharedDoorSystem>().SetBoltsDown((doorUid, entManager.GetComponent<DoorBoltComponent>(doorUid)), true));
        await Pair.RunTicksSync(30);

        Assert.That(await FindPath(entManager, pathfindingSystem, npcUid, gridUid), Is.EqualTo(PathResult.Path), "bolted open, it should be a way through");
    }

    /// <summary>
    ///     An access breaker bolts the door it forces as it starts to open, while it is still solid. A navmesh rebuild
    ///         then - one is due every time anything changes on the grid, so often enough - must not make it a wall: it
    ///         sent the NPC that had just forced it off through some other door, until the door opened and the navmesh
    ///         was rebuilt again. This door stays solid for 5s once it starts opening, so the rebuild lands in that time.
    /// </summary>
    [Test]
    public async Task TestDoorBoltedOnItsWayOpenIsNoWall()
    {
        var (entManager, gridUid) = await SetUpGrid();
        var pathfindingSystem = entManager.System<PathfindingSystem>();
        EntityUid doorUid = default, npcUid = default;

        await Pair.Server.WaitPost(() =>
        {
            for (var y = -6; y <= 6; y++)
            {
                if (y == 0)
                    doorUid = SpawnPoweredDoorAt(entManager, "KsDoorTestSlowAirlock", gridUid, 2, 0);
                else
                    SpawnAt(entManager, "WallSolid", gridUid, 2, y);
            }

            npcUid = SpawnAt(entManager, SecurityMob, gridUid, 0, 0);
        });

        await Pair.RunTicksSync(90);

        // As an access breaker does it.
        await Pair.Server.WaitPost(() => Assert.That(entManager.System<SharedDoorSystem>().TryOpenAndBolt(doorUid), "the door should start to give"));
        await Pair.RunTicksSync(60);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(entManager.GetComponent<DoorComponent>(doorUid).State, Is.EqualTo(DoorState.Opening), "it should still be opening");
            Assert.That(entManager.GetComponent<DoorBoltComponent>(doorUid).BoltsDown, "and bolted");
        });

        Assert.That(await FindPath(entManager, pathfindingSystem, npcUid, gridUid), Is.EqualTo(PathResult.Path),
            "a door on its way open should be a way through, bolted or not");
    }

    /// <summary>
    ///     <c>klovn.npc.path_node_limit</c> sets how far a path search goes before giving up. At 1 it gives up at once,
    ///         even across a few tiles, and at the default the same path is found.
    /// </summary>
    [TestCase(1, false)]
    [TestCase(512, true)]
    public async Task TestPathNodeLimitIsSetByCvar(int nodeLimit, bool expectPath)
    {
        var (entManager, gridUid) = await SetUpGrid();
        var pathfindingSystem = entManager.System<PathfindingSystem>();
        EntityUid npcUid = default;

        await Pair.Server.WaitPost(() =>
        {
            // A wall in the way, with a gap at its top end: no straight line, so the search has to look round.
            for (var y = -6; y <= 4; y++)
            {
                SpawnAt(entManager, "WallSolid", gridUid, 2, y);
            }

            npcUid = SpawnAt(entManager, SecurityMob, gridUid, 0, 0);
        });

        await Pair.RunTicksSync(90);
        await OverrideCVar(Side.Server, KsCCVars.NpcPathNodeLimit, nodeLimit);

        Assert.That(await FindPath(entManager, pathfindingSystem, npcUid, gridUid), Is.EqualTo(expectPath ? PathResult.Path : PathResult.NoPath));
    }

    private async Task<PathResult> FindPath(IEntityManager entManager, PathfindingSystem pathfindingSystem, EntityUid npcUid, EntityUid gridUid)
    {
        System.Threading.Tasks.Task<PathResultEvent> pathTask = default!;
        await Pair.Server.WaitPost(() => pathTask = pathfindingSystem.GetPath(npcUid,
            entManager.GetComponent<TransformComponent>(npcUid).Coordinates,
            new EntityCoordinates(gridUid, new Vector2(4.5f, 0.5f)),
            0.5f,
            default,
            PathFlags.Interact));

        for (var i = 0; i < 120 && !pathTask.IsCompleted; i++)
        {
            await Pair.RunTicksSync(1);
        }

        Assert.That(pathTask.IsCompletedSuccessfully, "the path request never finished");
        return (await pathTask).Result;
    }

    private static EntityUid GiveInHand(IEntityManager entManager, EntityUid npcUid, string prototype)
    {
        var handsSystem = entManager.System<SharedHandsSystem>();
        handsSystem.AddHand(npcUid, "right", HandLocation.Right);
        var itemUid = entManager.SpawnEntity(prototype, entManager.GetComponent<TransformComponent>(npcUid).Coordinates);
        Assert.That(handsSystem.TryPickup(npcUid, itemUid, "right"), $"it should be holding the {prototype}");
        return itemUid;
    }

    private async Task<(IEntityManager EntManager, EntityUid GridUid)> SetUpGrid()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default;

        await server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-6, -6), new Vector2i(6, 6)).Owner;
        });

        return (entManager, gridUid);
    }
}
