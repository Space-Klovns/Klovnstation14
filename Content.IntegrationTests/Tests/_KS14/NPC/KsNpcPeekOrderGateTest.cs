#nullable enable
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;
using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Time;
using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Squad;
using Content.Server._KS14.NPC.Squad.Tactics;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Shared._KS14.NPC;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     Peeking at a lost hostile (<c>peek.yml</c>, its two <c>root.yml</c> branches) only beats holding still or looking: a
///         member a squad has sent to stack up, breach or go in carries on with that. And the time between peeks is the mob's
///         to say (<c>PeekInterval</c> on its blackboard, through <see cref="SetTimeOperator.OffsetKey"/>).
/// </summary>
[TestFixture]
public sealed class KsNpcPeekOrderGateTest : GameTest
{
    // Connected: a real NPC only runs while a player is about.
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private readonly ProtoId<HTNCompoundPrototype> _operativeRoot = "KsOperativeCombatCompound";

    /// <summary>
    ///     How long it gets, at most, in ticks: 50s, as <c>KsNpcPeekTest</c>. A peek takes several plans, and planning has a
    ///         real-time budget a tick.
    /// </summary>
    private const int MaxTicks = 1500;

    /// <summary>
    ///     How long one with an order it should keep to is watched for a peek, in ticks: 20s, several times what a peek takes
    ///         when it is let.
    /// </summary>
    private const int HeldTicks = 600;

    /// <summary>
    ///     A member with a squad's order: <see cref="NpcOrderKind.Watch"/> is one a peek may take it off, and
    ///         <see cref="NpcOrderKind.Stage"/> one it may not. The scene is <c>KsNpcPeekTest</c>'s: behind a wall that ends at
    ///         y=-1, a hostile it lost a moment ago in sight of the wall's end.
    /// </summary>
    [Test]
    public async Task TestPeekOnlyTakesAMemberOffWatching([Values(NpcOrderKind.Watch, NpcOrderKind.Stage)] NpcOrderKind kind)
    {
        if (!Server.ProtoMan.HasIndex(_operativeRoot))
            Assert.Ignore("the operative HTN is in the private _KsModule submodule, which is not present");

        var entManager = Server.EntMan;
        var tileDefinitionManager = Server.ResolveDependency<ITileDefinitionManager>();
        var timing = Server.ResolveDependency<IGameTiming>();
        var transformSystem = entManager.System<SharedTransformSystem>();
        var tacticsSystem = entManager.System<NpcSquadTacticsSystem>();
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default, operativeUid = default, hostileUid = default;

        await Server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-12, -12), new Vector2i(12, 12)).Owner;
            GiveGravity(entManager, gridUid);

            for (var y = -9; y <= -2; y++)
                SpawnAt(entManager, "WallSolid", gridUid, -2, y);

            SpawnAt(entManager, "KsRandomHumanoidSpawnerOperativeSynArmedPistol", gridUid, -4, -4);
            hostileUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 6, 0);
            entManager.System<Content.Shared.Damage.Systems.SharedGodmodeSystem>().EnableGodmode(hostileUid);
        });

        await Pair.RunTicksSync(120); // the spawner makes the operative; the navmesh builds

        try
        {
            await Server.WaitPost(() =>
            {
                var enumerator = entManager.EntityQueryEnumerator<NpcSquadMemberComponent, TransformComponent>();
                while (enumerator.MoveNext(out var uid, out _, out var transformComponent))
                {
                    if (transformComponent.MapID == map.MapId && uid != hostileUid)
                        operativeUid = uid;
                }

                entManager.System<Content.Shared.Damage.Systems.SharedGodmodeSystem>().EnableGodmode(operativeUid);

                var now = timing.CurTime;
                entManager.System<NpcPerceptionSystem>().SetContact(operativeUid, hostileUid, new NpcContact(NpcContactState.Lost,
                    now - System.TimeSpan.FromSeconds(3), now - System.TimeSpan.FromSeconds(2), now - System.TimeSpan.FromSeconds(2),
                    new EntityCoordinates(gridUid, new Vector2(6.5f, 0.5f)), default, null, Reacted: true));

                entManager.GetComponent<HTNComponent>(operativeUid).Blackboard
                    .SetValue(EnsureVirtualMarkerOperator.MarkerSet, new HashSet<string> { "OpInCombat" });

                // Where it stands: an order to stay, of either kind, and nothing else for it to walk to.
                tacticsSystem.UpdatesPaused = true;
                tacticsSystem.IssueOrder(operativeUid, kind, entManager.GetComponent<TransformComponent>(operativeUid).Coordinates, targetUid: hostileUid);
            });

            var steppedOut = false;
            var limit = kind == NpcOrderKind.Watch ? MaxTicks : HeldTicks;
            var log = new System.Text.StringBuilder();

            for (var tick = 0; tick < limit && !steppedOut; tick += 5)
            {
                await Pair.RunTicksSync(5);
                await Server.WaitPost(() =>
                {
                    var position = transformSystem.GetWorldPosition(operativeUid);
                    var plan = entManager.GetComponent<HTNComponent>(operativeUid).Plan;
                    log.AppendLine($"+{tick}: {position:0.0} [{(plan == null ? "" : string.Join(",", plan.BranchTraversalRecord))}] {plan?.CurrentOperator.GetType().Name}");

                    // Past the wall's end: beyond what it could see of the hostile from behind it.
                    steppedOut = position.Y > -1.5f;
                });
            }

            if (steppedOut != (kind == NpcOrderKind.Watch))
                TestContext.Out.WriteLine(log.ToString());

            if (kind == NpcOrderKind.Watch)
                Assert.That(steppedOut, "watching, it should step out to peek");
            else
                Assert.That(steppedOut, Is.False, "sent to stack up, it should keep to that and not step out to peek");
        }
        finally
        {
            await Server.WaitPost(() => tacticsSystem.UpdatesPaused = false);
        }
    }

    /// <summary>
    ///     <see cref="SetTimeOperator"/> takes its offset and jitter from the blackboard where the mob has them, and its own
    ///         otherwise.
    /// </summary>
    [Test]
    public async Task TestSetTimeGoesByTheMobsInterval()
    {
        var timing = Server.ResolveDependency<IGameTiming>();

        await Server.WaitAssertion(() =>
        {
            var setTime = new SetTimeOperator
            {
                Key = "DueAt",
                Offset = System.TimeSpan.FromSeconds(6),
                OffsetKey = "Interval",
                JitterKey = "IntervalJitter",
            };
            setTime.Initialize(Server.EntMan.EntitySysManager);

            var blackboard = new NPCBlackboard();
            setTime.Update(blackboard, 0f);
            Assert.That(blackboard.GetValue<System.TimeSpan>("DueAt"), Is.EqualTo(timing.CurTime + System.TimeSpan.FromSeconds(6)),
                "without the mob's own, its own offset");

            blackboard.SetValue("Interval", 20f);
            blackboard.SetValue("IntervalJitter", 0f);
            setTime.Update(blackboard, 0f);
            Assert.That(blackboard.GetValue<System.TimeSpan>("DueAt"), Is.EqualTo(timing.CurTime + System.TimeSpan.FromSeconds(20)),
                "with the mob's own, that");
        });
    }
}
