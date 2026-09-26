#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Squad;
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
            var squads = GetSquads(entManager);

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
            var squad = GetSquads(entManager).Single();
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
            var squad = GetSquads(entManager).Single();

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
            var squads = GetSquads(entManager);
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
            Assert.That(GetSquads(entManager), Has.Count.EqualTo(2), "the lone NPC starts out of range, in its own squad");
            transformSystem.SetCoordinates(loneUid, new EntityCoordinates(gridUid, new Vector2(6.5f, 0.5f)));
        });

        await Pair.RunTicksSync(SquadUpdateTicks);

        var sizes = System.Array.Empty<int>();
        await Pair.Server.WaitPost(() =>
        {
            sizes = GetSquads(entManager).Select(squad => squad.Comp.Members.Count).OrderBy(count => count).ToArray();
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
    ///     Every live squad. An emptied squad is only queued for deletion, so it is skipped here.
    /// </summary>
    private static List<Entity<NpcSquadComponent>> GetSquads(IEntityManager entManager)
    {
        var squads = new List<Entity<NpcSquadComponent>>();
        var squadEnumerator = entManager.EntityQueryEnumerator<NpcSquadComponent>();

        while (squadEnumerator.MoveNext(out var squadUid, out var squadComponent))
        {
            if (squadComponent.Members.Count > 0)
                squads.Add((squadUid, squadComponent));
        }

        return squads;
    }
}
