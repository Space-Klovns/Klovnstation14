#nullable enable
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;
using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Squad;
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
///     Live peeking (<c>peek.yml</c>): an operative that has just lost a hostile round a wall's end steps out to where it can
///         be seen, fires if it shows, and steps back if it does not. Before, only a squadmate's callout ever sent one
///         to peek (<c>support.yml</c>), so an operative holding cover never did.
/// </summary>
[TestFixture]
public sealed class KsNpcPeekTest : GameTest
{
    // Connected: a real NPC only runs while a player is about.
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private readonly ProtoId<HTNCompoundPrototype> _operativeRoot = "KsOperativeCombatCompound";

    /// <summary>
    ///     How long it gets, at most, in ticks: 50s. Planning shares 4ms of real time a tick (<c>HTNSystem._planQueue</c>), so on a
    ///         busy machine each of the several plans a peek takes spans more ticks; the loops end the moment it is done.
    /// </summary>
    private const int MaxTicks = 1500;

    /// <summary>
    ///     The operative at -3.5,-3.5, behind a wall that ends at y=-1, with a hostile at 6.5,0.5 it lost a moment ago (a
    ///         belief, staged). From past the wall's end it can see there; from where it stands it cannot.
    ///         <paramref name="hostileThere"/>: whether the hostile really is there, or walled in elsewhere.
    /// </summary>
    /// <returns>The operative, and the map it is on.</returns>
    private async Task<(EntityUid OperativeUid, MapId MapId)> SetUp(bool hostileThere)
    {
        if (!Server.ProtoMan.HasIndex(_operativeRoot))
            Assert.Ignore("the operative HTN is in the private _KsModule submodule, which is not present");

        var entManager = Server.EntMan;
        var tileDefinitionManager = Server.ResolveDependency<ITileDefinitionManager>();
        var timing = Server.ResolveDependency<IGameTiming>();
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

        await Server.WaitPost(() =>
        {
            // The spawner's mob, not the spawner.
            var enumerator = entManager.EntityQueryEnumerator<NpcSquadMemberComponent, TransformComponent>();
            while (enumerator.MoveNext(out var uid, out _, out var transformComponent))
            {
                if (transformComponent.MapID == map.MapId && uid != hostileUid)
                    operativeUid = uid;
            }

            entManager.System<Content.Shared.Damage.Systems.SharedGodmodeSystem>().EnableGodmode(operativeUid);

            if (!hostileThere)
            {
                // Walled in, out of sight of the corner.
                for (var x = 9; x <= 11; x++)
                {
                    for (var y = -8; y <= -6; y++)
                    {
                        if (x != 10 || y != -7)
                            SpawnAt(entManager, "WallSolid", gridUid, x, y);
                    }
                }

                entManager.System<SharedTransformSystem>().SetCoordinates(hostileUid, new EntityCoordinates(gridUid, new Vector2(10.5f, -6.5f)));
            }

            // Where it was last seen, a couple of seconds ago, and the fight not over.
            var now = timing.CurTime;
            entManager.System<NpcPerceptionSystem>().SetContact(operativeUid, hostileUid, new NpcContact(NpcContactState.Lost,
                now - System.TimeSpan.FromSeconds(3), now - System.TimeSpan.FromSeconds(2), now - System.TimeSpan.FromSeconds(2),
                new EntityCoordinates(gridUid, LostAt), default, null, Reacted: true));

            entManager.GetComponent<HTNComponent>(operativeUid).Blackboard
                .SetValue(EnsureVirtualMarkerOperator.MarkerSet, new HashSet<string> { "OpInCombat" });
        });

        return (operativeUid, map.MapId);
    }

    /// <summary>
    ///     Where the hostile was lost, on the grid and, as the grid sits at the map's origin, on the map.
    /// </summary>
    private static readonly Vector2 LostAt = new(6.5f, 0.5f);

    /// <summary>
    ///     How near where it stood counts as there, and how far from it as having left.
    /// </summary>
    private const float HomeRange = 1.5f;

