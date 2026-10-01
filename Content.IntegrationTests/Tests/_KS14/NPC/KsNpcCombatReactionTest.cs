#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;
using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Squad;
using Content.Server._KS14.NPC.Squad;
using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared._KS14.NPC;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     Reaction time (see <see cref="NpcPerceptionSystem"/>), squad contact, virtual markers, and the firing distance
///         NPCs keep with pellet weapons.
/// </summary>
public sealed class KsNpcCombatReactionTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    /// <summary>
    ///     A calm NPC holds fire on a target it has only just seen, and reacts once it has had it in sight for its
    ///         reaction time (0.6s on the test mob).
    /// </summary>
    [Test]
    public async Task TestReactionTimeDelaysFirstReaction()
    {
        var (entManager, npcUid, targetUid) = await SetUpLoner();
        var perceptionSystem = entManager.System<NpcPerceptionSystem>();

        await Pair.Server.WaitAssertion(() =>
        {
            perceptionSystem.UpdateNow(npcUid);
            Assert.That(perceptionSystem.TryGetContact(npcUid, targetUid, out var contact) && contact.State == NpcContactState.Visible,
                "the target should be in sight");
            Assert.That(contact.Reacted, Is.False, "a target seen for the first time should not be reacted to yet");
        });

        await Pair.RunTicksSync(24); // 0.8s

        await Pair.Server.WaitAssertion(() =>
        {
            perceptionSystem.UpdateNow(npcUid);
            perceptionSystem.TryGetContact(npcUid, targetUid, out var contact);
            Assert.That(contact.Reacted, "a target in sight for longer than the reaction time should be reacted to");
        });
    }

    /// <summary>
    ///     An alert NPC - already in combat - reacts at once.
    /// </summary>
    [Test]
    public async Task TestAlertNpcReactsAtOnce()
    {
        var (entManager, npcUid, targetUid) = await SetUpLoner();
        var perceptionSystem = entManager.System<NpcPerceptionSystem>();

        await Pair.Server.WaitAssertion(() =>
        {
            entManager.GetComponent<HTNComponent>(npcUid).Blackboard
                .SetValue(EnsureVirtualMarkerOperator.MarkerSet, new HashSet<string> { "OpInCombat" });

            perceptionSystem.UpdateNow(npcUid);
            perceptionSystem.TryGetContact(npcUid, targetUid, out var contact);
            Assert.That(contact.Reacted, "an NPC already in combat should react at once");
        });
    }

    /// <summary>
    ///     A target out of sight for a moment is still reacted to when it comes back; one out of sight for longer
    ///         than the reaction forget time (2s) needs a fresh reaction.
    /// </summary>
    [Test]
    public async Task TestTargetOutOfSightIsForgotten()
    {
        var (entManager, npcUid, targetUid) = await SetUpLoner();
        var perceptionSystem = entManager.System<NpcPerceptionSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();
        EntityCoordinates inSight = default;

        await Pair.Server.WaitPost(() =>
        {
            inSight = entManager.GetComponent<TransformComponent>(targetUid).Coordinates;
            perceptionSystem.UpdateNow(npcUid);
        });
        await Pair.RunTicksSync(24);

        // Reacted to, then out of sight - out of range - for 1s, then back.
        await Pair.Server.WaitPost(() =>
        {
            perceptionSystem.UpdateNow(npcUid);
            transformSystem.SetCoordinates(targetUid, inSight.Offset(new System.Numerics.Vector2(0f, 20f)));
            perceptionSystem.UpdateNow(npcUid);
        });
        await Pair.RunTicksSync(30);

        await Pair.Server.WaitAssertion(() =>
        {
            transformSystem.SetCoordinates(targetUid, inSight);
            perceptionSystem.UpdateNow(npcUid);
            perceptionSystem.TryGetContact(npcUid, targetUid, out var contact);
            Assert.That(contact.Reacted, "a target out of sight for a moment should still be reacted to");

            transformSystem.SetCoordinates(targetUid, inSight.Offset(new System.Numerics.Vector2(0f, 20f)));
            perceptionSystem.UpdateNow(npcUid);
        });

        await Pair.RunTicksSync(90); // 3s unseen, past the 2s reaction forget time

        await Pair.Server.WaitAssertion(() =>
        {
            transformSystem.SetCoordinates(targetUid, inSight);
            perceptionSystem.UpdateNow(npcUid);
            perceptionSystem.TryGetContact(npcUid, targetUid, out var contact);
            Assert.That(contact.Reacted, Is.False, "a target lost for longer than the forget time should need a fresh reaction");
        });
    }

    /// <summary>
    ///     Contact makes the whole squad engaged, and clearing the threat ends it.
    /// </summary>
    [Test]
    public async Task TestSquadContactEngagesSquad()
    {
        var (entManager, npcUid, targetUid) = await SetUpPair();
        var squadSystem = entManager.System<NpcSquadSystem>();
        var window = System.TimeSpan.FromSeconds(20);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(squadSystem.IsEngaged(npcUid, window), Is.False, "a squad that has seen nothing is not engaged");

            squadSystem.ReportContact(npcUid, entManager.GetComponent<TransformComponent>(targetUid).Coordinates);
            Assert.That(squadSystem.IsEngaged(npcUid, window), "contact should engage the squad");

            squadSystem.ClearThreat(npcUid);
            Assert.That(squadSystem.IsEngaged(npcUid, window), Is.False, "a cleared threat should end the engagement");
        });
    }

    /// <summary>
    ///     A marker can be added to a set that already holds others, and removed again. Adding used to clone the
    ///         existing set without the new marker in it.
    /// </summary>
    [Test]
    public async Task TestVirtualMarkersAddAndRemove()
    {
        var (entManager, npcUid, _) = await SetUpPair();
        var ensure = new EnsureVirtualMarkerOperator { Id = "KsTestMarker" };
        var remove = new RemoveVirtualMarkerOperator { Id = "KsTestMarker" };

        await Pair.Server.WaitAssertion(() =>
        {
            IoCManager.InjectDependencies(ensure);
            IoCManager.InjectDependencies(remove);

            var blackboard = new NPCBlackboard();
            blackboard.SetValue(NPCBlackboard.Owner, npcUid);
            blackboard.SetValue(EnsureVirtualMarkerOperator.MarkerSet, new HashSet<string> { "KsOtherMarker" });

            // Both complete synchronously; there is nothing to wait on.
#pragma warning disable RA0004
            ApplyEffects(blackboard, ensure.Plan(blackboard, default).Result.Effects);
            var markers = blackboard.GetValue<HashSet<string>>(EnsureVirtualMarkerOperator.MarkerSet);
            Assert.That(markers, Is.EquivalentTo(new[] { "KsOtherMarker", "KsTestMarker" }));

            ApplyEffects(blackboard, remove.Plan(blackboard, default).Result.Effects);
#pragma warning restore RA0004
            markers = blackboard.GetValue<HashSet<string>>(EnsureVirtualMarkerOperator.MarkerSet);
            Assert.That(markers, Is.EquivalentTo(new[] { "KsOtherMarker" }));
        });
    }

    /// <summary>
    ///     A buckshot shotgun counts its pellet spread, so the NPC's preferred firing distance is close - not the
    ///         dozens of tiles a 2-degree single projectile gets.
    /// </summary>
    [Test]
    public async Task TestShotgunFiringDistanceCountsPellets()
    {
        var (entManager, npcUid, targetUid) = await SetUpPair();
        var jukeSystem = entManager.System<NPCJukeSystem>();
        var gunSystem = entManager.System<GunSystem>();

        await Pair.Server.WaitAssertion(() =>
        {
            var shotgunUid = entManager.SpawnEntity("WeaponShotgunKammerer", entManager.GetComponent<TransformComponent>(npcUid).Coordinates);
            Assert.That(gunSystem.TryGetGun(shotgunUid, out var gunEntity), "the shotgun should be a gun");

            var (spread, projectiles) = jukeSystem.GetShotSpread(gunEntity);

            Assert.Multiple(() =>
            {
                Assert.That(projectiles, Is.GreaterThan(1), "buckshot fires several pellets");
                Assert.That(spread.Degrees, Is.GreaterThan(gunEntity.Comp.MaxAngleModified.Degrees),
                    "the pellet spread should widen the gun's own");
                Assert.That(jukeSystem.GetDesiredFiringDistance(targetUid, spread, 1.8f), Is.LessThan(5f),
                    "a shotgun's preferred distance should be close range");
            });
        });
    }

    /// <summary>
    ///     An NPC with no squad heads for the threat it knows of when it has nowhere better to hold, rather than
    ///         staying where it is.
    /// </summary>
    [Test]
    public async Task TestSquadlessNpcAnchorsOnItsThreat()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        var anchorOperator = new GetSquadHoldAnchorOperator();

        EntityUid gridUid = default;
        EntityUid followerUid = default;

        await server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-5, -5), new Vector2i(10, 5)).Owner;
            followerUid = SpawnAt(entManager, FollowerMob, gridUid, 0, 0);
        });

        await Pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            entManager.EntitySysManager.DependencyCollection.InjectDependencies(anchorOperator, oneOff: true);

            var threatCoordinates = new EntityCoordinates(gridUid, new System.Numerics.Vector2(8.5f, 0.5f));
            var blackboard = new NPCBlackboard();
            blackboard.SetValue(NPCBlackboard.Owner, followerUid);
            blackboard.SetValue(anchorOperator.ThreatKey, threatCoordinates);

