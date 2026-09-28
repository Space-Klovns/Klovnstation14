#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Squad;
using Content.Server.NPC.HTN;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
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
///     Squad self-assignment, the size cap, leader succession, and assimilation of undersized squads.
/// </summary>
public sealed class KsNpcSquadFormationTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    /// <summary>
    ///     A squad update is once a second; this is comfortably more than two.
    /// </summary>
    private const int SquadUpdateTicks = 90;

    private static readonly ProtoId<DamageTypePrototype> BluntDamage = "Blunt";

    /// <summary>
    ///     Six friendly NPCs together make two squads of at most four, each with exactly one leader who is
    ///         also a member.
    /// </summary>
    [Test]
    public async Task TestFormationRespectsCapAndHasOneLeader()
    {
        var (entManager, gridUid) = await SetUpGrid(new Vector2i(-5, -5), new Vector2i(10, 5));
        var mobUids = new List<EntityUid>();

        await Pair.Server.WaitPost(() =>
        {
            for (var x = 0; x < 6; x++)
            {
                mobUids.Add(SpawnAt(entManager, SyndicateMob, gridUid, x, 0));
            }
        });

        await Pair.RunTicksSync(SquadUpdateTicks);

        await Pair.Server.WaitAssertion(() =>
        {
            var squads = GetSquads(entManager, gridUid);

            Assert.That(squads, Has.Count.EqualTo(2));
            Assert.That(squads.Select(squad => squad.Comp.Members.Count).OrderBy(count => count),
                Is.EqualTo(new[] { 2, 4 }));

            foreach (var squad in squads)
            {
                Assert.That(squad.Comp.Leader, Is.Not.Null);
                Assert.That(squad.Comp.Members, Does.Contain(squad.Comp.Leader!.Value), "the leader counts as a member");
            }

            foreach (var mobUid in mobUids)
            {
                var squadUid = entManager.GetComponent<NpcSquadMemberComponent>(mobUid).Squad;
                Assert.That(squadUid, Is.Not.Null, "every NPC should have self-assigned");
                Assert.That(entManager.GetComponent<NpcSquadComponent>(squadUid!.Value).Members, Does.Contain(mobUid));
            }
        });
    }

    /// <summary>
    ///     When the leader dies, the member with the most health left takes over - not merely the next in line.
    /// </summary>
    [Test]
    public async Task TestLeaderSuccessionPicksHealthiest()
    {
        var (entManager, gridUid) = await SetUpGrid(new Vector2i(-5, -5), new Vector2i(5, 5));
        var protoManager = Pair.Server.ResolveDependency<IPrototypeManager>();
        var damageableSystem = entManager.System<DamageableSystem>();
        var mobStateSystem = entManager.System<MobStateSystem>();

        await Pair.Server.WaitPost(() =>
        {
            for (var x = 0; x < 3; x++)
            {
                SpawnAt(entManager, SyndicateMob, gridUid, x, 0);
            }
        });

        await Pair.RunTicksSync(SquadUpdateTicks);

        EntityUid leaderUid = default;
        EntityUid healthierUid = default;
        EntityUid hurtUid = default;

        await Pair.Server.WaitPost(() =>
        {
            var squad = GetSquads(entManager, gridUid).Single();
            leaderUid = squad.Comp.Leader!.Value;

            var others = squad.Comp.Members.Where(uid => uid != leaderUid).ToList();

            // Ordered so that picking the first remaining member would pick the wrong one.
            hurtUid = others[0];
            healthierUid = others[1];

            var blunt = protoManager.Index(BluntDamage);
            damageableSystem.SetDamage(hurtUid, new DamageSpecifier(blunt, 50));
            damageableSystem.SetDamage(healthierUid, new DamageSpecifier(blunt, 10));

            Assert.That(damageableSystem.GetTotalDamage(hurtUid), Is.GreaterThan(damageableSystem.GetTotalDamage(healthierUid)),
                "the setup should have hurt one member more than the other");

            mobStateSystem.ChangeMobState(leaderUid, MobState.Dead);
        });

        await Pair.Server.WaitAssertion(() =>
        {
            var squad = GetSquads(entManager, gridUid).Single();

            Assert.That(squad.Comp.Members, Does.Not.Contain(leaderUid), "a dead NPC leaves its squad");
            Assert.That(squad.Comp.Leader, Is.EqualTo(healthierUid), "the healthiest member should lead");
        });
    }

    /// <summary>
    ///     A lone NPC's squad merges into a nearby squad it can see.
    /// </summary>
    [Test]
    public async Task TestUndersizedSquadAssimilates()
    {
        var squads = await RunAssimilation(wallBetween: false);
        Assert.That(squads, Is.EqualTo(new[] { 4 }));
    }

    /// <summary>
    ///     Control for <see cref="TestUndersizedSquadAssimilates"/>: the same squads, just as close, but with a
    ///         wall between them, stay apart. Without this, assimilation that ignored line of sight would pass.
    /// </summary>
    [Test]
    public async Task TestAssimilationNeedsLineOfSight()
    {
        var squads = await RunAssimilation(wallBetween: true);
        Assert.That(squads, Is.EqualTo(new[] { 1, 3 }));
    }

    /// <summary>
    ///     A member restating a threat it already knew of does not make it any fresher - members restate theirs on
    ///         every replan, which would otherwise keep an unreachable threat alive forever - while a restated
    ///         threat somewhere new, or a fresh report even at the same spot, does.
    /// </summary>
    [Test]
    public async Task TestRepeatedThreatReportDoesNotRefresh()
    {
        var (entManager, gridUid) = await SetUpGrid(new Vector2i(-5, -5), new Vector2i(10, 5));
        var squadSystem = entManager.System<NpcSquadSystem>();
        EntityUid mobUid = default;

        await Pair.Server.WaitPost(() => mobUid = SpawnAt(entManager, SyndicateMob, gridUid, 0, 0));
        await Pair.RunTicksSync(SquadUpdateTicks);

        var firstReportedAt = TimeSpan.Zero;
        await Pair.Server.WaitPost(() =>
        {
            squadSystem.ReportThreat(mobUid, new EntityCoordinates(gridUid, new Vector2(5.5f, 0.5f)));
            firstReportedAt = GetSquads(entManager, gridUid).Single().Comp.ThreatReportedAt;
        });

        await Pair.RunTicksSync(30);

        await Pair.Server.WaitAssertion(() =>
        {
            var squad = GetSquads(entManager, gridUid).Single();

            squadSystem.RepeatThreat(mobUid, new EntityCoordinates(gridUid, new Vector2(5.5f, 0.5f)));
            Assert.That(squad.Comp.ThreatReportedAt, Is.EqualTo(firstReportedAt), "a restated threat must not be refreshed");

            squadSystem.ReportThreat(mobUid, new EntityCoordinates(gridUid, new Vector2(5.5f, 0.5f)));
            Assert.That(squad.Comp.ThreatReportedAt, Is.GreaterThan(firstReportedAt),
                "a fresh report - a hostile still holding its position - should refresh it, even at the same spot");
        });

        await Pair.RunTicksSync(30);

        await Pair.Server.WaitAssertion(() =>
        {
            var squad = GetSquads(entManager, gridUid).Single();
            var reportedAt = squad.Comp.ThreatReportedAt;

            squadSystem.RepeatThreat(mobUid, new EntityCoordinates(gridUid, new Vector2(9.5f, 0.5f)));
            Assert.That(squad.Comp.ThreatReportedAt, Is.GreaterThan(reportedAt), "a restated threat somewhere new should count");
        });
    }

    /// <summary>
    ///     NPCs that cannot lead never found a squad: on their own, they stay disorganised.
    /// </summary>
    [Test]
    public async Task TestFollowersAloneStayDisorganised()
    {
        var (entManager, gridUid) = await SetUpGrid(new Vector2i(-5, -5), new Vector2i(10, 5));
        var followerUids = new List<EntityUid>();

        await Pair.Server.WaitPost(() =>
        {
            for (var x = 0; x < 3; x++)
            {
                followerUids.Add(SpawnAt(entManager, FollowerMob, gridUid, x, 0));
            }
        });

        await Pair.RunTicksSync(SquadUpdateTicks);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(GetSquads(entManager, gridUid), Is.Empty, "followers with nobody to lead them should not form a squad");
            Assert.That(followerUids.All(uid => entManager.GetComponent<NpcSquadMemberComponent>(uid).Squad == null));
        });
    }

    /// <summary>
    ///     A leader gathers followers into its squad; when it dies with no one left able to lead, the squad
    ///         breaks up rather than a follower taking over.
    /// </summary>
    [Test]
    public async Task TestLeaderlessSquadDisbands()
    {
        var (entManager, gridUid) = await SetUpGrid(new Vector2i(-5, -5), new Vector2i(10, 5));
        var mobStateSystem = entManager.System<MobStateSystem>();
        EntityUid leaderUid = default;
        var followerUids = new List<EntityUid>();

        await Pair.Server.WaitPost(() =>
        {
            leaderUid = SpawnAt(entManager, SyndicateMob, gridUid, 0, 0);
            followerUids.Add(SpawnAt(entManager, FollowerMob, gridUid, 1, 0));
            followerUids.Add(SpawnAt(entManager, FollowerMob, gridUid, 2, 0));
        });

        await Pair.RunTicksSync(SquadUpdateTicks);

        await Pair.Server.WaitAssertion(() =>
        {
            var squad = GetSquads(entManager, gridUid).Single();
            Assert.That(squad.Comp.Leader, Is.EqualTo(leaderUid), "only the NPC that can lead should lead");
            Assert.That(squad.Comp.Members, Has.Count.EqualTo(3), "the followers should have joined it");

            mobStateSystem.ChangeMobState(leaderUid, MobState.Dead);

            Assert.That(followerUids.All(uid => entManager.GetComponent<NpcSquadMemberComponent>(uid).Squad == null),
                "with nobody left who can lead, the squad should break up");
        });
    }

    /// <summary>
    ///     Succession only considers members that can lead, however healthy the others are.
    /// </summary>
    [Test]
    public async Task TestSuccessionSkipsFollowers()
    {
        var (entManager, gridUid) = await SetUpGrid(new Vector2i(-5, -5), new Vector2i(10, 5));
        var protoManager = Pair.Server.ResolveDependency<IPrototypeManager>();
        var damageableSystem = entManager.System<DamageableSystem>();
        var mobStateSystem = entManager.System<MobStateSystem>();

        var leaderUids = new List<EntityUid>();
        EntityUid followerUid = default;

        await Pair.Server.WaitPost(() =>
        {
            leaderUids.Add(SpawnAt(entManager, SyndicateMob, gridUid, 0, 0));
            leaderUids.Add(SpawnAt(entManager, SyndicateMob, gridUid, 1, 0));
            followerUid = SpawnAt(entManager, FollowerMob, gridUid, 2, 0);
        });

        await Pair.RunTicksSync(SquadUpdateTicks);

        await Pair.Server.WaitAssertion(() =>
        {
            var squad = GetSquads(entManager, gridUid).Single();
            var leaderUid = squad.Comp.Leader!.Value;
            var otherLeaderUid = leaderUids.Single(uid => uid != leaderUid);

            // The remaining would-be leader is badly hurt; the follower is untouched.
            damageableSystem.SetDamage(otherLeaderUid, new DamageSpecifier(protoManager.Index(BluntDamage), 80));
            mobStateSystem.ChangeMobState(leaderUid, MobState.Dead);

            Assert.That(squad.Comp.Leader, Is.EqualTo(otherLeaderUid), "a follower must never take over, however healthy");
            Assert.That(squad.Comp.Members, Does.Contain(followerUid));
        });
    }

    /// <summary>
    ///     A leader's shared keys reach members with nothing at that key, and never overwrite what a member has.
    /// </summary>
    [Test]
    public async Task TestLeaderSharesBlackboardKeys()
    {
        var (entManager, gridUid) = await SetUpGrid(new Vector2i(-5, -5), new Vector2i(10, 5));
        EntityUid leaderUid = default;
        EntityUid emptyFollowerUid = default;
        EntityUid informedFollowerUid = default;

        await Pair.Server.WaitPost(() =>
        {
            leaderUid = SpawnAt(entManager, SharingLeaderMob, gridUid, 0, 0);
            emptyFollowerUid = SpawnAt(entManager, FollowerMob, gridUid, 1, 0);
            informedFollowerUid = SpawnAt(entManager, FollowerMob, gridUid, 2, 0);
        });

        await Pair.RunTicksSync(SquadUpdateTicks);

        await Pair.Server.WaitPost(() =>
        {
            entManager.GetComponent<HTNComponent>(leaderUid).Blackboard.SetValue(SharedKey, "from the leader");
            entManager.GetComponent<HTNComponent>(informedFollowerUid).Blackboard.SetValue(SharedKey, "its own");
        });

        await Pair.RunTicksSync(SquadUpdateTicks);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(GetSquads(entManager, gridUid).Single().Comp.Leader, Is.EqualTo(leaderUid));
            Assert.Multiple(() =>
            {
                Assert.That(entManager.GetComponent<HTNComponent>(emptyFollowerUid).Blackboard.GetValue<string>(SharedKey),
                    Is.EqualTo("from the leader"), "a member with nothing at the key should get the leader value");
                Assert.That(entManager.GetComponent<HTNComponent>(informedFollowerUid).Blackboard.GetValue<string>(SharedKey),
                    Is.EqualTo("its own"), "a member value must not be overwritten");
            });

            // The member deals with it and drops it.
            entManager.GetComponent<HTNComponent>(emptyFollowerUid).Blackboard.Remove<string>(SharedKey);
        });

        await Pair.RunTicksSync(SquadUpdateTicks);

        await Pair.Server.WaitAssertion(() =>
        {
            var followerBlackboard = entManager.GetComponent<HTNComponent>(emptyFollowerUid).Blackboard;
            Assert.That(followerBlackboard.ContainsKey(SharedKey), Is.False,
                "a value the member was already given, and dropped, must not be handed back");

            entManager.GetComponent<HTNComponent>(leaderUid).Blackboard.SetValue(SharedKey, "news");
        });

        await Pair.RunTicksSync(SquadUpdateTicks);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(entManager.GetComponent<HTNComponent>(emptyFollowerUid).Blackboard.GetValue<string>(SharedKey),
                Is.EqualTo("news"), "a new value from the leader should be handed down");

            // The leader drops it too, then later learns the very same thing again.
            entManager.GetComponent<HTNComponent>(emptyFollowerUid).Blackboard.Remove<string>(SharedKey);
            entManager.GetComponent<HTNComponent>(leaderUid).Blackboard.Remove<string>(SharedKey);
        });

        await Pair.RunTicksSync(SquadUpdateTicks);
        await Pair.Server.WaitPost(() => entManager.GetComponent<HTNComponent>(leaderUid).Blackboard.SetValue(SharedKey, "news"));
        await Pair.RunTicksSync(SquadUpdateTicks);

        await Pair.Server.WaitAssertion(() =>
            Assert.That(entManager.GetComponent<HTNComponent>(emptyFollowerUid).Blackboard.GetValue<string>(SharedKey),
                Is.EqualTo("news"), "once the leader has dropped a value, the same value coming back is news again"));
    }

    /// <summary>
    ///     What a member dropped is remembered per leader: a new leader sharing the same value hands it down again,
    ///         since the member has no way of knowing the new leader's value is the one it already dealt with.
    /// </summary>
    [Test]
    public async Task TestNewLeaderSharesWhatOldOneDid()
    {
        var (entManager, gridUid) = await SetUpGrid(new Vector2i(-5, -5), new Vector2i(10, 5));
        EntityUid firstLeaderUid = default;
        EntityUid secondLeaderUid = default;
        EntityUid followerUid = default;

        await Pair.Server.WaitPost(() =>
        {
            firstLeaderUid = SpawnAt(entManager, SharingLeaderMob, gridUid, 0, 0);
            secondLeaderUid = SpawnAt(entManager, SharingLeaderMob, gridUid, 1, 0);
            followerUid = SpawnAt(entManager, FollowerMob, gridUid, 2, 0);
        });

        await Pair.RunTicksSync(SquadUpdateTicks);

        await Pair.Server.WaitPost(() =>
        {
            // Whichever of the two founded the squad leads it.
            if (GetSquads(entManager, gridUid).Single().Comp.Leader == secondLeaderUid)
                (firstLeaderUid, secondLeaderUid) = (secondLeaderUid, firstLeaderUid);

            entManager.GetComponent<HTNComponent>(firstLeaderUid).Blackboard.SetValue(SharedKey, "the same news");
        });

        await Pair.RunTicksSync(SquadUpdateTicks);

        await Pair.Server.WaitPost(() =>
        {
            Assert.That(entManager.GetComponent<HTNComponent>(followerUid).Blackboard.GetValue<string>(SharedKey),
                Is.EqualTo("the same news"), "the first leader should hand its value down");

            // The follower deals with it and drops it; the second leader has heard the same thing.
            entManager.GetComponent<HTNComponent>(followerUid).Blackboard.Remove<string>(SharedKey);
            entManager.GetComponent<HTNComponent>(secondLeaderUid).Blackboard.SetValue(SharedKey, "the same news");
            entManager.DeleteEntity(firstLeaderUid);
        });

        await Pair.RunTicksSync(SquadUpdateTicks);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(GetSquads(entManager, gridUid).Single().Comp.Leader, Is.EqualTo(secondLeaderUid));
            Assert.That(entManager.GetComponent<HTNComponent>(followerUid).Blackboard.GetValue<string>(SharedKey),
                Is.EqualTo("the same news"), "a new leader's value should be handed down, even one the old leader shared");
        });
    }

    /// <summary>
    ///     Hostile factions standing together never share a squad.
    /// </summary>
    [Test]
    public async Task TestHostileFactionsNeverShareSquad()
    {
        var (entManager, gridUid) = await SetUpGrid(new Vector2i(-5, -5), new Vector2i(5, 5));

        await Pair.Server.WaitPost(() =>
        {
            SpawnAt(entManager, SyndicateMob, gridUid, 0, 0);
            SpawnAt(entManager, NanoTrasenMob, gridUid, 1, 0);
            SpawnAt(entManager, SyndicateMob, gridUid, 2, 0);
            SpawnAt(entManager, NanoTrasenMob, gridUid, 3, 0);
        });

        await Pair.RunTicksSync(SquadUpdateTicks);

        await Pair.Server.WaitAssertion(() =>
        {
            var squads = GetSquads(entManager, gridUid);
            Assert.That(squads, Has.Count.EqualTo(2));

            foreach (var squad in squads)
            {
                var prototypes = squad.Comp.Members
                    .Select(uid => entManager.GetComponent<MetaDataComponent>(uid).EntityPrototype!.ID)
                    .Distinct()
                    .ToList();

                Assert.That(prototypes, Has.Count.EqualTo(1), "a squad mixed hostile factions");
            }
        });
    }

    /// <summary>
    ///     Forms a squad of three and a lone NPC far apart, then brings the lone one close, optionally with a
    ///         wall in the way, and returns the resulting squad sizes in ascending order.
    /// </summary>
    private async Task<int[]> RunAssimilation(bool wallBetween)
    {
        var (entManager, gridUid) = await SetUpGrid(new Vector2i(-5, -5), new Vector2i(40, 5));
        var transformSystem = entManager.System<SharedTransformSystem>();
        EntityUid loneUid = default;

        await Pair.Server.WaitPost(() =>
        {
            for (var x = 0; x < 3; x++)
            {
                SpawnAt(entManager, SyndicateMob, gridUid, x, 0);
            }

            loneUid = SpawnAt(entManager, SyndicateMob, gridUid, 35, 0);

            if (!wallBetween)
                return;

            for (var y = -5; y <= 5; y++)
            {
                SpawnAt(entManager, "WallSolid", gridUid, 4, y);
            }
        });

        await Pair.RunTicksSync(SquadUpdateTicks);

        await Pair.Server.WaitPost(() =>
        {
            Assert.That(GetSquads(entManager, gridUid), Has.Count.EqualTo(2), "the lone NPC starts out of range, in its own squad");
            transformSystem.SetCoordinates(loneUid, new EntityCoordinates(gridUid, new Vector2(6.5f, 0.5f)));
        });

        await Pair.RunTicksSync(SquadUpdateTicks);

        var sizes = System.Array.Empty<int>();
        await Pair.Server.WaitPost(() =>
        {
            sizes = GetSquads(entManager, gridUid).Select(squad => squad.Comp.Members.Count).OrderBy(count => count).ToArray();
        });

        return sizes;
    }

    private async Task<(IEntityManager EntManager, EntityUid GridUid)> SetUpGrid(Vector2i min, Vector2i max)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default;

        await server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, min, max).Owner;
        });

        return (entManager, gridUid);
    }

    /// <summary>
    ///     Every live squad led from this test's grid. Pooled pairs are reused between tests, so squads from
    ///         earlier tests may still exist on other maps. An emptied squad is only queued for deletion, so it
    ///         is skipped here.
    /// </summary>
    private static List<Entity<NpcSquadComponent>> GetSquads(IEntityManager entManager, EntityUid gridUid)
    {
        var squads = new List<Entity<NpcSquadComponent>>();
        var squadEnumerator = entManager.EntityQueryEnumerator<NpcSquadComponent>();

        while (squadEnumerator.MoveNext(out var squadUid, out var squadComponent))
        {
            if (squadComponent.Members.Count > 0 &&
                squadComponent.Leader is { } leaderUid &&
                entManager.GetComponent<TransformComponent>(leaderUid).GridUid == gridUid)
                squads.Add((squadUid, squadComponent));
        }

        return squads;
    }
}
