#nullable enable
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Perception;
using Content.Shared._KS14.NPC;
using Content.Server._KS14.NPC.Squad;
using Content.Server._KS14.NPC.Squad.Tactics;
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

        // Repaired, it is as good as new: the rounds it took count for nothing, and it is shot at again.
        await Server.WaitPost(() =>
        {
            var heal = new Content.Shared.Damage.DamageSpecifier();
            heal.DamageDict["Piercing"] = -1000;
            heal.DamageDict["Heat"] = -1000;
            Server.EntMan.System<Content.Shared.Damage.Systems.DamageableSystem>().ChangeDamage(lockerUid, heal, ignoreResistances: true);
        });

        await Pair.RunTicksSync(2);
        var afterRepair = -1;
        await Server.WaitPost(() => afterRepair = suppressiveFireSystem.GetRoundsTaken(lockerUid));
        Assert.That(afterRepair, Is.Zero, "repaired, it should have no rounds counted against it");

        var firedAgain = false;
        for (var tick = 0; tick < MaxTicks && !firedAgain; tick += 10)
        {
            await Pair.RunTicksSync(10);
            await Server.WaitPost(() => firedAgain = suppressiveFireSystem.GetRoundsTaken(lockerUid) > 0);
        }

        Assert.That(firedAgain, "and it should be shot at again");
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
    ///     End to end: a squad of live Redacted that believe the hostile is in a locker none of them can open, and that do not
    ///         shoot lockers open, gets it open with a breaching charge from their loadout. One plants it, everyone gets clear,
    ///         and it is set off. The locker goes, and the hostile with it is out. With <c>searchChargeLockers</c> off, it
    ///         stands.
    /// </summary>
    [Test]
    public async Task TestASquadBlowsOpenALockerItCannotOpen([Values] bool charges)
    {
        if (!Server.ProtoMan.HasIndex<EntityPrototype>(Spawner))
            Assert.Ignore("the operative HTN is in the private _KsModule submodule, which is not present");

        var entManager = Server.EntMan;
        var tileDefinitionManager = Server.ResolveDependency<ITileDefinitionManager>();
        var timing = Server.ResolveDependency<IGameTiming>();
        var containerSystem = entManager.System<SharedContainerSystem>();
        var tacticsSystem = entManager.System<NpcSquadTacticsSystem>();
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default, hostileUid = default, lockerUid = default;
        var members = new System.Collections.Generic.List<EntityUid>();
        var chargeProto = new EntProtoId("KsWireEndBreachingCharge");
        var chargesBefore = 0;

        await Server.WaitPost(() =>
        {
            tacticsSystem.UpdatesPaused = false; // a test before this one on this server may have paused them
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-10, -5), new Vector2i(10, 5)).Owner;
            GiveGravity(entManager, gridUid);
            SpawnAt(entManager, Spawner, gridUid, -6, 1);
            SpawnAt(entManager, Spawner, gridUid, -6, -1);
            lockerUid = SpawnAt(entManager, "LockerSecurity", gridUid, 4, 0);
            hostileUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 7, 0);

            // In it, and locked, before they are about: never seen, never fought, and so never known to be anywhere.
            var storageSystem = entManager.System<SharedEntityStorageSystem>();
            if (storageSystem.IsOpen(lockerUid))
                storageSystem.CloseStorage(lockerUid);
            Assert.That(storageSystem.Insert(hostileUid, lockerUid), "the hostile should be in the locker");
        });

        await Pair.RunTicksSync(150); // the spawners make the operatives; the navmesh builds; the squad forms

        await Server.WaitPost(() =>
        {
            var enumerator = entManager.EntityQueryEnumerator<NpcSquadMemberComponent, TransformComponent>();
            while (enumerator.MoveNext(out var uid, out var memberComponent, out var transformComponent))
            {
                if (transformComponent.MapID != map.MapId || uid == hostileUid)
                    continue;

                members.Add(uid);
                entManager.System<Content.Shared.Damage.Systems.SharedGodmodeSystem>().EnableGodmode(uid);
                entManager.RemoveComponent<Content.Server._KS14.NPC.Grenades.NpcGrenadeThrowerComponent>(uid);

                // Only the charge may open it: nobody shoots it, alone or as a squad.
                entManager.GetComponent<HTNComponent>(uid).Blackboard.Remove<bool>("BreakLockers");
                var settings = memberComponent.Tactics;
                settings.SearchBreakLockers = false;
                settings.SearchChargeLockers = charges;
                settings.PlanChargeMinPressure = 0f; // a test map has no air in it
            }

            Assert.That(members, Has.Count.EqualTo(2), "the test needs two operatives");
            chargesBefore = CountCharges(entManager, chargeProto, map.MapId);
            Assert.That(chargesBefore, Is.GreaterThan(0), "the test needs them to carry a breaching charge");
            Assert.That(entManager.System<NpcSquadSystem>().TryGetSquad(members[0], out _), "the two should form a squad");

            Assert.That(entManager.System<SharedEntityStorageSystem>().CanOpen(members[0], lockerUid, silent: true), Is.False,
                "the test needs the locker locked to them");

            // Seen vanishing beside it, by both.
            var now = timing.CurTime;
            foreach (var memberUid in members)
            {
                entManager.System<NpcPerceptionSystem>().SetContact(memberUid, hostileUid, new NpcContact(NpcContactState.Suspected,
                    now - System.TimeSpan.FromSeconds(3), now - System.TimeSpan.FromSeconds(1), now - System.TimeSpan.FromSeconds(1),
                    entManager.GetComponent<TransformComponent>(lockerUid).Coordinates, default, lockerUid, Reacted: true, ReactAt: now));
            }
        });

        var broken = false;
        var sawBlast = false;
        var log = new System.Text.StringBuilder();

        // 50s: the watch, the walk up, sticking it on, getting clear and setting it off.
        for (var tick = 0; tick < 1500 && !broken; tick += 15)
        {
            await Pair.RunTicksSync(15);
            await Server.WaitPost(() =>
            {
                broken = entManager.Deleted(lockerUid) ||
                    !(containerSystem.TryGetContainingContainer(hostileUid, out var container) && container.Owner == lockerUid);

                var hunt = entManager.System<NpcSquadSystem>().TryGetSquad(members[0], out var squad) ? tacticsSystem.GetHunt(squad.Value.Owner) : null;
                sawBlast |= hunt?.BlastLockerUid != null;

                log.Append($"+{tick}: hunt={hunt?.Phase} locker={(entManager.Deleted(lockerUid) ? "gone" : entManager.System<SharedEntityStorageSystem>().IsOpen(lockerUid) ? "open" : "shut")} hostileIn={containerSystem.TryGetContainingContainer(hostileUid, out var hostileContainer) && hostileContainer.Owner == lockerUid}");
                foreach (var memberUid in members)
                {
                    var order = tacticsSystem.TryGetOrder(memberUid, out var memberOrder) ? memberOrder.Kind.ToString() : "-";
                    log.Append($" [{order} {entManager.GetComponent<TransformComponent>(memberUid).LocalPosition}]");
                }

                log.AppendLine();
            });
        }

        var chargesLeft = 0;
        await Server.WaitPost(() => chargesLeft = CountCharges(entManager, chargeProto, map.MapId));

        if (broken != charges || charges && !sawBlast)
            TestContext.Out.WriteLine(log.ToString());

        if (!charges)
        {
            Assert.That(broken, Is.False, "with the setting off, nothing should open it");
            return;
        }

        Assert.Multiple(() =>
        {
            Assert.That(sawBlast, "the squad should have gone for it with a charge");
            Assert.That(broken, "the charge should have blown the locker open, and the hostile out of it");
            Assert.That(chargesLeft, Is.EqualTo(chargesBefore - 1), "one charge should have gone off, and only the one");
        });
    }

    /// <summary>
    ///     Breaching charges on <paramref name="mapId"/>: carried, stuck on, or lying about.
    /// </summary>
    private static int CountCharges(IEntityManager entManager, EntProtoId chargeProto, MapId mapId)
    {
        var count = 0;
        var enumerator = entManager.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (enumerator.MoveNext(out _, out var metaData, out var transformComponent))
        {
            if (metaData.EntityPrototype?.ID == chargeProto.Id && transformComponent.MapID == mapId)
                count++;
        }

        return count;
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
