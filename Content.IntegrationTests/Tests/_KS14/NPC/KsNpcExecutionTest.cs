#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Execution;
using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Execution;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     An NPC carrying out an execution: starting it with what it is given, and everything going back as it was, however
///         it ends. What an execution then does to its victim is the execution systems' own, and choosing the weapon is
///         the operative HTN's.
/// </summary>
public sealed class KsNpcExecutionTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string Npc = "KsExecutionTestNpc";
    private const string Weapon = "KsExecutionTestGun";
    private const string Knife = "KsExecutionTestKnife";
    private const string Victim = "KsExecutionTestVictim";
    private const string SoftCritVictim = "KsExecutionTestSoftCritVictim";

    private const string WeaponHand = "right";

    private static readonly ProtoId<DamageTypePrototype> BluntDamage = "Blunt";

    /// <summary>
    ///     Enough of a mob to hold things and start do-afters, and weapons that execute at once.
    /// </summary>
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsExecutionTestNpc
  components:
  - type: Hands
  - type: ComplexInteraction
  - type: Inventory
    templateId: human
  - type: ContainerContainer
  - type: MobState
  - type: DoAfter
  - type: CombatMode
  - type: NpcExecutioner

# Takes damage, which the shared test mobs do not: they have no Injurable.
- type: entity
  parent: KsSquadTestMobNanoTrasen
  id: KsExecutionTestVictim
  components:
  - type: Injurable

# One that can crawl while critical, up to 130 damage on these thresholds.
- type: entity
  parent: KsExecutionTestVictim
  id: KsExecutionTestSoftCritVictim
  components:
  - type: KsSoftCrit

- type: entity
  parent: KsNpcTestPistol
  id: KsExecutionTestGun
  components:
  - type: Gun
    gunExecutionTime: 0.1

- type: entity
  parent: BaseItem
  id: KsExecutionTestKnife
  components:
  - type: Item
    size: Small
  - type: MeleeWeapon
    damage:
      types:
        Slash: 15
  - type: Execution
    doAfterDuration: 0.1
