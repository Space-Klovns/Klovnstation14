#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server.Atmos.EntitySystems;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared.Atmos.Components;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Stunnable;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     A burning NPC puts itself out, as a player would, where that makes sense. Fire is held still in these: on a grid with
///         no air it would go out by itself, and how fast rolling takes it down is the fire's own business.
/// </summary>
public sealed class KsNpcStopDropRollTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string Mob = "KsStopDropRollTestMob";
    private const string Holder = "KsStopDropRollTestHolder";
    private const string Fire = "KsStopDropRollTestFire";
    private const string Thing = "KsStopDropRollTestThing";
    private const string BusyKey = "KsStopDropRollTestBusy";

    /// <summary>
    ///     Long enough for a new navmesh chunk.
    /// </summary>
    private const int NavmeshTicks = 90;

    /// <summary>
    ///     Longer than it takes to notice it is alight.
    /// </summary>
    private const int NoticeTicks = 45;

    // Slow, so that a test can catch it on its way from a fire.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsStopDropRollTestMob
  components:
  - type: Flammable
    canResistFire: true
    damage:
      types:
        Heat: 1.5
  - type: Hands
  - type: StandingState
  - type: StatusEffectContainer
  - type: InputMover
  - type: MobMover
  - type: MovementSpeedModifier
    baseWalkSpeed: 0.2
    baseSprintSpeed: 0.2

- type: entity
  parent: KsStopDropRollTestMob
  id: KsStopDropRollTestHolder
  components:
  - type: NpcStopDropRoll
    keepsHold: true
    busyKey: KsStopDropRollTestBusy

- type: entity
  parent: KsStopDropRollTestMob
  id: KsStopDropRollTestUnable
  components:
  - type: Flammable
    canResistFire: false
    damage:
      types:
        Heat: 1.5

- type: entity
  id: KsStopDropRollTestFire
  components:
  - type: NpcTileDanger
    dangers: Fire

- type: entity
  id: KsStopDropRollTestThing
  components:
  - type: Item
