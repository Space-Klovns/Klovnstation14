#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Squad;
using Content.Server._KS14.NPC.Squad.Tactics;
using Content.Shared._KS14.NPC;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     The corners of what a squad does together in a fight (<c>NpcSquadTacticsSystem.Fight.cs</c>) that
///         <see cref="KsNpcSquadFightTest"/> does not reach: a squad all to one side of the hostile, where angles wrap round, and a
///         flanker that is gone while nobody has the hostile in sight. Tactics are driven by hand, and what each member sees is
///         staged.
/// </summary>
public sealed class KsNpcSquadFightEdgeTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string FighterMob = "KsSquadFightEdgeTestMob";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsSquadFightEdgeTestMob
  components:
  - type: Gun # a gun in hand, as far as anyone asks: only the armed go in
  - type: NpcSquadMember
    tactics:
      pushing: true
      flanking: true
";

    private sealed record Scene(IEntityManager EntManager, NpcSquadTacticsSystem TacticsSystem, EntityUid GridUid, List<EntityUid> Members, EntityUid HostileUid);

    /// <summary>
    ///     Pushers all due west of the hostile, either side of where angles wrap round (179 and -179 degrees), still go in each
    ///         to a spot of its own: in order by the plain angle, those two were a whole turn apart, and were sent to one tile.
    /// </summary>
    [Test]
    public async Task TestPushersEitherSideOfDueWestGoToTheirOwnSpots()
    {
        var scene = await SetUp((-6, -1), (-6, 0), (-6, 1));

        await Pair.Server.WaitAssertion(() =>
        {
            var transformSystem = scene.EntManager.System<SharedTransformSystem>();
            var hostileCoordinates = scene.EntManager.GetComponent<TransformComponent>(scene.HostileUid).Coordinates;
            scene.TacticsSystem.NoteBlast(scene.Members[0], hostileCoordinates);
            StageContacts(scene, NpcContactState.Visible);
            scene.TacticsSystem.UpdateNow();

            var spots = new List<System.Numerics.Vector2>();
            foreach (var memberUid in scene.Members)
            {
                Assert.That(scene.TacticsSystem.TryGetOrder(memberUid, out var order) && order.Kind == NpcOrderKind.Push,
                    "the test needs all three pushing, on the blast");
                spots.Add(transformSystem.ToMapCoordinates(order.Coordinates).Position);
            }

            for (var i = 0; i < spots.Count; i++)
            {
                for (var j = i + 1; j < spots.Count; j++)
                {
                    Assert.That((spots[i] - spots[j]).Length(), Is.GreaterThan(0.9f),
                        $"two pushers were sent to one spot: {string.Join(" ", spots)}");
                }
            }
        });
    }

    /// <summary>
    ///     A flanker deleted - gibbed, say - while nobody has the hostile in sight ends the flank. The flank is otherwise kept
    ///         on for a moment with the hostile out of sight, and working out how far a deleted flanker has to go threw, every
    ///         update, for every squad updated after this one.
    /// </summary>
    [Test]
    public async Task TestAFlankerDeletedOutOfSightEndsTheFlank()
    {
        var scene = await SetUp((-6, -1), (-6, 0), (-6, 1));
        EntityUid? flankerUid = null;

        for (var i = 0; i < 40 && flankerUid == null; i++)
        {
            await Pair.Server.WaitPost(() =>
            {
                StageContacts(scene, NpcContactState.Visible);
                scene.TacticsSystem.UpdateNow();

                foreach (var memberUid in scene.Members)
                {
                    if (scene.TacticsSystem.TryGetOrder(memberUid, out var order) && order.Kind == NpcOrderKind.Flank)
                        flankerUid = memberUid;
                }
            });

            await Pair.RunTicksSync(5);
        }

        Assert.That(flankerUid, Is.Not.Null, "the test needs a flank on");

        await Pair.Server.WaitAssertion(() =>
        {
            var squadUid = scene.EntManager.System<NpcSquadSystem>().GetSquadOrSelf(scene.Members.First(memberUid => memberUid != flankerUid));
            var fightComponent = scene.EntManager.GetComponent<NpcSquadFightComponent>(squadUid);
            Assert.That(fightComponent.FlankTargetUid, Is.EqualTo(scene.HostileUid), "the test needs a flank on");

            scene.EntManager.DeleteEntity(flankerUid!.Value);
            scene.Members.Remove(flankerUid.Value);

            // The front has ducked back: nobody has it in sight, a moment after someone did.
            StageContacts(scene, NpcContactState.Lost);
            Assert.DoesNotThrow(() => scene.TacticsSystem.UpdateNow());
            Assert.That(fightComponent.FlankTargetUid, Is.Null, "with its flanker gone, the flank is over");
        });
    }

    private static void StageContacts(Scene scene, NpcContactState state)
    {
        var now = Robust.Shared.IoC.IoCManager.Resolve<IGameTiming>().CurTime;
        var perceptionSystem = scene.EntManager.System<NpcPerceptionSystem>();
        var coordinates = scene.EntManager.GetComponent<TransformComponent>(scene.HostileUid).Coordinates;

        foreach (var memberUid in scene.Members)
        {
            perceptionSystem.SetContact(memberUid, scene.HostileUid, new NpcContact(state,
                now, now, now, coordinates, default, null, Reacted: true, ReactAt: now));
        }
    }

    /// <summary>
    ///     A squad at <paramref name="memberTiles"/> and a hostile at 1,0 in the open, on a grid from -12,-8 to 12,8, with the
    ///         squad formed and tactics paused.
    /// </summary>
    private async Task<Scene> SetUp(params (int X, int Y)[] memberTiles)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var tacticsSystem = entManager.System<NpcSquadTacticsSystem>();
        var map = await Pair.CreateTestMap();
        var members = new List<EntityUid>();
        EntityUid gridUid = default, hostileUid = default;

        await server.WaitPost(() =>
        {
            tacticsSystem.UpdatesPaused = true;
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-12, -8), new Vector2i(12, 8)).Owner;

            foreach (var (x, y) in memberTiles)
            {
                members.Add(SpawnAt(entManager, FighterMob, gridUid, x, y));
            }
        });

        await Pair.RunTicksSync(240); // for the squad to form

        await server.WaitAssertion(() =>
        {
            var squadSystem = entManager.System<NpcSquadSystem>();
            Assert.That(members.Select(memberUid => squadSystem.GetSquadOrSelf(memberUid)).Distinct().Count(), Is.EqualTo(1),
                "the test needs them in one squad");
            hostileUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 1, 0);
        });

        return new Scene(entManager, tacticsSystem, gridUid, members, hostileUid);
    }
}
