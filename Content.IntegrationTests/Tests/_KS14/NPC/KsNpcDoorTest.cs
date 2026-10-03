#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Doors;
using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Doors;
using Content.Server._KS14.NPC.Squad;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
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
    private const string DressedBreacherMob = "KsDoorTestMobDressedBreacher";
    private const string Wieldable = "KsDoorTestWieldable";
    private const string EmptyHandedWalkerMob = "KsDoorTestMobEmptyHandedWalker";
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
    ///         it with. It finds that out at the door, and goes the long way round rather than walking back into the same
    ///         door for ever: the door is a wall to its own paths for a while.
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

        // Steering gives up at the door.
        var reachedDoor = false;
        for (var i = 0; i < 300 && !reachedDoor; i++)
        {
            await Pair.RunTicksSync(1);
            await Pair.Server.WaitPost(() => reachedDoor = doorSystem.IsBlocked(walkerUid, doorUid));
        }

        Assert.That(reachedDoor, "it should have found the door would not let it through");

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
