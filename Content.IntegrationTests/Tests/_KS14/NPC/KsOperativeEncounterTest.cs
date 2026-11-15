#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Squad;
using Content.Server._KS14.NPC.Suppression;
using Content.Server.NPC.HTN;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     Live encounters with real operatives: a hostile shows itself beyond the end of a wall, then ducks behind it.
/// </summary>
[TestFixture]
public sealed class KsOperativeEncounterTest : GameTest
{
    // A light tree, for the tests in the dark: light levels need one, which the server only builds if told to at startup.
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true, ServerLightTree = true };

    private readonly ProtoId<HTNCompoundPrototype> _operativeRoot = "KsOperativeCombatCompound";

    private const int Encounters = 4;
    private const int ShownTicks = 45;
    private const int HiddenTicks = 450;

    /// <summary>
    ///     The operatives on the test map, all but the hostile, made unkillable: there is no air on a test map, and the
    ///         tests are about where they go, not whether they live.
    /// </summary>
    private static List<EntityUid> GetOperatives(IEntityManager entManager, MapId mapId, EntityUid hostileUid)
    {
        var operatives = new List<EntityUid>();
        var enumerator = entManager.EntityQueryEnumerator<NpcSquadMemberComponent, TransformComponent>();

        while (enumerator.MoveNext(out var uid, out _, out var transformComponent))
        {
            if (transformComponent.MapID != mapId || uid == hostileUid)
                continue;

            operatives.Add(uid);
            entManager.System<Content.Shared.Damage.Systems.SharedGodmodeSystem>().EnableGodmode(uid);
        }

        return operatives;
    }

    /// <summary>
    ///     Each time a hostile ducks out of sight, the squad should keep its head down for it: a burst or two of suppressive
    ///         fire at where it went, from whoever saw it go. Not every member, not every moment.
    /// </summary>
    [Test]
    public async Task TestRedactedSuppressWhereAHostileDucked()
    {
        if (!Server.ProtoMan.HasIndex(_operativeRoot))
            Assert.Ignore("the operative HTN is in the private _KsModule submodule, which is not present");

        var entManager = Server.EntMan;
        var tileDefinitionManager = Server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        var mobs = new List<EntityUid>();
        EntityUid gridUid = default, hostileUid = default;

        await Server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-12, -12), new Vector2i(12, 12)).Owner;
            GiveGravity(entManager, gridUid);

            // A cell to duck into, walled in all round: out of sight, and out of reach of any hunt for it.
            for (var x = 8; x <= 10; x++)
            {
                for (var y = -4; y <= -2; y++)
                {
                    if (x != 9 || y != -3)
                        SpawnAt(entManager, "WallSolid", gridUid, x, y);
                }
            }

            for (var y = -1; y <= 2; y++)
            {
                SpawnAt(entManager, "KsRandomHumanoidSpawnerOperativeRedacted", gridUid, -9, y);
            }

            hostileUid = SpawnAt(entManager, "KsSquadTestMobSyndicate", gridUid, 9, -3);
            entManager.System<Content.Shared.Damage.Systems.SharedGodmodeSystem>().EnableGodmode(hostileUid);
        });

        await Pair.RunTicksSync(240); // for a squad to form

        // Every duck taken: the chance of letting the moment pass (SuppressionChance, 75 by default) is a dial of its own,
        //      and at 75 one run in four would see two ducks go by.
        await Server.WaitPost(() =>
        {
            mobs.AddRange(GetOperatives(entManager, map.MapId, hostileUid));
            foreach (var mobUid in mobs)
            {
                entManager.GetComponent<HTNComponent>(mobUid).Blackboard.SetValue("SuppressionChance", 100f);

                // Suppression alone. A frag after it into the cell, when one comes up, has them all go in on the blast, which
                //      comes before suppressing (root.yml), and can break the cell open: then an encounter is not one duck.
                entManager.RemoveComponent<Content.Server._KS14.NPC.Grenades.NpcGrenadeThrowerComponent>(mobUid);
            }
        });

        Assert.That(mobs, Has.Count.EqualTo(4), "the test needs the four of them");

        var suppressionsPerEncounter = new List<int>();
        var log = new System.Text.StringBuilder();
        var transformSystem = entManager.System<SharedTransformSystem>();
        var suppressiveFireSystem = entManager.System<NpcSuppressiveFireSystem>();
        var cell = new EntityCoordinates(gridUid, new Vector2(9.5f, -2.5f));
        var inTheOpen = new EntityCoordinates(gridUid, new Vector2(4.5f, 6.5f));

        for (var encounter = 0; encounter < Encounters; encounter++)
        {
            var firing = new HashSet<EntityUid>();
            var bursts = 0;

            // Steps out where they can see it, then ducks into the cell, then the squad gets over it.
            await Server.WaitPost(() => transformSystem.SetCoordinates(hostileUid, inTheOpen));

            for (var tick = 0; tick < ShownTicks + HiddenTicks; tick += 3)
            {
                if (tick == ShownTicks)
                    await Server.WaitPost(() => transformSystem.SetCoordinates(hostileUid, cell));

                await Pair.RunTicksSync(3);
                await Server.WaitPost(() =>
                {
                    foreach (var mobUid in mobs)
                    {
                        if (!suppressiveFireSystem.IsFiring(mobUid))
                        {
                            firing.Remove(mobUid);
                            continue;
                        }

                        if (firing.Add(mobUid))
                        {
                            bursts++;
                            log.AppendLine($"encounter {encounter} +{tick}: {entManager.ToPrettyString(mobUid)} suppresses");
                        }
                    }
                });
            }

            suppressionsPerEncounter.Add(bursts);
            log.AppendLine($"encounter {encounter}: {bursts} bursts");
        }

        TestContext.Out.WriteLine(log.ToString());

        Assert.That(suppressionsPerEncounter.Count(bursts => bursts >= 1), Is.EqualTo(Encounters),
            $"every encounter should be suppressed: {string.Join(", ", suppressionsPerEncounter)}");
        Assert.That(suppressionsPerEncounter.Max(), Is.LessThanOrEqualTo(3),
            $"but only a burst or two each: {string.Join(", ", suppressionsPerEncounter)}");
    }

    /// <summary>
    ///     Half a squad in a firefight round a wall, the other half behind it, seeing nothing of it but able to walk round:
    ///         told of the hostile by those fighting it, they come round and join in, whatever kind of operative they are,
    ///         and whether or not they were in the fight before it moved round there.
    ///     <para>
    ///         In the dark too, with light detection on as on a station: then only close up can they see it, and they have to
    ///             get that close, not stop at the first line to it. They used to stand at a peek with a line to it, seeing
    ///             nothing, until the fight was over, then search the area.
    ///     </para>
    /// </summary>
    [TestCase("KsRandomHumanoidSpawnerOperativeRedacted", "KsRandomHumanoidSpawnerOperativeRedacted", "KsSquadTestMobSyndicate", false, false)]
    [TestCase("KsRandomHumanoidSpawnerOperativeSynArmedBulldog", "KsRandomHumanoidSpawnerOperativeSynArmedPistol", "KsSquadTestMobNanoTrasen", false, false)]
    [TestCase("KsRandomHumanoidSpawnerOperativeNtArmedXL8", "KsRandomHumanoidSpawnerOperativeNtArmedPistol", "KsSquadTestMobSyndicate", false, false)]
    [TestCase("KsRandomHumanoidSpawnerOperativeRedacted", "KsRandomHumanoidSpawnerOperativeRedacted", "KsSquadTestMobSyndicate", true, false)]
    [TestCase("KsRandomHumanoidSpawnerOperativeSynArmedBulldog", "KsRandomHumanoidSpawnerOperativeSynArmedPistol", "KsSquadTestMobNanoTrasen", true, false)]
    [TestCase("KsRandomHumanoidSpawnerOperativeSynArmedBulldog", "KsRandomHumanoidSpawnerOperativeSynArmedPistol", "KsSquadTestMobNanoTrasen", false, true)]
    [TestCase("KsRandomHumanoidSpawnerOperativeNtArmedXL8", "KsRandomHumanoidSpawnerOperativeNtArmedPistol", "KsSquadTestMobSyndicate", false, true)]
    public async Task TestTheRestOfTheSquadComeToAFight(string leaderSpawner, string memberSpawner, string hostile, bool allFoughtFirst, bool dark)
    {
        if (!Server.ProtoMan.HasIndex(_operativeRoot))
            Assert.Ignore("the operative HTN is in the private _KsModule submodule, which is not present");

        var entManager = Server.EntMan;
        var tileDefinitionManager = Server.ResolveDependency<ITileDefinitionManager>();
        var transformSystem = entManager.System<SharedTransformSystem>();
        var perceptionSystem = entManager.System<Content.Server._KS14.NPC.Perception.NpcPerceptionSystem>();
        // In the dark, as on a station with light detection on: the test map's light is black, so a hostile is only seen
        //      close up. The squadmates fighting it are; the rest have to come that close too, not just into a line to it.
        if (dark)
            await OverrideCVar(Content.IntegrationTests.Fixtures.Attributes.Side.Server, Content.Shared._KS14.CCVar.KsCCVars.NpcLightDetection, true);

        var map = await Pair.CreateTestMap();
        var mobs = new List<EntityUid>();
        var rear = new List<EntityUid>();
        EntityUid gridUid = default, hostileUid = default;

        await Server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-12, -12), new Vector2i(12, 12)).Owner;
            GiveGravity(entManager, gridUid);

            // A wall down the middle, open at the top: the way round is some twenty steps.
            for (var y = -12; y <= 6; y++)
            {
                SpawnAt(entManager, "WallSolid", gridUid, 0, y);
            }

            // An elite to lead them, as regular operatives only fall in behind one.
            for (var y = -1; y <= 2; y++)
            {
                SpawnAt(entManager, y == -1 ? leaderSpawner : memberSpawner, gridUid, -4, y);
            }

            // Out of everyone's sight until the fight starts.
            hostileUid = SpawnAt(entManager, hostile, gridUid, 8, -8);
            entManager.System<Content.Shared.Damage.Systems.SharedGodmodeSystem>().EnableGodmode(hostileUid);
        });

        await Pair.RunTicksSync(240); // for a squad to form

        // All of them in the fight first, on this side of the wall, before it moves round where only some can see it.
        if (allFoughtFirst)
        {
            await Server.WaitPost(() =>
            {
                GetOperatives(entManager, map.MapId, hostileUid);
                transformSystem.SetCoordinates(hostileUid, new EntityCoordinates(gridUid, new Vector2(-4.5f, 9.5f)));
            });

            await Pair.RunTicksSync(90);
        }

        await Server.WaitPost(() =>
        {
            mobs.AddRange(GetOperatives(entManager, map.MapId, hostileUid));

            Assert.That(entManager.System<NpcSquadSystem>().TryGetSquad(mobs[0], out var squad) && squad.Value.Comp.Members.Count >= 2,
                "the test needs a squad");

            // Half of it round the wall, in sight of the hostile; the rest behind it, back where they started.
            var members = squad!.Value.Comp.Members.ToList();
            var frontCount = members.Count / 2;

            for (var i = 0; i < members.Count; i++)
            {
                if (i < frontCount)
                    transformSystem.SetCoordinates(members[i], new EntityCoordinates(gridUid, new Vector2(3.5f, -7.5f - i)));
                else
                {
                    rear.Add(members[i]);
                    transformSystem.SetCoordinates(members[i], new EntityCoordinates(gridUid, new Vector2(-3.5f, -7.5f + i)));
                }
            }

            transformSystem.SetCoordinates(hostileUid, new EntityCoordinates(gridUid, new Vector2(dark ? 5.5f : 9.5f, -8.5f)));
        });

        var joined = new HashSet<EntityUid>();
        var log = new System.Text.StringBuilder();

        for (var tick = 0; tick < 900 && joined.Count < rear.Count; tick += 15)
        {
            await Pair.RunTicksSync(15);
            await Server.WaitPost(() =>
            {
                var line = new System.Text.StringBuilder($"+{tick,4}:");
                foreach (var mobUid in rear)
                {
                    if (perceptionSystem.TryGetContact(mobUid, hostileUid, out var contact) && contact.State == Content.Shared._KS14.NPC.NpcContactState.Visible)
                        joined.Add(mobUid);

                    var htn = entManager.GetComponent<HTNComponent>(mobUid);
                    var plan = htn.Plan;
                    var state = perceptionSystem.TryGetContact(mobUid, hostileUid, out var c) ? c.State.ToString() : "none";
                    var target = htn.Blackboard.TryGetValue<EntityCoordinates>("SupportTargetCoordinates", out var supportCoordinates, entManager) ? $"{supportCoordinates.EntityId}:{supportCoordinates.Position:0.0}" : "-";
                    var steering = entManager.TryGetComponent<Content.Server.NPC.Components.NPCSteeringComponent>(mobUid, out var steeringComponent)
                        ? $"{steeringComponent.Status} path={steeringComponent.CurrentPath.Count} to={steeringComponent.Coordinates.EntityId}:{steeringComponent.Coordinates.Position:0.0}"
                        : "no steering";
                    line.Append($" | {transformSystem.GetWorldPosition(mobUid):0.0} [{(plan == null ? "" : string.Join(",", plan.BranchTraversalRecord))}] {plan?.CurrentOperator.GetType().Name} {state} tgt={target} {steering}");
                }

                log.AppendLine(line.ToString());
            });
        }

        if (joined.Count < rear.Count)
            TestContext.Out.WriteLine(log.ToString());

        Assert.That(joined, Has.Count.EqualTo(rear.Count), "those behind the wall should come round to the fight, within 30s: a grenade or a burst round the corner first is fine, staying put is not");
    }

    /// <summary>
    ///     A lit L-shaped room, walled all round: two of a squad of four fighting a hostile at the far end of one leg, the
    ///         other two at the far end of the other leg, out of sight round the bend. Told of the hostile, those two come
    ///         round the bend and join in: in a narrow L, a long one, and one lit by station lights, with light detection on,
    ///         where the hostile keeps ducking into an alcove and out again.
    /// </summary>
    [TestCase(2, 10, false, false)]
    [TestCase(3, 18, false, false)]
    [TestCase(3, 14, true, true)]
    public async Task TestTheRestOfTheSquadComeRoundAnL(int width, int length, bool lights, bool ducks)
    {
        if (!Server.ProtoMan.HasIndex(_operativeRoot))
            Assert.Ignore("the operative HTN is in the private _KsModule submodule, which is not present");

        const string redacted = "KsRandomHumanoidSpawnerOperativeRedacted";
        var entManager = Server.EntMan;
        var tileDefinitionManager = Server.ResolveDependency<ITileDefinitionManager>();
        var transformSystem = entManager.System<SharedTransformSystem>();
        var perceptionSystem = entManager.System<Content.Server._KS14.NPC.Perception.NpcPerceptionSystem>();
        // Lit by station lights, with light detection on, as on a station: how lit the hostile is gets worked out, not
        //      taken as given.
        if (lights)
            await OverrideCVar(Content.IntegrationTests.Fixtures.Attributes.Side.Server, Content.Shared._KS14.CCVar.KsCCVars.NpcLightDetection, true);

        var map = await Pair.CreateTestMap();
        var rear = new List<EntityUid>();
        EntityUid gridUid = default, hostileUid = default;

        // The bend at the origin: one leg runs north from it, the other east.
        //      An alcove off the end of the east leg, for a hostile that ducks.
        bool IsFloor(int x, int y) => x >= 0 && y >= 0 && (x < width && y < length || y < width && x < length) ||
            ducks && x == length - 1 && (y == width || y == width + 1);

        await Server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-1, -1), new Vector2i(length, length)).Owner;
            GiveGravity(entManager, gridUid);

            for (var x = -1; x <= length; x++)
            {
                for (var y = -1; y <= length; y++)
                {
                    if (!IsFloor(x, y))
                        SpawnAt(entManager, "WallSolid", gridUid, x, y);
                }
            }

            if (lights)
            {
                for (var i = 1; i < length; i += 4)
                {
                    SpawnAt(entManager, "AlwaysPoweredWallLight", gridUid, width / 2, i);
                    SpawnAt(entManager, "AlwaysPoweredWallLight", gridUid, i, width / 2);
                }

                SpawnAt(entManager, "AlwaysPoweredWallLight", gridUid, length - 1, width / 2);
            }

            // Together at the end of the north leg, for a squad to form.
            for (var i = 0; i < 4; i++)
            {
                SpawnAt(entManager, redacted, gridUid, i % width, length - 1 - i / width);
            }

            // Walled in at the end of the east leg, out of everyone's sight until the fight starts.
            hostileUid = SpawnAt(entManager, "KsSquadTestMobSyndicate", gridUid, length - 1, 0);
            entManager.System<Content.Shared.Damage.Systems.SharedGodmodeSystem>().EnableGodmode(hostileUid);
            transformSystem.SetCoordinates(hostileUid, new EntityCoordinates(gridUid, new Vector2(length + 5.5f, length + 5.5f)));
        });

        await Pair.RunTicksSync(240); // for a squad to form

        await Server.WaitPost(() =>
        {
            var mobs = GetOperatives(entManager, map.MapId, hostileUid);

            Assert.That(entManager.System<NpcSquadSystem>().TryGetSquad(mobs[0], out var squad) && squad.Value.Comp.Members.Count == 4,
                "the test needs a squad of four");

            // Two near the end of the east leg, in sight of the hostile; the other two left at the end of the north leg.
            var members = squad!.Value.Comp.Members.ToList();
            for (var i = 0; i < members.Count; i++)
            {
                if (i < 2)
                    transformSystem.SetCoordinates(members[i], new EntityCoordinates(gridUid, new Vector2(length - 6.5f, 0.5f + i % width)));
                else
                    rear.Add(members[i]);
            }

            transformSystem.SetCoordinates(hostileUid, new EntityCoordinates(gridUid, new Vector2(length - 0.5f, 0.5f)));
        });

        var joined = new HashSet<EntityUid>();
        var log = new System.Text.StringBuilder();

        for (var tick = 0; tick < 900 && joined.Count < rear.Count; tick += 15)
        {
            await Pair.RunTicksSync(15);
            await Server.WaitPost(() =>
            {
                // Out for three seconds, in the alcove for one and a half, as one trading shots from round a corner.
                var hidden = ducks && tick % 135 >= 90;
                transformSystem.SetCoordinates(hostileUid, new EntityCoordinates(gridUid, hidden
                    ? new Vector2(length - 0.5f, width + 1.5f)
                    : new Vector2(length - 0.5f, 0.5f)));

                var line = new System.Text.StringBuilder($"+{tick,4}{(hidden ? " hid" : "")}:");
                foreach (var mobUid in rear)
                {
                    if (perceptionSystem.TryGetContact(mobUid, hostileUid, out var contact) && contact.State == Content.Shared._KS14.NPC.NpcContactState.Visible)
                        joined.Add(mobUid);

                    var htn = entManager.GetComponent<HTNComponent>(mobUid);
                    var plan = htn.Plan;
                    var state = perceptionSystem.TryGetContact(mobUid, hostileUid, out var c) ? c.State.ToString() : "none";
                    var order = entManager.TryGetComponent<Content.Server._KS14.NPC.Squad.Tactics.NpcOrderComponent>(mobUid, out var orderComponent) && orderComponent.Order is { } o ? o.Kind.ToString() : "-";
                    var steering = entManager.TryGetComponent<Content.Server.NPC.Components.NPCSteeringComponent>(mobUid, out var steeringComponent)
                        ? $"{steeringComponent.Status} path={steeringComponent.CurrentPath.Count} to={steeringComponent.Coordinates.Position:0.0}"
                        : "no steering";
                    line.Append($" | {transformSystem.GetWorldPosition(mobUid):0.0} [{(plan == null ? "" : string.Join(",", plan.BranchTraversalRecord))}] {plan?.CurrentOperator.GetType().Name} {state} order={order} {steering}");
                }

                log.AppendLine(line.ToString());
            });
        }

        if (joined.Count < rear.Count)
            TestContext.Out.WriteLine(log.ToString());

        Assert.That(joined, Has.Count.EqualTo(rear.Count), "those round the bend should come round to the fight, within 30s");
    }
}