";

    /// <summary>
    ///     Live, with the gun it already has in hand: it starts, ends by itself, and leaves the gun where it was.
    /// </summary>
    [Test]
    public async Task TestWithTheGunInHand()
    {
        var (entManager, npcUid, victimUid, gunUid) = await SetUp();
        var executionSystem = entManager.System<NpcExecutionSystem>();
        var handsSystem = entManager.System<SharedHandsSystem>();

        await Pair.Server.WaitPost(() =>
            Assert.That(executionSystem.TryStartExecution(npcUid, victimUid, gunUid), "the execution should start"));

        await RunUntilDone(executionSystem, npcUid);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(executionSystem.IsExecuting(npcUid), Is.False, "it should end by itself");

            executionSystem.FinishExecution(npcUid);

            Assert.That(handsSystem.GetActiveItem(npcUid), Is.EqualTo(gunUid), "the gun should still be in hand");
            Assert.That(entManager.System<MobStateSystem>().IsDead(victimUid), "and the victim should be dead");
        });
    }

    /// <summary>
    ///     With something carried: it comes out for the job, and it goes back, with the weapon in hand again.
    /// </summary>
    [Test]
    public async Task TestWithACarriedKnife()
    {
        var (entManager, npcUid, victimUid, gunUid) = await SetUp();
        var executionSystem = entManager.System<NpcExecutionSystem>();
        var handsSystem = entManager.System<SharedHandsSystem>();
        var inventorySystem = entManager.System<InventorySystem>();
        EntityUid knifeUid = default;

        await Pair.Server.WaitPost(() =>
        {
            knifeUid = entManager.SpawnEntity(Knife, entManager.GetComponent<TransformComponent>(npcUid).Coordinates);
            Assert.That(inventorySystem.TryEquip(npcUid, knifeUid, "pocket1", silent: true, force: true), "the knife should be in a pocket");

            Assert.That(executionSystem.TryStartExecution(npcUid, victimUid, knifeUid), "the execution should start");
            Assert.That(handsSystem.GetActiveItem(npcUid), Is.EqualTo(knifeUid), "the knife should be out for it");
        });

        await RunUntilDone(executionSystem, npcUid);

        await Pair.Server.WaitAssertion(() =>
        {
            executionSystem.FinishExecution(npcUid);

            Assert.Multiple(() =>
            {
                Assert.That(handsSystem.GetActiveItem(npcUid), Is.EqualTo(gunUid), "the gun should be back in hand");
                Assert.That(handsSystem.IsHolding(npcUid, knifeUid), Is.False, "the knife should be away again");
            });
        });
    }

    /// <summary>
    ///     The task fails when the victim comes out of it alive - moving breaks an execution off - and still puts everything
    ///         back.
    /// </summary>
    [Test]
    public async Task TestOperatorFailsIfItComesToNothing()
    {
        var (entManager, npcUid, victimUid, gunUid) = await SetUp();
        var executionSystem = entManager.System<NpcExecutionSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();
        var executeOperator = new ExecuteOperator();
        var blackboard = new NPCBlackboard();

        await Pair.Server.WaitPost(() =>
        {
            executeOperator.Initialize(entManager.EntitySysManager);
            blackboard.SetValue(NPCBlackboard.Owner, npcUid);
            blackboard.SetValue(executeOperator.TargetKey, victimUid);
            blackboard.SetValue(executeOperator.WeaponKey, gunUid);

            executeOperator.Startup(blackboard);
            Assert.That(executeOperator.Update(blackboard, 0f), Is.EqualTo(HTNOperatorStatus.Continuing), "it should be waiting on the execution");

            var coordinates = entManager.GetComponent<TransformComponent>(npcUid).Coordinates;
            transformSystem.SetCoordinates(npcUid, coordinates.Offset(new System.Numerics.Vector2(2f, 0f)));
        });

        await RunUntilDone(executionSystem, npcUid);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(executeOperator.Update(blackboard, 0f), Is.EqualTo(HTNOperatorStatus.Failed), "the victim is still alive");

            executeOperator.TaskShutdown(blackboard, HTNOperatorStatus.Failed);

            Assert.That(entManager.System<SharedHandsSystem>().GetActiveItem(npcUid), Is.EqualTo(gunUid));
        });
    }

    /// <summary>
    ///     An execution cancelled in any way - the victim crawling off, or dragged, or the executioner shoved - is given up
    ///         on at once, in soft crit or hard: executioners shoot it from then on, and the target query passes it by.
    /// </summary>
    [TestCase(110, true)]
    [TestCase(150, true)]
    [TestCase(150, false)]
    public async Task TestCancelledExecutionIsGivenUpOn(int damage, bool victimMoves)
    {
        var (entManager, npcUid, victimUid, gunUid) = await SetUp(SoftCritVictim, damage);
        var executionSystem = entManager.System<NpcExecutionSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();
        var executeOperator = new ExecuteOperator();
        var blackboard = new NPCBlackboard();

        await Pair.Server.WaitPost(() =>
        {
            executeOperator.Initialize(entManager.EntitySysManager);
            blackboard.SetValue(NPCBlackboard.Owner, npcUid);
            blackboard.SetValue(executeOperator.TargetKey, victimUid);
            blackboard.SetValue(executeOperator.WeaponKey, gunUid);

            Assert.That(executionSystem.AwaitsExecution(victimUid), "it should await its execution to begin with");

            executeOperator.Startup(blackboard);
            Assert.That(executeOperator.Update(blackboard, 0f), Is.EqualTo(HTNOperatorStatus.Continuing), "it should be waiting on the execution");

            // Either the victim is moved away, or the executioner is.
            var movedUid = victimMoves ? victimUid : npcUid;
            transformSystem.SetCoordinates(movedUid, entManager.GetComponent<TransformComponent>(movedUid).Coordinates.Offset(new System.Numerics.Vector2(3f, 0f)));
        });

        await RunUntilDone(executionSystem, npcUid);

        await Pair.Server.WaitPost(() =>
        {
            Assert.That(executeOperator.Update(blackboard, 0f), Is.EqualTo(HTNOperatorStatus.Failed), "the victim is still alive");
            executeOperator.TaskShutdown(blackboard, HTNOperatorStatus.Failed);

            Assert.That(executionSystem.IsRefused(victimUid), "one cancelled execution should be enough");
            Assert.That(executionSystem.AwaitsExecution(victimUid), Is.False, "and the victim is one to shoot, not walk up to");
        });
    }

    /// <summary>
    ///     Only an execution that was started and then cancelled counts: one that goes through, and one the NPC's task is
    ///         dropped from before it comes to anything, leave the victim to be executed.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestExecutionNotCancelledIsNotGivenUpOn(bool completes)
    {
        var (entManager, npcUid, victimUid, gunUid) = await SetUp(SoftCritVictim, 150);
        var executionSystem = entManager.System<NpcExecutionSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();
        var executeOperator = new ExecuteOperator();
        var blackboard = new NPCBlackboard();

        await Pair.Server.WaitPost(() =>
        {
            executeOperator.Initialize(entManager.EntitySysManager);
            blackboard.SetValue(NPCBlackboard.Owner, npcUid);
            blackboard.SetValue(executeOperator.TargetKey, victimUid);
            blackboard.SetValue(executeOperator.WeaponKey, gunUid);
            executeOperator.Startup(blackboard);

            if (!completes)
            {
                // Replanned away: the task shuts down, and only afterwards does anything disturb the victim.
                executeOperator.TaskShutdown(blackboard, HTNOperatorStatus.Failed);
                transformSystem.SetCoordinates(victimUid, entManager.GetComponent<TransformComponent>(victimUid).Coordinates.Offset(new System.Numerics.Vector2(3f, 0f)));
            }
        });

        await RunUntilDone(executionSystem, npcUid);
        await Pair.RunTicksSync(5);

        await Pair.Server.WaitPost(() =>
        {
            // Asked once more after its task was dropped, as a plan that came back to it would: the execution is no longer
            //      its to judge, so the victim having been moved since is not a cancelled one. Without this nothing asks.
            if (!completes)
                Assert.That(executeOperator.Update(blackboard, 0f), Is.EqualTo(HTNOperatorStatus.Failed), "the victim is still alive");

            Assert.That(executionSystem.IsRefused(victimUid), Is.False);

            if (completes)
            {
                Assert.That(executeOperator.Update(blackboard, 0f), Is.EqualTo(HTNOperatorStatus.Finished));
                Assert.That(entManager.System<MobStateSystem>().IsDead(victimUid), "and the victim should be dead");
            }

            executeOperator.TaskShutdown(blackboard, HTNOperatorStatus.Finished);
        });
    }

    private async Task RunUntilDone(NpcExecutionSystem executionSystem, EntityUid npcUid)
    {
        for (var i = 0; i < 120; i++)
        {
            await Pair.RunTicksSync(1);

            var done = false;
            await Pair.Server.WaitPost(() => done = !executionSystem.IsExecuting(npcUid));

            if (done)
                return;
        }
    }

    /// <summary>
    ///     An NPC with a loaded gun in hand, right beside a hostile that is down.
    /// </summary>
    private async Task<(IEntityManager EntManager, EntityUid NpcUid, EntityUid VictimUid, EntityUid GunUid)> SetUp(string victimPrototype = Victim, int damage = 0)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid npcUid = default, victimUid = default, gunUid = default;

        await server.WaitPost(() =>
        {
            var gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-5, -5), new Vector2i(5, 5)).Owner;
            npcUid = SpawnAt(entManager, Npc, gridUid, 0, 0);
            victimUid = SpawnAt(entManager, victimPrototype, gridUid, 1, 0);

            var handsSystem = entManager.System<SharedHandsSystem>();
            handsSystem.AddHand(npcUid, WeaponHand, HandLocation.Right);
            handsSystem.AddHand(npcUid, "left", HandLocation.Left);

            gunUid = entManager.SpawnEntity(Weapon, entManager.GetComponent<TransformComponent>(npcUid).Coordinates);
            Assert.That(handsSystem.TryPickup(npcUid, gunUid, WeaponHand));
            handsSystem.TrySetActiveHand(npcUid, WeaponHand); // false if it already is

            // Chamber a round, as racking would.
            entManager.System<Content.Shared.Weapons.Ranged.Systems.SharedGunSystem>().UseChambered(gunUid,
                entManager.GetComponent<Content.Shared.Weapons.Ranged.Components.ChamberMagazineAmmoProviderComponent>(gunUid), npcUid);

            if (damage > 0)
                entManager.System<DamageableSystem>().TryChangeDamage(victimUid,
                    new DamageSpecifier(SProtoMan.Index(BluntDamage), FixedPoint2.New(damage)), ignoreResistances: true);
            else
                entManager.System<MobStateSystem>().ChangeMobState(victimUid, MobState.Critical);
        });

        // A gun just put in hand has a moment before it can fire, and executing with one has to be able to.
        var timing = server.ResolveDependency<Robust.Shared.Timing.IGameTiming>();
        var wait = 0.0;
        await server.WaitPost(() =>
            wait = (entManager.GetComponent<Content.Shared.Weapons.Ranged.Components.GunComponent>(gunUid).NextFire - timing.CurTime).TotalSeconds);

        await Pair.RunTicksSync((int) (wait * timing.TickRate) + 2);

        return (entManager, npcUid, victimUid, gunUid);
    }
}