    /// <summary>
    ///     Whether the operative has stepped out: it has left where it stood for somewhere it can see where the hostile was
    ///         lost. That is what a peek is for, wherever the spot it picks is: asked by the sight check the peek's own
    ///         search uses, where a line drawn across the map by the test was missed by a spot 0.1 of a tile short of it.
    /// </summary>
    private bool HasSteppedOut(MapId mapId, Vector2 position, Vector2 home)
    {
        return (position - home).Length() >= HomeRange &&
            Server.EntMan.System<NpcLineOfSightSystem>().InLineOfSight(new MapCoordinates(position, mapId), new MapCoordinates(LostAt, mapId), (LostAt - position).Length() + 1f);
    }

    /// <summary>
    ///     The test's own arithmetic: it cannot see where the hostile was lost from where it stands, so it has to step out.
    /// </summary>
    private async Task<Vector2> GetHome(EntityUid operativeUid, MapId mapId)
    {
        var home = Vector2.Zero;
        await Server.WaitAssertion(() =>
        {
            home = Server.EntMan.System<SharedTransformSystem>().GetWorldPosition(operativeUid);
            Assert.That(Server.EntMan.System<NpcLineOfSightSystem>().InLineOfSight(new MapCoordinates(home, mapId), new MapCoordinates(LostAt, mapId), (LostAt - home).Length() + 1f),
                Is.False, "the test needs the operative to start out of sight of where the hostile was lost");
        });

        return home;
    }

    /// <summary>
    ///     It steps out, sees the hostile and fires: the operative went past the wall's end and then fired.
    /// </summary>
    [Test]
    public async Task TestStepsOutAndFiresAtALostHostile()
    {
        var (operativeUid, mapId) = await SetUp(hostileThere: true);
        var entManager = Server.EntMan;
        var transformSystem = entManager.System<SharedTransformSystem>();
        var steppedOut = false;
        var fired = false;
        var log = new System.Text.StringBuilder();
        var home = await GetHome(operativeUid, mapId);

        for (var tick = 0; tick < MaxTicks && !fired; tick += 5)
        {
            await Pair.RunTicksSync(5);
            await Server.WaitPost(() =>
            {
                var position = transformSystem.GetWorldPosition(operativeUid);
                var plan = entManager.GetComponent<HTNComponent>(operativeUid).Plan;
                log.AppendLine($"+{tick}: {position:0.0} [{(plan == null ? "" : string.Join(",", plan.BranchTraversalRecord))}] {plan?.CurrentOperator.GetType().Name}");

                steppedOut |= HasSteppedOut(mapId, position, home);
                fired = entManager.TryGetComponent<NpcRecentShooterComponent>(operativeUid, out var shooterComponent) && shooterComponent.LastShotAt > System.TimeSpan.Zero;
            });
        }

        if (!steppedOut || !fired)
            TestContext.Out.WriteLine(log.ToString());

        Assert.That(steppedOut, "it should have stepped out to where it could see where the hostile went");
        Assert.That(fired, "and fired on the hostile it saw there");
    }

    /// <summary>
    ///     It steps out, finds nothing, and steps back to the cover it left.
    /// </summary>
    [Test]
    public async Task TestStepsBackWhenNothingShows()
    {
        var (operativeUid, mapId) = await SetUp(hostileThere: false);
        var entManager = Server.EntMan;
        var transformSystem = entManager.System<SharedTransformSystem>();
        var steppedOut = false;
        var backHome = false;
        var log = new System.Text.StringBuilder();
        var home = await GetHome(operativeUid, mapId);

        for (var tick = 0; tick < MaxTicks && !backHome; tick += 5)
        {
            await Pair.RunTicksSync(5);
            await Server.WaitPost(() =>
            {
                var position = transformSystem.GetWorldPosition(operativeUid);
                var plan = entManager.GetComponent<HTNComponent>(operativeUid).Plan;
                log.AppendLine($"+{tick}: {position:0.0} [{(plan == null ? "" : string.Join(",", plan.BranchTraversalRecord))}] {plan?.CurrentOperator.GetType().Name}");

                steppedOut |= HasSteppedOut(mapId, position, home);
                backHome |= steppedOut && (position - home).Length() < HomeRange;
            });
        }

        if (!steppedOut || !backHome)
            TestContext.Out.WriteLine(log.ToString());

        Assert.That(steppedOut, "it should have stepped out to where it could see where the hostile went");
        Assert.That(backHome, "and, with nothing there, back to where it stood");
    }
}
