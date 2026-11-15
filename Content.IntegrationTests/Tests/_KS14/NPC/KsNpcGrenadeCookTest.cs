#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Grenades;
using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Grenades;
using Content.Server._KS14.NPC.Queries.Considerations;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Queries.Curves;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Throwing;
using Content.Shared.Trigger.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     Cooking (<c>NpcGrenadeSystem.Cooking.cs</c>): a thrower that cooks holds a lit frag, and throws it to land with about a
///         second of fuse left; an ordinary thrower throws it at once. A hold that is cut short, however, never leaves the
///         grenade going off in a hand.
/// </summary>
public sealed class KsNpcGrenadeCookTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string Cook = "KsCookTestCook";
    private const string Plain = "KsCookTestPlain";
    private const string Frag = "KsCookTestFrag";
    private const string Flash = "KsCookTestFlash";
    private const string Weapon = "KsCookTestWeapon";

    private const string WeaponHand = "right";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsCookTestPlain
  components:
  - type: Hands
  - type: ComplexInteraction
  - type: Inventory
    templateId: human
  - type: ContainerContainer
  - type: MobState
  - type: NpcGrenadeThrower

- type: entity
  parent: KsCookTestPlain
  id: KsCookTestCook
  components:
  - type: NpcGrenadeThrower
    cooks: true
    cookMargin: 0.45
    cookSpread: 0

# One set to keep nothing back at all: only the system's own minimum stands between it and a grenade landing as it goes off.
- type: entity
  parent: KsCookTestPlain
  id: KsCookTestCookReckless
  components:
  - type: NpcGrenadeThrower
    cooks: true
    cookMargin: 0
    cookSpread: 0

- type: entity
  id: KsCookTestWeapon
  components:
  - type: Item
    size: Large
  - type: Wieldable

# A frag that never really explodes: what it would do is delete itself, which is how the test sees it go off.
- type: entity
  parent: BaseItem
  id: KsCookTestFrag
  components:
  - type: Item
    size: Small
  - type: TimerTrigger
    delay: 3
  - type: Explosive
    explosionType: Default
    maxIntensity: 1
    intensitySlope: 1
    totalIntensity: 1
  - type: DeleteOnTrigger
    keysIn:
    - timer

- type: entity
  parent: BaseItem
  id: KsCookTestFlash
  components:
  - type: Item
    size: Small
  - type: TimerTrigger
    delay: 3
  - type: FlashOnTrigger
    keysIn:
    - timer
    range: 1
  - type: DeleteOnTrigger
    keysIn:
    - timer
