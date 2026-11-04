#nullable enable
using System.Linq;
using System.Text;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Doors;
using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Squad;
using Content.Server._KS14.NPC.Squad.Tactics;
using Content.Server.Gravity;
using Content.Server.NPC.HTN;
using Content.Shared._KS14.NPC;
using Content.Shared.Damage.Systems;
using Content.Shared.Doors.Components;
using Content.Shared.Gravity;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

public sealed class KsTempDoorLiveTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string Out = "C:/Users/decode/AppData/Local/Temp/claude/c--Space-SS14-Dev-Repo-claude-mnet-klovn-clone/ec960ef3-01e4-4c7e-ab0f-5e4617e5ef2d/scratchpad/door";

    [TestCase(1, false, false)]
    [TestCase(2, false, false)]
    [TestCase(3, false, false)]
    [TestCase(4, false, false)]
    [TestCase(5, false, false)]
    [TestCase(6, false, false)]
    [TestCase(7, false, false)]
    [TestCase(8, false, false)]
    
    
    
    public async Task Live(int run, bool nt, bool threatOnly)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var timing = server.ResolveDependency<IGameTiming>();
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default;
        EntityUid hostileUid = default;

        await server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-20, -12), new Vector2i(20, 12)).Owner;
            var gravityComponent = entManager.EnsureComponent<GravityComponent>(gridUid);
            entManager.System<GravitySystem>().EnableGravity(gridUid, gravityComponent);

            for (var x = -1; x <= 7; x++)
            {
                for (var y = -1; y <= 6; y++)
                {
                    if (x is > -1 and < 7 && y is > -1 and < 6)
                        continue;

                    if (x == -1 && y == 2)
                        SpawnPoweredDoorAt(entManager, "AirlockCommandLocked", gridUid, x, y);
                    else
                        SpawnAt(entManager, "WallSolid", gridUid, x, y);
                }
            }

            if (nt)
            {
                SpawnAt(entManager, "KsRandomHumanoidSpawnerOperativeNtArmedXL8", gridUid, -8, 2);
                SpawnAt(entManager, "KsRandomHumanoidSpawnerOperativeNtArmedXL8", gridUid, -8, 3);
            }
            else
            {
                SpawnAt(entManager, "KsSpawnMobOperativeRedactedSquad", gridUid, -8, 2);
            }

            hostileUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 3, 3);
        });

        await Pair.RunTicksSync(90);

        var log = new StringBuilder();

        await server.WaitPost(() =>
        {
            var doors = entManager.AllEntityQueryEnumerator<DoorComponent, TransformComponent>();
            while (doors.MoveNext(out var doorUid, out var doorComponent, out var doorTransform))
            {
                log.AppendLine($"door {doorUid} {doorTransform.LocalPosition} {doorComponent.State}");
            }

            var now = timing.CurTime;
            var query = entManager.EntityQueryEnumerator<NpcSquadMemberComponent, HTNComponent>();
            while (query.MoveNext(out var uid, out _, out _))
            {
                if (uid == hostileUid)
                    continue;

                entManager.System<SharedGodmodeSystem>().EnableGodmode(uid);

                // No access of their own, so that the door has to be forced.
                if (entManager.System<Content.Shared.Inventory.InventorySystem>().TryGetSlotEntity(uid, "id", out var idUid))
                    entManager.DeleteEntity(idUid);

                if (threatOnly)
                    entManager.System<NpcSquadSystem>().ReportThreat(uid, new EntityCoordinates(gridUid, new System.Numerics.Vector2(3.5f, 3.5f)));
                else
                    entManager.System<NpcPerceptionSystem>().SetContact(uid, hostileUid, new NpcContact(NpcContactState.Lost,
                        now, now, now, new EntityCoordinates(gridUid, new System.Numerics.Vector2(3.5f, 3.5f)), default, null, Reacted: true));
                log.AppendLine($"member {uid} tools={string.Join(",", entManager.System<Content.Shared.Inventory.InventorySystem>().GetHandOrInventoryEntities(uid).Select(e => entManager.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID).Where(id => id != null && (id.Contains("Jaws") || id.Contains("Crowbar"))))}");
            }
        });

        var last = new System.Collections.Generic.Dictionary<EntityUid, string>();
        var doorOpenedAt = -1;

        for (var i = 0; i < 400; i++)
        {
            await Pair.RunTicksSync(5);
            var tick = i * 5;
            await server.WaitPost(() =>
            {
                var doorQuery = entManager.AllEntityQueryEnumerator<DoorComponent>();
                while (doorOpenedAt < 0 && doorQuery.MoveNext(out _, out var doorState))
                {
                    if (doorState.State is DoorState.Open or DoorState.Opening)
                    {
                        doorOpenedAt = tick;
                        log.AppendLine($"DOOROPEN t{tick}");
                    }
                }

                var query = entManager.EntityQueryEnumerator<NpcSquadMemberComponent, HTNComponent, TransformComponent>();
                while (query.MoveNext(out var uid, out _, out var htnComponent, out var transformComponent))
                {
                    if (uid == hostileUid)
                        continue;

                    var plan = htnComponent.Plan is { } p ? string.Join(">", p.Tasks.Skip(p.Index).Take(3).Select(t => t.Operator.GetType().Name.Replace("Operator", ""))) : "noplan";
                    var order = entManager.System<NpcSquadTacticsSystem>().TryGetOrder(uid, out var o) ? $"{o.Kind}#{o.Id}" : "-";
                    var held = entManager.System<SharedHandsSystem>().GetActiveItem(uid) is { } heldUid ? entManager.GetComponent<MetaDataComponent>(heldUid).EntityPrototype?.ID : "-";
                    var state = $"{order} held={held} {plan}";
                    if (last.TryGetValue(uid, out var previous) && previous == state)
                        continue;

                    last[uid] = state;
                    log.AppendLine($"t{tick} {uid} {transformComponent.LocalPosition.Rounded()} {state}");
                }
            });
        }

        System.IO.File.WriteAllText($"{Out}{run}.txt", log.ToString());
    }
}
