#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Squad;
using Content.Server._KS14.NPC.Squad.Tactics;
using Content.Shared._KS14.NPC;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     A hunt goes by when the hostile was really lost, not by when the squad got round to hunting it: a squad busy for a few
///         seconds after it ducked out of sight (suppressing, pushing) has already spent those seconds of its watch, and
///         guesses it has got that much further. Perception is real here, a sighting and then a loss; the tactics system is
///         paused and driven by hand, so it can be made to start late.
/// </summary>
public sealed class KsNpcHuntTimingTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string Mob = "KsSquadTestMobSyndicate";

    /// <summary>
    ///     How long after the loss the squad first gets to hunt, in seconds: less than the watch (4s), more than half of it.
    /// </summary>
    private const float LateStart = 3f;

    [Test]
    public async Task TestALateHuntWatchesFromTheLoss()
    {
        var scene = await SetUp(locker: false);
        var lostAt = await SeeThenLose(scene);

        await RunSeconds(LateStart);

        await Pair.Server.WaitAssertion(() =>
        {
            scene.TacticsSystem.UpdateNow();
            var hunt = scene.TacticsSystem.GetHunt(scene.SquadUid);

            Assert.That(hunt, Is.Not.Null, "a hostile lost a few seconds ago is hunted");
            Assert.That(hunt!.LostAt, Is.EqualTo(lostAt), "from when it was lost, not when the hunt began");
            Assert.That(hunt.Phase, Is.EqualTo(NpcHuntPhase.Watch), "still within the watch, counted from the loss");
        });

        // The watch is 4s from the loss: 3s gone at the start, so 1.5s more is past it. From the hunt's start, it would not be.
        await RunSeconds(1.5f);

        await Pair.Server.WaitAssertion(() =>
        {
            scene.TacticsSystem.UpdateNow();
            var hunt = scene.TacticsSystem.GetHunt(scene.SquadUid);
            Assert.That(hunt, Is.Not.Null, "still hunting: a hunt that had ended would not be watching either");
            Assert.That(hunt!.Phase, Is.Not.EqualTo(NpcHuntPhase.Watch),
                "the watch is over 4s after the loss, however late the hunt began");
        });
    }

    /// <summary>
    ///     Gone in plain sight beside a closed locker, a hostile is suspected to be in it: still a hostile the squad lost, and
    ///         hunted as one.
    /// </summary>
    [Test]
    public async Task TestASuspectedHostileIsHunted()
    {
        var scene = await SetUp(locker: true);
        var lostAt = await SeeThenLose(scene);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(scene.PerceptionSystem.TryGetContact(scene.MemberUids[0], scene.HostileUid, out var contact));
            Assert.That(contact.State, Is.EqualTo(NpcContactState.Suspected), "the test needs it suspected in the locker");

            scene.TacticsSystem.UpdateNow();
            var hunt = scene.TacticsSystem.GetHunt(scene.SquadUid);
            Assert.That(hunt, Is.Not.Null, "a hostile suspected in a locker is still hunted");
            Assert.That(hunt!.LostAt, Is.EqualTo(lostAt));
        });
    }

    /// <summary>
    ///     The guess at where it has got to moves on with the time since the loss while the squad watches, rather than staying
    ///         where it was the moment the hunt began.
    /// </summary>
    [Test]
    public async Task TestTheGuessMovesOnDuringTheWatch()
    {
        var scene = await SetUp(locker: false);
        EntityCoordinates first = default;

        await Pair.Server.WaitPost(() =>
        {
            foreach (var memberUid in scene.MemberUids)
            {
                scene.PerceptionSystem.UpdateNow(memberUid);
            }
        });

        await RunSeconds(1f);

        await Pair.Server.WaitAssertion(() =>
        {
            foreach (var memberUid in scene.MemberUids)
            {
                scene.PerceptionSystem.UpdateNow(memberUid);
                Assert.That(scene.PerceptionSystem.IsVisible(memberUid, scene.HostileUid), "the test needs it seen first");

                // Seen walking south at 2 tiles a second, then gone.
                scene.PerceptionSystem.TryGetContact(memberUid, scene.HostileUid, out var contact);
                scene.PerceptionSystem.SetContact(memberUid, scene.HostileUid, contact with { LastKnownVelocity = new Vector2(0f, -2f) });
            }

            MoveAway(scene);

            foreach (var memberUid in scene.MemberUids)
            {
                scene.PerceptionSystem.UpdateNow(memberUid);
            }

            scene.TacticsSystem.UpdateNow();
            first = scene.TacticsSystem.GetHunt(scene.SquadUid)!.PredictedCoordinates;
        });

        await RunSeconds(1.5f);

        await Pair.Server.WaitAssertion(() =>
        {
            scene.TacticsSystem.UpdateNow();
            var hunt = scene.TacticsSystem.GetHunt(scene.SquadUid)!;
            Assert.That(hunt.Phase, Is.EqualTo(NpcHuntPhase.Watch));

            var transformSystem = scene.EntManager.System<SharedTransformSystem>();
            var moved = transformSystem.ToMapCoordinates(first).Position.Y - transformSystem.ToMapCoordinates(hunt.PredictedCoordinates).Position.Y;
            Assert.That(moved, Is.GreaterThan(2f), "1.5s on at 2 tiles a second, the guess is about 3 tiles further south");
        });
    }

    /// <summary>
    ///     Every member sees the hostile and reacts to it; then it is gone. Returns when it was last seen.
    /// </summary>
    private async Task<System.TimeSpan> SeeThenLose(Scene scene)
    {
        await Pair.Server.WaitPost(() =>
        {
            foreach (var memberUid in scene.MemberUids)
            {
                scene.PerceptionSystem.UpdateNow(memberUid);
            }
        });

        await RunSeconds(1f);

        var lostAt = System.TimeSpan.Zero;

        await Pair.Server.WaitAssertion(() =>
        {
            foreach (var memberUid in scene.MemberUids)
            {
                scene.PerceptionSystem.UpdateNow(memberUid);
                Assert.That(scene.PerceptionSystem.IsVisible(memberUid, scene.HostileUid), "the test needs it seen first");
            }

            MoveAway(scene);

            foreach (var memberUid in scene.MemberUids)
            {
                scene.PerceptionSystem.UpdateNow(memberUid);
                Assert.That(scene.PerceptionSystem.TryGetContact(memberUid, scene.HostileUid, out var contact));
                Assert.That(contact.State, Is.Not.EqualTo(NpcContactState.Visible));

                if (contact.LastSeen > lostAt)
                    lostAt = contact.LastSeen;
            }
        });

        return lostAt;
    }

    /// <summary>
    ///     Off the grid and far out of range: gone, as far as anyone can tell.
    /// </summary>
    private static void MoveAway(Scene scene)
    {
        scene.EntManager.System<SharedTransformSystem>()
            .SetCoordinates(scene.HostileUid, new EntityCoordinates(scene.GridUid, new Vector2(0.5f, 40.5f)));
    }

    private async Task<Scene> SetUp(bool locker)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var tacticsSystem = entManager.System<NpcSquadTacticsSystem>();
        var map = await Pair.CreateTestMap();
        var memberUids = new List<EntityUid>();
        EntityUid gridUid = default, hostileUid = default, squadUid = default;

        await server.WaitPost(() =>
        {
            tacticsSystem.UpdatesPaused = true;
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-8, -8), new Vector2i(8, 8)).Owner;
            memberUids.Add(SpawnAt(entManager, Mob, gridUid, -3, 1));
            memberUids.Add(SpawnAt(entManager, Mob, gridUid, -3, -1));

            if (locker)
                SpawnAt(entManager, TestLocker, gridUid, 2, 1);
        });

        await Pair.RunTicksSync(240); // for the squad to form

        await server.WaitAssertion(() =>
        {
            var squadSystem = entManager.System<NpcSquadSystem>();
            var squads = memberUids.Select(memberUid => squadSystem.GetSquadOrSelf(memberUid)).Distinct().ToList();
            Assert.That(squads, Has.Count.EqualTo(1), "the test needs them in one squad");
            squadUid = squads[0];
            hostileUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 2, 0);
        });

        return new Scene(entManager, tacticsSystem, entManager.System<NpcPerceptionSystem>(), gridUid, squadUid, memberUids, hostileUid);
    }

    private sealed record Scene(
        IEntityManager EntManager,
        NpcSquadTacticsSystem TacticsSystem,
        NpcPerceptionSystem PerceptionSystem,
        EntityUid GridUid,
        EntityUid SquadUid,
        List<EntityUid> MemberUids,
        EntityUid HostileUid);
}
