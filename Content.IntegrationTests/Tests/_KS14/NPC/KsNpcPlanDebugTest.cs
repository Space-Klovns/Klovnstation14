#nullable enable
using System.Linq;
using Content.Client._KS14.NPC;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.LineOfFire;
using Content.Server._KS14.NPC.Squad.Tactics;
using Content.Server.NPC.Components;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     What the planner's debug commands send gets to a client, and its overlay goes up and comes down. Debug systems get no
///         tests as a rule, but a message the client cannot read only fails once someone goes to use it. What the overlays
///         look like is for looking at: a headless client does not draw.
/// </summary>
public sealed class KsNpcPlanDebugTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    /// <summary>
    ///     A squad behind a wall of glass with a hostile on the far side: a plan, for someone to watch.
    /// </summary>
    [Test]
    public async Task TestSquadPlanFramesReachTheClient()
    {
        var entManager = Pair.Server.ResolveDependency<IEntityManager>();
        await SetUpBehindGlass(entManager);

        var debugSystem = entManager.System<NpcSquadPlanDebugSystem>();
        await Pair.Server.WaitPost(() => debugSystem.Toggle(Pair.Player!, null));

        var clientSystem = Pair.Client.EntMan.System<SquadPlanDebugSystem>();
        var overlayManager = Pair.Client.ResolveDependency<IOverlayManager>();

        await RunUntil(() => overlayManager.HasOverlay<SquadPlanDebugOverlay>() && clientSystem.Frames.Count > 0);

        await Pair.Client.WaitAssertion(() =>
        {
            Assert.That(overlayManager.HasOverlay<SquadPlanDebugOverlay>(), "the overlay should be up");
            Assert.That(clientSystem.Frames, Is.Not.Empty, "the client should have heard of the plan");

            var frame = clientSystem.Frames.Values.First().Frame;
            Assert.That(frame.Shots, Is.Not.Empty);
            Assert.That(frame.Shots.All(shot => !shot.Clear), "the glass should be stopping every shot");
        });

        await Pair.Server.WaitPost(() => debugSystem.Toggle(Pair.Player!, null));
        await RunUntil(() => !overlayManager.HasOverlay<SquadPlanDebugOverlay>());

        await Pair.Client.WaitAssertion(() => Assert.That(overlayManager.HasOverlay<SquadPlanDebugOverlay>(), Is.False, "the overlay should be down"));
    }

    /// <summary>
    ///     An NPC with a gun on something shows how far its shot would get, and what stopped it.
    /// </summary>
    [Test]
    public async Task TestLineOfFireFramesReachTheClient()
    {
        var entManager = Pair.Server.ResolveDependency<IEntityManager>();
        var (mobUid, hostileUid) = await SetUpBehindGlass(entManager);

        var debugSystem = entManager.System<NpcLineOfFireDebugSystem>();

        await Pair.Server.WaitPost(() =>
        {
            debugSystem.Toggle(Pair.Player!, null);
            entManager.EnsureComponent<NPCRangedCombatComponent>(mobUid).Target = hostileUid;
        });

        var clientSystem = Pair.Client.EntMan.System<LineOfFireDebugSystem>();
        var overlayManager = Pair.Client.ResolveDependency<IOverlayManager>();

        await RunUntil(() => overlayManager.HasOverlay<LineOfFireDebugOverlay>() && clientSystem.Latest?.Shots.Count > 0);

        await Pair.Client.WaitAssertion(() =>
        {
            Assert.That(overlayManager.HasOverlay<LineOfFireDebugOverlay>(), "the overlay should be up");
            Assert.That(clientSystem.Latest?.Shots, Is.Not.Null.And.Not.Empty);

            var shot = clientSystem.Latest!.Shots[0];
            Assert.That(shot.Clear, Is.False);
            Assert.That(shot.Blocker, Is.Not.Null, "it should say what stopped the shot");
        });

        await Pair.Server.WaitPost(() => debugSystem.Toggle(Pair.Player!, null));
        await RunUntil(() => !overlayManager.HasOverlay<LineOfFireDebugOverlay>());

        await Pair.Client.WaitAssertion(() => Assert.That(overlayManager.HasOverlay<LineOfFireDebugOverlay>(), Is.False, "the overlay should be down"));
    }

    /// <summary>
    ///     Runs until <paramref name="done"/> is so on the client. A client that has only just connected takes a while to get
    ///         round to its first network event, so there is no one number of ticks to wait.
    /// </summary>
    private async Task RunUntil(System.Func<bool> done)
    {
        var finished = false;

        for (var i = 0; i < 300 && !finished; i += 10)
        {
            await Pair.RunTicksSync(10);
            await Pair.Client.WaitPost(() => finished = done());
        }
    }

    private async Task<(EntityUid MobUid, EntityUid HostileUid)> SetUpBehindGlass(IEntityManager entManager)
    {
        var tileDefinitionManager = Pair.Server.ResolveDependency<ITileDefinitionManager>();
        var handsSystem = entManager.System<SharedHandsSystem>();
        var map = await Pair.CreateTestMap();
        EntityUid mobUid = default, hostileUid = default;

        await Pair.Server.WaitPost(() =>
        {
            entManager.System<NpcSquadTacticsSystem>().UpdatesPaused = false; // a test before this one on this server may not have

            var gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-6, -6), new Vector2i(6, 6)).Owner;

            for (var y = -6; y <= 6; y++)
            {
                SpawnAt(entManager, "KsSquadPlanTestArmouredGlass", gridUid, 2, y);
            }

            mobUid = SpawnAt(entManager, "KsSquadPlanTestMob", gridUid, 0, 0);
            hostileUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 4, 0);

            handsSystem.AddHand(mobUid, "right", HandLocation.Right);
            Assert.That(handsSystem.TryPickup(mobUid, SpawnAt(entManager, "KsSquadPlanTestGun", gridUid, 0, 0), "right"), "the test needs it holding the gun");
        });

        return (mobUid, hostileUid);
    }
}