#pragma warning disable RA0004 // completes synchronously
            var effects = anchorOperator.Plan(blackboard, default).Result.Effects!;
#pragma warning restore RA0004

            Assert.That(entManager.GetComponent<NpcSquadMemberComponent>(followerUid).Squad, Is.Null,
                "a follower on its own should have no squad");
            Assert.That(effects[anchorOperator.Key], Is.EqualTo(threatCoordinates),
                "a squadless NPC should hold around the threat it knows of");
        });
    }

    /// <summary>
    ///     Light detection switched on without a server light tree - the ordinary test server has none - does not
    ///         hide anyone: every target counts as lit.
    /// </summary>
    [Test]
    public async Task TestLightDetectionWithoutLightTreeTreatsTargetsAsLit()
    {
        await OverrideCVar(Content.IntegrationTests.Fixtures.Attributes.Side.Server, Content.Shared._KS14.CCVar.KsCCVars.NpcLightDetection, true);
        await AssertTargetsCountAsLitWithoutLightTree();
    }

    /// <summary>
    ///     Turning the server light tree cvar on mid-round builds no tree - it is only read at startup - so it must
    ///         not make light detection believe there is one, and leave every target reading as pitch dark.
    /// </summary>
    [Test]
    public async Task TestLightTreeCvarTurnedOnMidRoundTreatsTargetsAsLit()
    {
        await OverrideCVar(Content.IntegrationTests.Fixtures.Attributes.Side.Server, Content.Shared._KS14.CCVar.KsCCVars.NpcLightDetection, true);
        await OverrideCVar(Content.IntegrationTests.Fixtures.Attributes.Side.Server, Robust.Shared.CVars.LookupEnableServerLightTree, true);
        await AssertTargetsCountAsLitWithoutLightTree();
    }

    private async Task AssertTargetsCountAsLitWithoutLightTree()
    {
        var (entManager, _, targetUid) = await SetUpPair();
        var lightDetectionSystem = entManager.System<NpcLightDetectionSystem>();

        await Pair.Server.WaitAssertion(() =>
        {
            var mapUid = entManager.GetComponent<TransformComponent>(targetUid).MapUid!.Value;
            Assert.That(entManager.HasComponent<Robust.Shared.ComponentTrees.LightTreeComponent>(mapUid), Is.False,
                "this test server should have no light tree");
            Assert.That(lightDetectionSystem.GetLightLevel(targetUid), Is.EqualTo(1f),
                "with no light tree to compute from, a target should count as lit");
        });
    }

    private static void ApplyEffects(NPCBlackboard blackboard, Dictionary<string, object>? effects)
    {
        if (effects == null)
            return;

        foreach (var (key, value) in effects)
        {
            blackboard.SetValue(key, value);
        }
    }

    /// <summary>
    ///     A squadless NPC - so nothing makes it alert but itself - asleep, so only <c>UpdateNow</c> moves its
    ///         perception along, and a target that has only just appeared six tiles away.
    /// </summary>
    private async Task<(IEntityManager EntManager, EntityUid NpcUid, EntityUid TargetUid)> SetUpLoner()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();

        EntityUid gridUid = default;
        EntityUid npcUid = default;
        EntityUid targetUid = default;

        await server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-5, -5), new Vector2i(10, 5)).Owner;
            npcUid = SpawnAt(entManager, LonerMob, gridUid, 0, 0);
        });

        await Pair.RunTicksSync(10);

        await server.WaitPost(() =>
        {
            entManager.System<NPCSystem>().SleepNPC(npcUid);
            targetUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 6, 0);
        });

        return (entManager, npcUid, targetUid);
    }

    /// <summary>
    ///     A squad NPC, asleep, and a target to look at, on an open grid, with the squad formed.
    /// </summary>
    private async Task<(IEntityManager EntManager, EntityUid NpcUid, EntityUid TargetUid)> SetUpPair()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();

        EntityUid npcUid = default;
        EntityUid targetUid = default;

        EntityUid gridUid = default;

        await server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-5, -5), new Vector2i(10, 5)).Owner;
            npcUid = SpawnAt(entManager, SyndicateMob, gridUid, 0, 0);
        });

        await Pair.RunTicksSync(90);

        // Asleep before the target appears: an awake one would spot it, call it out, and engage its squad on its own.
        await server.WaitPost(() =>
        {
            entManager.System<NPCSystem>().SleepNPC(npcUid);
            targetUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 6, 0);
        });

        return (entManager, npcUid, targetUid);
    }
}
