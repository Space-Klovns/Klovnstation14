#nullable enable
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Execution;
using Content.Server._KS14.NPC.Squad;
using Content.Server.NPC.HTN;
using Content.Shared.DoAfter;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     A Redacted with no gun and a knife in the hand that is not active walks up to a downed hostile and executes it, once:
///         one do-after, and it is dead. On an older branch one like this was seen in a loop, its execution cut short
///         part way by its own hands and started again, over and over.
/// </summary>
[TestFixture]
public sealed class KsNpcExecutionOffhandKnifeTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    // A string, not an EntProtoId: the linter checks those, and the Redacted are in the private submodule, which it may lack.
    private const string Spawner = "KsRandomHumanoidSpawnerOperativeRedacted";

    [Test]
    public async Task TestExecutesOnceWithAKnifeInTheOtherHand()
    {
        if (!Server.ProtoMan.HasIndex<Robust.Shared.Prototypes.EntityPrototype>(Spawner))
            Assert.Ignore("no submodule");

        var entManager = Server.EntMan;
        var tileDefinitionManager = Server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default, npcUid = default, victimUid = default;

        await Server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-8, -8), new Vector2i(8, 8)).Owner;
            GiveGravity(entManager, gridUid);
            SpawnAt(entManager, Spawner, gridUid, -3, 0);
            victimUid = SpawnAt(entManager, "MobHuman", gridUid, 2, 0);
            entManager.RemoveComponent<Content.Server.Atmos.Components.BarotraumaComponent>(victimUid);
            entManager.RemoveComponent<Content.Server.Body.Components.RespiratorComponent>(victimUid);
        });

        await Pair.RunTicksSync(120);

        await Server.WaitPost(() =>
        {
            var enumerator = entManager.EntityQueryEnumerator<NpcSquadMemberComponent, TransformComponent>();
            while (enumerator.MoveNext(out var uid, out _, out var transformComponent))
            {
                if (transformComponent.MapID == map.MapId && uid != victimUid)
                    npcUid = uid;
            }

            entManager.System<Content.Shared.Damage.Systems.SharedGodmodeSystem>().EnableGodmode(npcUid);

            // No gun: whatever is in its hands goes.
            var handsSystem = entManager.System<SharedHandsSystem>();
            var handsComponent = entManager.GetComponent<HandsComponent>(npcUid);
            foreach (var hand in handsSystem.EnumerateHands((npcUid, handsComponent)).ToList())
            {
                if (handsSystem.GetHeldItem((npcUid, handsComponent), hand) is { } held)
                    entManager.DeleteEntity(held);
            }

            // A knife in the hand that is not active.
            var inactive = handsSystem.EnumerateHands((npcUid, handsComponent)).First(h => h != handsComponent.ActiveHandId);
            var knifeUid = entManager.SpawnAtPosition("CombatKnife", entManager.GetComponent<TransformComponent>(npcUid).Coordinates);
            Assert.That(handsSystem.TryPickup(npcUid, knifeUid, inactive));
            // The empty one active, the knife in the other.
            var empty = handsSystem.EnumerateHands((npcUid, handsComponent)).First(h => h != inactive);
            handsSystem.TrySetActiveHand((npcUid, handsComponent), empty);

            entManager.System<MobStateSystem>().ChangeMobState(victimUid, MobState.Critical);
        });

        var log = new System.Text.StringBuilder();
        var executionSystem = entManager.System<NpcExecutionSystem>();
        var mobStateSystem = entManager.System<MobStateSystem>();
        var starts = 0;
        var wasExecuting = false;
        var dead = false;

        // 30s at most: walking up, the line, a beat and the do-after take about 6.
        for (var tick = 0; tick < 900 && !dead; tick++)
        {
            await Pair.RunTicksSync(1);
            await Server.WaitPost(() =>
            {
                var plan = entManager.GetComponent<HTNComponent>(npcUid).Plan;
                var handsSystem = entManager.System<SharedHandsSystem>();
                var handsComponent = entManager.GetComponent<HandsComponent>(npcUid);
                var hands = string.Join(" ", handsSystem.EnumerateHands((npcUid, handsComponent)).Select(h =>
                    $"{h}{(h == handsComponent.ActiveHandId ? "*" : "")}={(handsSystem.GetHeldItem((npcUid, handsComponent), h) is { } i ? entManager.GetComponent<MetaDataComponent>(i).EntityPrototype?.ID : "-")}"));
                var doAfters = entManager.TryGetComponent<DoAfterComponent>(npcUid, out var doAfterComponent) ? doAfterComponent.DoAfters.Count : 0;
                var executing = executionSystem.IsExecuting(npcUid);
                if (executing && !wasExecuting)
                    starts++;

                wasExecuting = executing;
                dead = mobStateSystem.IsDead(victimUid);
                log.AppendLine($"+{tick}: [{(plan == null ? "" : string.Join(",", plan.BranchTraversalRecord))}] {plan?.CurrentOperator.GetType().Name} | {hands} | exec={executing} doafters={doAfters} dead={dead}");
            });
        }

        if (!dead || starts != 1)
            TestContext.Out.WriteLine(log.ToString());

        Assert.That(dead, "it should have executed the hostile");
        Assert.That(starts, Is.EqualTo(1), "with one execution, not one cut short and started again");
    }
}
