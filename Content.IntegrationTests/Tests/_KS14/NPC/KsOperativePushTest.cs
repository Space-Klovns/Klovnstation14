#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Squad;
using Content.Server._KS14.NPC.Squad.Tactics;
using Content.Shared._KS14.NPC;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     Live, real Redacted through the real HTN: a hostile shooting at them from across a room, that then goes quiet. They
///         push: orders go out, and they close on it.
/// </summary>
public sealed class KsOperativePushTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private const string RedactedSpawner = "KsRandomHumanoidSpawnerOperativeRedacted";

    [Test]
    public async Task TestRedactedPushOnAHostileThatGoesQuiet()
    {
        if (!Server.ProtoMan.HasIndex<EntityPrototype>(RedactedSpawner))
            Assert.Ignore("the Redacted are in the private _KsModule submodule, which is not present");

        var entManager = Server.EntMan;
        var tileDefinitionManager = Server.ResolveDependency<ITileDefinitionManager>();
        var transformSystem = entManager.System<SharedTransformSystem>();
        var tacticsSystem = entManager.System<NpcSquadTacticsSystem>();
        var map = await Pair.CreateTestMap();
        var mobs = new List<EntityUid>();
        EntityUid gridUid = default, hostileUid = default;

        await Server.WaitPost(() =>
        {
            // Live: a test before this one on the same server may have left the squad tactics paused.
            tacticsSystem.UpdatesPaused = false;
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-13, -8), new Vector2i(13, 8)).Owner;
            GiveGravity(entManager, gridUid);

            for (var x = -13; x <= 13; x++)
            {
                for (var y = -8; y <= 8; y++)
                {
                    if (x is -13 or 13 || y is -8 or 8)
                        SpawnAt(entManager, "WallSolid", gridUid, x, y);
                }
            }

            for (var i = 0; i < 4; i++)
            {
                SpawnAt(entManager, RedactedSpawner, gridUid, -10, i - 2);
            }

            hostileUid = SpawnAt(entManager, SyndicateMob, gridUid, 0, 0);
            entManager.System<Content.Shared.Damage.Systems.SharedGodmodeSystem>().EnableGodmode(hostileUid);
            transformSystem.SetCoordinates(hostileUid, new EntityCoordinates(gridUid, new Vector2(40f, 40f)));
        });

        await Pair.RunTicksSync(240); // for the squad to form

        await Server.WaitPost(() =>
        {
            var enumerator = entManager.EntityQueryEnumerator<NpcSquadMemberComponent, TransformComponent>();
            while (enumerator.MoveNext(out var uid, out _, out var transformComponent))
            {
                if (uid != hostileUid && transformComponent.MapID == map.MapId)
                {
                    mobs.Add(uid);
                    entManager.System<Content.Shared.Damage.Systems.SharedGodmodeSystem>().EnableGodmode(uid);
                }
            }

            transformSystem.SetCoordinates(hostileUid, new EntityCoordinates(gridUid, new Vector2(6.5f, 0.5f)));
        });

        Assert.That(mobs, Has.Count.EqualTo(4), "the test needs the four of them");

        // It shoots for a while, as far as anyone can hear, then stops.
        for (var tick = 0; tick < 120; tick += 6)
        {
            await Pair.RunTicksSync(6);
            await Server.WaitPost(() =>
            {
                var coordinates = entManager.GetComponent<TransformComponent>(hostileUid).Coordinates;
                var ev = new GunShotEvent(hostileUid, new List<(EntityUid? Uid, IShootable Shootable)>(), coordinates, coordinates);
                entManager.EventBus.RaiseEvent(EventSource.Local, ref ev);
            });
        }

        // While each one's push order stands, how near it gets: the push is the closing in, and once it is over the fight
        //      may well back them off again.
        var pushed = false;
        var nearest = float.MaxValue;
        for (var tick = 0; tick < 300; tick += 5)
        {
            await Pair.RunTicksSync(5);
            await Server.WaitPost(() =>
            {
                var hostilePosition = transformSystem.GetMapCoordinates(hostileUid).Position;
                foreach (var mob in mobs)
                {
                    if (!tacticsSystem.TryGetOrder(mob, out var order) || order.Kind != NpcOrderKind.Push)
                        continue;

                    pushed = true;
                    nearest = MathF.Min(nearest, (transformSystem.GetMapCoordinates(mob).Position - hostilePosition).Length());
                }
            });
        }

        TestContext.Out.WriteLine($"nearest a pusher got: {nearest:0.0}");
        Assert.That(pushed, "a hostile gone quiet should be pushed on");
        Assert.That(nearest, Is.LessThan(4f), "and the pushers should close on it");
    }
}
