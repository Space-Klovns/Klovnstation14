#nullable enable
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Perception;
using Content.Shared._KS14.NPC;
using Content.Server._KS14.NPC.Squad;
using Content.Server._KS14.NPC.Suppression;
using Content.Server.NPC.HTN;
using Content.Shared.Storage.EntitySystems;
using NUnit.Framework;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     A Redacted that saw a hostile get into a locked locker, which it cannot open, shoots the locker until it breaks
///         (<c>KsOperativeSearchBreakCompound</c>, gated on <c>BreakLockers</c>). Without the key it only covers it.
/// </summary>
[TestFixture]
public sealed class KsNpcSearchBreakLockerTest : GameTest
{
    // Connected: a real NPC only runs while a player is about.
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId Spawner = "KsRandomHumanoidSpawnerOperativeRedacted";

    /// <summary>
    ///     20s: up to the locker, then a few bursts.
    /// </summary>
    private const int MaxTicks = 600;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: LockerSecurity
  id: KsBreakLockerTestSturdyLocker
  components:
  - type: Destructible
    thresholds:
    - trigger:
        !type:DamageTrigger
        damage: 140
      behaviors:
      - !type:DoActsBehavior
        acts: [ ""Destruction"" ]
";

    /// <summary>
    ///     With <c>BreakLockerRounds</c> set, it gives up on a locker once NPCs have put that many rounds into it: a sturdier
    ///         one than three rounds break is left standing, with three in it and no more, and covered instead.
    /// </summary>
    [Test]
    public async Task TestALockerThatSoaksUpRoundsIsGivenUpOn()
    {
        if (!Server.ProtoMan.HasIndex<EntityPrototype>(Spawner))
            Assert.Ignore("the operative HTN is in the private _KsModule submodule, which is not present");

        var (npcUid, hostileUid, lockerUid) = await SetUp("KsBreakLockerTestSturdyLocker", breakLockers: true, maxRounds: 3);
        var suppressiveFireSystem = Server.EntMan.System<NpcSuppressiveFireSystem>();
        var roundsSeen = 0;
        var stillAt = 0;

        for (var tick = 0; tick < MaxTicks; tick += 10)
        {
            await Pair.RunTicksSync(10);
            await Server.WaitPost(() =>
            {
                var rounds = suppressiveFireSystem.GetRoundsTaken(lockerUid);
                if (rounds != roundsSeen)
                    stillAt = tick;
                roundsSeen = rounds;
            });
        }

        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.EntMan.Deleted(lockerUid), Is.False, "three rounds should not break it");
            Assert.That(roundsSeen, Is.InRange(3, 5), "it should have fired three rounds at it, and given up: a burst may finish");
            Assert.That(stillAt, Is.LessThan(MaxTicks - 200), "and fired no more at it after that");
        });
    }

    [Test]
    public async Task TestALockedLockerItSawTheHostileGetIntoIsShotOpen([Values] bool breakLockers)
    {
        if (!Server.ProtoMan.HasIndex<EntityPrototype>(Spawner))
            Assert.Ignore("the operative HTN is in the private _KsModule submodule, which is not present");

        var entManager = Server.EntMan;
        var containerSystem = entManager.System<SharedContainerSystem>();
        var (npcUid, hostileUid, lockerUid) = await SetUp("LockerSecurity", breakLockers);

        var broken = false;
        var log = new System.Text.StringBuilder();
        for (var tick = 0; tick < MaxTicks && !broken; tick += 10)
        {
            await Pair.RunTicksSync(10);
            await Server.WaitPost(() =>
            {
                broken = entManager.Deleted(lockerUid) ||
                    !(containerSystem.TryGetContainingContainer(hostileUid, out var container) && container.Owner == lockerUid);
                var plan = entManager.GetComponent<HTNComponent>(npcUid).Plan;
                log.AppendLine($"+{tick}: [{(plan == null ? "" : string.Join(",", plan.BranchTraversalRecord))}] {plan?.CurrentOperator.GetType().Name} pos={entManager.GetComponent<TransformComponent>(npcUid).LocalPosition}");
            });
        }

        if (broken != breakLockers)
            TestContext.Out.WriteLine(log.ToString());

        if (breakLockers)
            Assert.That(broken, "it should have shot the locker open, and the hostile out of it");
        else
            Assert.That(broken, Is.False, "without BreakLockers it should only cover the locker");
    }

    /// <summary>
    ///     A live Redacted, and the hostile it saw get into <paramref name="lockerPrototype"/> a few tiles off, locked to it.
    /// </summary>
    private async Task<(EntityUid Npc, EntityUid Hostile, EntityUid Locker)> SetUp(string lockerPrototype, bool breakLockers, float? maxRounds = null)
    {
        var entManager = Server.EntMan;
        var tileDefinitionManager = Server.ResolveDependency<ITileDefinitionManager>();
        var timing = Server.ResolveDependency<IGameTiming>();
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default, npcUid = default, hostileUid = default, lockerUid = default;

        await Server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-8, -4), new Vector2i(8, 4)).Owner;
            GiveGravity(entManager, gridUid);
            SpawnAt(entManager, Spawner, gridUid, -5, 0);
            lockerUid = SpawnAt(entManager, lockerPrototype, gridUid, 3, 0);
            hostileUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 6, 0);
        });

        await Pair.RunTicksSync(120); // the spawner makes the operative; the navmesh builds

        await Server.WaitPost(() =>
        {
            var enumerator = entManager.EntityQueryEnumerator<NpcSquadMemberComponent, TransformComponent>();
            while (enumerator.MoveNext(out var uid, out _, out var transformComponent))
            {
                if (transformComponent.MapID == map.MapId && uid != hostileUid)
                    npcUid = uid;
            }

            entManager.System<Content.Shared.Damage.Systems.SharedGodmodeSystem>().EnableGodmode(npcUid);
            // Its grenades go too: one at the hidden hostile is a fair answer, and a push after it is not what is tested.
            entManager.RemoveComponent<Content.Server._KS14.NPC.Grenades.NpcGrenadeThrowerComponent>(npcUid);

            // In it, and locked: the operative cannot open it.
            var storageSystem = entManager.System<SharedEntityStorageSystem>();
            if (storageSystem.IsOpen(lockerUid))
                storageSystem.CloseStorage(lockerUid);
            Assert.That(storageSystem.Insert(hostileUid, lockerUid), "the hostile should be in the locker");
            Assert.That(storageSystem.CanOpen(npcUid, lockerUid, silent: true), Is.False, "and the locker locked to the operative");

            var blackboard = entManager.GetComponent<HTNComponent>(npcUid).Blackboard;
            if (!breakLockers)
                blackboard.Remove<bool>("BreakLockers");
            if (maxRounds is { } rounds)
                blackboard.SetValue("BreakLockerRounds", rounds);

            // Seen getting in.
            var now = timing.CurTime;
            entManager.System<NpcPerceptionSystem>().SetContact(npcUid, hostileUid, new NpcContact(NpcContactState.Concealed,
                now - System.TimeSpan.FromSeconds(3), now - System.TimeSpan.FromSeconds(1), now - System.TimeSpan.FromSeconds(1),
                entManager.GetComponent<TransformComponent>(lockerUid).Coordinates, default, lockerUid, Reacted: true));
        });

        return (npcUid, hostileUid, lockerUid);
    }
}