";

    /// <summary>
    ///     It rolls past a stack, and on what is left of a fire it does not bother.
    /// </summary>
    [TestCase(2f, true)]
    [TestCase(0.5f, false)]
    public async Task TestRollsPastAThreshold(float fireStacks, bool rolls)
    {
        var (entManager, gridUid) = await SetUpGrid();
        EntityUid npcUid = default;

        await Pair.Server.WaitPost(() =>
        {
            npcUid = SpawnAt(entManager, Mob, gridUid, 0, 0);
            Ignite(entManager, npcUid, fireStacks);
            Wake(entManager, npcUid);
        });

        await Pair.RunTicksSync(NoticeTicks);
        Assert.That(IsRolling(entManager, npcUid), Is.EqualTo(rolls));
    }

    /// <summary>
    ///     Not everything flammable can put itself out.
    /// </summary>
    [Test]
    public async Task TestOnlyWhatCanResistFireRolls()
    {
        var (entManager, gridUid) = await SetUpGrid();
        EntityUid npcUid = default;

        await Pair.Server.WaitPost(() =>
        {
            npcUid = SpawnAt(entManager, "KsStopDropRollTestUnable", gridUid, 0, 0);
            Ignite(entManager, npcUid, 2f);
            Wake(entManager, npcUid);
        });

        await Pair.RunTicksSync(NoticeTicks);
        Assert.That(IsRolling(entManager, npcUid), Is.False);
    }

    /// <summary>
    ///     Only an NPC, and only one awake. A player's mob sleeps, and a player decides for themselves.
    /// </summary>
    [Test]
    public async Task TestAsleepItDoesNotRoll()
    {
        var (entManager, gridUid) = await SetUpGrid();
        EntityUid npcUid = default;

        await Pair.Server.WaitPost(() =>
        {
            npcUid = SpawnAt(entManager, Mob, gridUid, 0, 0);
            entManager.System<NPCSystem>().SleepNPC(npcUid, entManager.GetComponent<HTNComponent>(npcUid));
            Ignite(entManager, npcUid, 2f);
        });

        await Pair.RunTicksSync(NoticeTicks);
        Assert.That(IsRolling(entManager, npcUid), Is.False);
    }

    /// <summary>
    ///     Dropping everything is what rolling costs anything else, and it stuns all the same for one that keeps hold of its
    ///         things. Otherwise a lighter would disarm it.
    /// </summary>
    [TestCase(Mob, false)]
    [TestCase(Holder, true)]
    public async Task TestWhatItHoldsOnRolling(string mob, bool keepsHold)
    {
        var (entManager, gridUid) = await SetUpGrid();
        var handsSystem = entManager.System<SharedHandsSystem>();
        EntityUid npcUid = default;
        EntityUid thingUid = default;

        await Pair.Server.WaitPost(() =>
        {
            npcUid = SpawnAt(entManager, mob, gridUid, 0, 0);
            handsSystem.AddHand(npcUid, "right", HandLocation.Right);
            thingUid = SpawnAt(entManager, Thing, gridUid, 0, 0);
            Assert.That(handsSystem.TryPickup(npcUid, thingUid, "right"), "the test needs it holding something");

            Ignite(entManager, npcUid, 2f);
            Wake(entManager, npcUid);
        });

        await Pair.RunTicksSync(NoticeTicks);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(entManager.GetComponent<FlammableComponent>(npcUid).Resisting, "it should be rolling");
            Assert.That(entManager.HasComponent<StunnedComponent>(npcUid), "rolling should stun it");
            Assert.That(handsSystem.IsHolding(npcUid, thingUid), Is.EqualTo(keepsHold));
        });
    }

    /// <summary>
    ///     On the fire that lit it, rolling gets it nowhere. It waits until it is off it, for an NPC that avoids fire or not.
    /// </summary>
    [Test]
    public async Task TestNotOnAFire()
    {
        var (entManager, gridUid) = await SetUpGrid();
        EntityUid npcUid = default, fireUid = default;

        await Pair.Server.WaitPost(() =>
        {
            npcUid = SpawnAt(entManager, Mob, gridUid, 0, 0);
            fireUid = SpawnAt(entManager, Fire, gridUid, 0, 0);
            Ignite(entManager, npcUid, 2f);
            Wake(entManager, npcUid);
        });

        await Pair.RunTicksSync(NoticeTicks);
        Assert.That(IsRolling(entManager, npcUid), Is.False, "it should not roll on the fire");

        await Pair.Server.WaitPost(() => entManager.DeleteEntity(fireUid));
        await Pair.RunTicksSync(NoticeTicks);
        Assert.That(IsRolling(entManager, npcUid), "with the fire gone, it should");
    }

    /// <summary>
    ///     Beside a fire it is walking away from, it keeps walking. Stopped there, it may as well.
    /// </summary>
    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task TestBesideAFire(bool walkingAway, bool rolls)
    {
        var (entManager, gridUid) = await SetUpGrid();
        EntityUid npcUid = default;

        await Pair.Server.WaitPost(() =>
        {
            npcUid = SpawnAt(entManager, Mob, gridUid, 0, 0);
            SpawnAt(entManager, Fire, gridUid, 1, 0);
        });

        await Pair.RunTicksSync(NavmeshTicks);

        await Pair.Server.WaitPost(() =>
        {
            if (walkingAway)
                entManager.System<NPCSteeringSystem>().Register(npcUid, new EntityCoordinates(gridUid, new Vector2(-5.5f, 0.5f)));

            Wake(entManager, npcUid);
        });

        await Pair.RunTicksSync(2); // for steering to get started, or it is not on its way yet
        await Pair.Server.WaitPost(() => Ignite(entManager, npcUid, 2f));
        await Pair.RunTicksSync(NoticeTicks);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(entManager.GetComponent<TransformComponent>(npcUid).LocalPosition.X, Is.GreaterThan(0f), "the test needs it still beside the fire");
            Assert.That(entManager.GetComponent<FlammableComponent>(npcUid).Resisting, Is.EqualTo(rolls));
        });
    }

    /// <summary>
    ///     With somewhere more urgent to be, it gets there first.
    /// </summary>
    [Test]
    public async Task TestNotWhileBusy()
    {
        var (entManager, gridUid) = await SetUpGrid();
        EntityUid npcUid = default;

        await Pair.Server.WaitPost(() =>
        {
            npcUid = SpawnAt(entManager, Holder, gridUid, 0, 0);
            entManager.GetComponent<HTNComponent>(npcUid).Blackboard.SetValue(BusyKey, true);
            Ignite(entManager, npcUid, 2f);
            Wake(entManager, npcUid);
        });

        await Pair.RunTicksSync(NoticeTicks);
        Assert.That(IsRolling(entManager, npcUid), Is.False, "it has somewhere to be");

        await Pair.Server.WaitPost(() => entManager.GetComponent<HTNComponent>(npcUid).Blackboard.Remove<bool>(BusyKey));
        await Pair.RunTicksSync(NoticeTicks);
        Assert.That(IsRolling(entManager, npcUid), "there now, it should");
    }

    private static bool IsRolling(IEntityManager entManager, EntityUid npcUid)
    {
        return entManager.GetComponent<FlammableComponent>(npcUid).Resisting;
    }

    private void Ignite(IEntityManager entManager, EntityUid npcUid, float fireStacks)
    {
        var flammableComponent = entManager.GetComponent<FlammableComponent>(npcUid);
        flammableComponent.NextUpdate = Pair.Server.ResolveDependency<IGameTiming>().CurTime + System.TimeSpan.FromHours(1);
        entManager.System<FlammableSystem>().AdjustFireStacks(npcUid, fireStacks, flammableComponent, ignite: true);
    }

    private static void Wake(IEntityManager entManager, EntityUid npcUid)
    {
        entManager.System<NPCSystem>().WakeNPC(npcUid, entManager.GetComponent<HTNComponent>(npcUid));
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