";

    private sealed record Thrower(EntityUid NpcUid, EntityUid WeaponUid, EntityUid GrenadeUid);

    /// <summary>
    ///     The two throw the same grenade the same way: the one that cooks lands it with about the margin of fuse left, the
    ///         other with nearly all of it.
    /// </summary>
    [Test]
    public async Task TestCookedFragLandsWithLessFuseLeft()
    {
        var (entManager, gridUid) = await SetUpGrid();
        var grenadeSystem = entManager.System<NpcGrenadeSystem>();
        var timing = Pair.Server.ResolveDependency<IGameTiming>();
        Thrower cook = default!, plain = default!;
        EntityCoordinates cookAim = default, plainAim = default;
        EntityUid bystanderUid = default;

        await Pair.Server.WaitPost(() =>
        {
            cook = SpawnThrower(entManager, gridUid, Cook, -3, 0, Frag);
            plain = SpawnThrower(entManager, gridUid, Plain, -3, 6, Frag);
            bystanderUid = SpawnAt(entManager, Plain, gridUid, 3, 0); // where the cook is throwing it
            cookAim = entManager.GetComponent<TransformComponent>(cook.NpcUid).Coordinates.Offset(new Vector2(6f, 0f));
            plainAim = entManager.GetComponent<TransformComponent>(plain.NpcUid).Coordinates.Offset(new Vector2(6f, 0f));
        });

        await Pair.RunTicksSync(1);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(grenadeSystem.TryBeginThrow(cook.NpcUid, cook.GrenadeUid, cookAim), Is.EqualTo(NpcGrenadeThrowResult.Cooking));
            Assert.That(grenadeSystem.TryBeginThrow(plain.NpcUid, plain.GrenadeUid, plainAim), Is.EqualTo(NpcGrenadeThrowResult.Thrown),
                "one that does not cook throws it at once");

            Assert.Multiple(() =>
            {
                Assert.That(grenadeSystem.IsCooking(cook.NpcUid), "the cook is holding it");
                Assert.That(entManager.System<SharedHandsSystem>().IsHolding(cook.NpcUid, cook.GrenadeUid), "in its hand");
                Assert.That(entManager.HasComponent<ActiveTimerTriggerComponent>(cook.GrenadeUid), "lit");
                Assert.That(grenadeSystem.IsCooking(plain.NpcUid), Is.False);
            });

            // Whoever is near where it is going gets the warning as it starts, and not when it leaves the hand.
            Assert.That(grenadeSystem.TryGetLanding(cook.GrenadeUid, out var landing), "the squad is told where it is coming down");
            Assert.That(landing, Is.EqualTo(cookAim));
            var danger = new TargetDangerRangeCon { Margin = 0f, Curve = new BoolCurve() };
            danger.Initialise(entManager.EntitySysManager.DependencyCollection);
            var blackboard = new NPCBlackboard();
            Assert.That(danger.GetScore(blackboard, plain.NpcUid, cook.GrenadeUid), Is.Zero, "nobody runs from where it was not going");
            Assert.That(danger.GetScore(blackboard, cook.NpcUid, cook.GrenadeUid), Is.Zero, "nor the cook, with it in its hand");
            Assert.That(danger.GetScore(blackboard, bystanderUid, cook.GrenadeUid), Is.EqualTo(1f), "but whoever is under it moves, already");
        });

        var cookLeft = double.NaN;
        var plainLeft = double.NaN;
        var cookWasThrown = false;
        var plainWasThrown = false;

        for (var i = 0; i < 300 && (double.IsNaN(cookLeft) || double.IsNaN(plainLeft)); i++)
        {
            // The task keeps saying it is cooking, as the HTN does every tick: only the clock ends it.
            await Pair.Server.WaitPost(() => grenadeSystem.RenewCook(cook.NpcUid));
            await Pair.RunTicksSync(1);
            await Pair.Server.WaitPost(() =>
            {
                Landed(entManager, timing, cook.GrenadeUid, ref cookWasThrown, ref cookLeft);
                Landed(entManager, timing, plain.GrenadeUid, ref plainWasThrown, ref plainLeft);
            });
        }

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(cookLeft, Is.Not.NaN, "the cooked grenade should have landed");
            Assert.That(plainLeft, Is.Not.NaN, "the plain one too");
            Assert.That(cookLeft, Is.LessThan(plainLeft - 1), "much less fuse is left of the cooked one");
            Assert.That(cookLeft, Is.InRange(0.4, 0.6), "0.4 to 0.6s left: next to no time to get clear");
            Assert.That(plainLeft, Is.GreaterThan(2), "nearly all of an ordinary one's");

            var handsSystem = entManager.System<SharedHandsSystem>();
            Assert.That(handsSystem.GetActiveItem(cook.NpcUid), Is.EqualTo(cook.WeaponUid), "the weapon is back in hand");
            Assert.That(entManager.HasComponent<NpcGrenadeCookComponent>(cook.NpcUid), Is.False, "and the hold is over");
        });
    }

    /// <summary>
    ///     A hold that is cut short, however it is, throws the grenade at once and never lets it go off in hand: at every tick
    ///         until it explodes, it is either out of the hand or has the minimum of fuse still to go.
    /// </summary>
    [TestCase("task ended")]
    [TestCase("better plan")]
    [TestCase("task failed")]
    [TestCase("task stopped renewing")]
    [TestCase("died")]
    [TestCase("cannot throw")]
    public async Task TestInterruptedCookNeverGoesOffInHand(string how)
    {
        var (entManager, gridUid) = await SetUpGrid();
        var grenadeSystem = entManager.System<NpcGrenadeSystem>();
        var handsSystem = entManager.System<SharedHandsSystem>();
        var timing = Pair.Server.ResolveDependency<IGameTiming>();
        var operatorTask = new ThrowGrenadeOperator();
        var blackboard = new NPCBlackboard();
        Thrower cook = default!;
        EntityCoordinates aim = default;

        await Pair.Server.WaitPost(() =>
        {
            cook = SpawnThrower(entManager, gridUid, Cook, -3, 0, Frag);
            aim = entManager.GetComponent<TransformComponent>(cook.NpcUid).Coordinates.Offset(new Vector2(6f, 0f));

            entManager.EntitySysManager.DependencyCollection.InjectDependencies(operatorTask, oneOff: true);
            blackboard.SetValue(NPCBlackboard.Owner, cook.NpcUid);
            blackboard.SetValue("ChosenGrenade", cook.GrenadeUid);
            blackboard.SetValue("GrenadeThrowCoordinates", aim);
        });

        await Pair.RunTicksSync(1);

        // Started the way the HTN starts it: by the task, which carries on while it is held.
        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(operatorTask.Update(blackboard, 0.03f), Is.EqualTo(HTNOperatorStatus.Continuing));
            Assert.That(grenadeSystem.IsCooking(cook.NpcUid), "it should be cooking");
            Assert.That(handsSystem.IsHolding(cook.NpcUid, cook.GrenadeUid));
        });

        // A little while in, the hold is cut short.
        await Pair.RunTicksSync(5);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(operatorTask.Update(blackboard, 0.03f), Is.EqualTo(HTNOperatorStatus.Continuing), "still held");
            Assert.That(grenadeSystem.IsCooking(cook.NpcUid), "still cooking, with the fuse long enough to go on");

            switch (how)
            {
                case "task ended":
                    operatorTask.TaskShutdown(blackboard, HTNOperatorStatus.Finished);
                    break;
                case "better plan":
                    operatorTask.TaskShutdown(blackboard, HTNOperatorStatus.BetterPlan);
                    break;
                case "task failed":
                    operatorTask.TaskShutdown(blackboard, HTNOperatorStatus.Failed);
                    break;
                case "died":
                    // Nothing ends the task: the system sees it itself, on its next update.
                    entManager.System<MobStateSystem>().ChangeMobState(cook.NpcUid, MobState.Dead);
                    break;
                case "cannot throw":
                    // Stunned: it can throw nothing. Not the wait between throws, which ending a hold does not keep to.
                    entManager.EnsureComponent<Content.Shared.Stunnable.StunnedComponent>(cook.NpcUid);
                    operatorTask.TaskShutdown(blackboard, HTNOperatorStatus.BetterPlan);
                    break;
                case "task stopped renewing":
                    break;
            }
        });

        // One tick, for what has no task to say so (a death), and the lapsed task takes half a second.
        await Pair.RunTicksSync(how == "task stopped renewing" ? 25 : 2);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(grenadeSystem.IsCooking(cook.NpcUid), Is.False, "the hold should be over");
            Assert.That(handsSystem.IsHolding(cook.NpcUid, cook.GrenadeUid), Is.False, "the grenade should be out of the hand");

            if (how == "cannot throw" || how == "died")
                Assert.That(entManager.HasComponent<ThrownItemComponent>(cook.GrenadeUid), Is.False, "it could only be dropped");
            else
                Assert.That(entManager.HasComponent<ThrownItemComponent>(cook.GrenadeUid), "it should have been thrown");
        });

        // Until it goes off, which it must: out of the hand, at every tick that is checked.
        var exploded = false;
        for (var i = 0; i < 150 && !exploded; i++)
        {
            await Pair.RunTicksSync(1);
            await Pair.Server.WaitPost(() =>
            {
                if (entManager.Deleted(cook.GrenadeUid))
                {
                    exploded = true;
                    return;
                }

                Assert.That(handsSystem.IsHolding(cook.NpcUid, cook.GrenadeUid), Is.False, "never back in the hand");
            });
        }

        Assert.That(exploded, "it should have gone off, out of the hand, in its own time");
    }

    /// <summary>
    ///     However long a cook runs, it ends with the minimum of fuse still to go: a task that never ends it, and a thrower that
    ///         is never told of anything, still throw it in time. Whatever it is set to keep back, too: one set to keep nothing
    ///         still leaves the system's minimum on landing.
    /// </summary>
    [TestCase(Cook)]
    [TestCase("KsCookTestCookReckless")]
    public async Task TestACookIsThrownBeforeTheFuseIsDown(string thrower)
    {
        var (entManager, gridUid) = await SetUpGrid();
        var grenadeSystem = entManager.System<NpcGrenadeSystem>();
        var handsSystem = entManager.System<SharedHandsSystem>();
        var timing = Pair.Server.ResolveDependency<IGameTiming>();
        Thrower cook = default!;
        var leftWhenThrown = double.NaN;
        var flightTime = 0d;
        const float throwDistance = 6f;

        await Pair.Server.WaitPost(() =>
        {
            cook = SpawnThrower(entManager, gridUid, thrower, -3, 0, Frag);
            var aim = entManager.GetComponent<TransformComponent>(cook.NpcUid).Coordinates.Offset(new Vector2(throwDistance, 0f));
            Assert.That(grenadeSystem.TryBeginThrow(cook.NpcUid, cook.GrenadeUid, aim), Is.EqualTo(NpcGrenadeThrowResult.Cooking));

            // How long it is in the air for, as the system reckons it: from the thrower's own hands, not a copied speed.
            var handsComponent = entManager.GetComponent<HandsComponent>(cook.NpcUid);
            Assert.That(handsComponent.ThrowRange, Is.GreaterThanOrEqualTo(throwDistance), "the test's throw should be within reach");
            flightTime = throwDistance / handsComponent.BaseThrowspeed;
        });

        for (var i = 0; i < 200 && double.IsNaN(leftWhenThrown); i++)
        {
            // The task keeps saying it is cooking: only the clock ends it.
            await Pair.Server.WaitPost(() => grenadeSystem.RenewCook(cook.NpcUid));
            await Pair.RunTicksSync(1);
            await Pair.Server.WaitPost(() =>
            {
                if (entManager.Deleted(cook.GrenadeUid))
                    Assert.Fail("it went off before it was thrown");

                if (!handsSystem.IsHolding(cook.NpcUid, cook.GrenadeUid))
                    leftWhenThrown = (entManager.GetComponent<TimerTriggerComponent>(cook.GrenadeUid).NextTrigger - timing.CurTime).TotalSeconds;
            });
        }

        Assert.That(leftWhenThrown, Is.Not.NaN, "it should have been thrown");
        // Thrown on the first update past its time, and looked at an update after that.
        var tick = timing.TickPeriod.TotalSeconds;
        Assert.That(leftWhenThrown, Is.GreaterThanOrEqualTo(flightTime + NpcGrenadeSystem.MinCookMargin - 2 * tick),
            "with the flight and the minimum margin still to go");
    }

    /// <summary>
    ///     A flash is thrown at once, cooking or not, and so is anything without a fuse: only explosives with a timer are held.
    /// </summary>
    [Test]
    public async Task TestOnlyExplosivesAreCooked()
    {
        var (entManager, gridUid) = await SetUpGrid();
        var grenadeSystem = entManager.System<NpcGrenadeSystem>();
        Thrower flash = default!;

        await Pair.Server.WaitPost(() => flash = SpawnThrower(entManager, gridUid, Cook, -3, 0, Flash));

        await Pair.RunTicksSync(1);

        await Pair.Server.WaitAssertion(() =>
        {
            var aim = entManager.GetComponent<TransformComponent>(flash.NpcUid).Coordinates.Offset(new Vector2(6f, 0f));
            Assert.That(grenadeSystem.TryBeginThrow(flash.NpcUid, flash.GrenadeUid, aim), Is.EqualTo(NpcGrenadeThrowResult.Thrown));
            Assert.That(grenadeSystem.IsCooking(flash.NpcUid), Is.False);
        });
    }

    /// <summary>
    ///     Records the fuse left (<paramref name="left"/>) the first tick a grenade that was in the air no longer is.
    /// </summary>
    private static void Landed(IEntityManager entManager, IGameTiming timing, EntityUid grenadeUid, ref bool wasThrown, ref double left)
    {
        if (!double.IsNaN(left) || entManager.Deleted(grenadeUid))
            return;

        var flying = entManager.HasComponent<ThrownItemComponent>(grenadeUid);
        wasThrown |= flying;

        if (wasThrown && !flying)
            left = (entManager.GetComponent<TimerTriggerComponent>(grenadeUid).NextTrigger - timing.CurTime).TotalSeconds;
    }

    private async Task<(IEntityManager EntManager, EntityUid GridUid)> SetUpGrid()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default;

        await server.WaitPost(() => gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-6, -4), new Vector2i(12, 10)).Owner);

        return (entManager, gridUid);
    }

    /// <summary>
    ///     A thrower with a weapon wielded in hand and a grenade in its pocket.
    /// </summary>
    private static Thrower SpawnThrower(IEntityManager entManager, EntityUid gridUid, string prototype, int x, int y, string grenade)
    {
        var npcUid = SpawnAt(entManager, prototype, gridUid, x, y);
        var coordinates = entManager.GetComponent<TransformComponent>(npcUid).Coordinates;
        var handsSystem = entManager.System<SharedHandsSystem>();
        handsSystem.AddHand(npcUid, WeaponHand, HandLocation.Right);
        handsSystem.AddHand(npcUid, "left", HandLocation.Left);

        var weaponUid = entManager.SpawnEntity(Weapon, coordinates);
        Assert.That(handsSystem.TryPickup(npcUid, weaponUid, WeaponHand));
        handsSystem.TrySetActiveHand(npcUid, WeaponHand);

        var grenadeUid = entManager.SpawnEntity(grenade, coordinates);
        Assert.That(entManager.System<Content.Shared.Inventory.InventorySystem>().TryEquip(npcUid, grenadeUid, "pocket1", silent: true, force: true),
            "the grenade should be in a pocket");

        return new Thrower(npcUid, weaponUid, grenadeUid);
    }
}
