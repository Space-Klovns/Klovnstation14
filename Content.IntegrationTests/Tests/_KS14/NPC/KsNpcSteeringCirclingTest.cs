#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server.Atmos.EntitySystems;
using Content.Server.NPC.Systems;
using Content.Shared.Atmos;
using Content.Shared.NPC;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     NPCs walking together do not go round and round a node of their path.
/// </summary>
/// <remarks>
///     A free node is only reached from inside it. Walking abreast, NPCs push each other off their lines, and one that
///         misses its node by a little turns too slowly to get into it: it goes round it, about a tile out, for good. The
///         stuck check went by distance moved, which going round is plenty of, so it never noticed. A squad sent to back up
///         a squadmate round a corner went round in circles short of the corner for the whole fight.
/// </remarks>
public sealed class KsNpcSteeringCirclingTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string Walker = "KsSteeringCirclingTestWalker";

    /// <summary>
    ///     A real body to walk with, as how it turns is the point.
    /// </summary>
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: KsSteeringCirclingTestWalker
  components:
  - type: MovementSpeedModifier # as quick as the quickest operatives, the Redacted
    baseSprintSpeed: 5
    baseWalkSpeed: 3
";

    /// <summary>
    ///     Four abreast, walking to the same spot through a gap in a wall: each gets through in good time.
    /// </summary>
    [Test]
    public async Task TestWalkersAbreastDoNotCircle()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        var min = new Vector2i(-12, -8);
        var max = new Vector2i(12, 8);
        EntityUid gridUid = default;

        await server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, min, max).Owner;

            // Air to breathe, so that nothing but the walking slows them.
            var moles = new float[Atmospherics.AdjustedNumberOfGases];
            moles[(int) Gas.Oxygen] = 21.824779f;
            moles[(int) Gas.Nitrogen] = 82.10312f;
            entManager.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, space: false, new GasMixture(moles, Atmospherics.T20C));
        });

        await WaitForNavmesh(Pair, gridUid, min, max);

        // A wall across the middle, with one gap at y = -3.
        await server.WaitPost(() =>
        {
            for (var y = min.Y; y <= max.Y; y++)
            {
                if (y != -3)
                    SpawnAt(entManager, "WallSolid", gridUid, 0, y);
            }
        });

        // For the navmesh to take the wall in.
        await Pair.RunTicksSync(60);

        var destination = new EntityCoordinates(gridUid, new Vector2(8.5f, 2.5f));
        var walkers = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            var steeringSystem = entManager.System<NPCSteeringSystem>();

            foreach (var (x, y) in new[] { (-6, 3), (-6, 1), (-8, 3), (-8, 1) })
            {
                var walkerUid = SpawnAt(entManager, Walker, gridUid, x, y);
                entManager.EnsureComponent<ActiveNPCComponent>(walkerUid);
                steeringSystem.Register(walkerUid, destination).Range = 1.5f;
                walkers.Add(walkerUid);
            }
        });

        // About fifteen tiles each, through the gap: a few seconds. Going round a node is forever.
        var through = false;
        var positions = string.Empty;

        for (var i = 0; i < 60 && !through; i++)
        {
            await Pair.RunTicksSync(15);
            await server.WaitPost(() =>
            {
                through = true;
                positions = string.Empty;

                foreach (var walkerUid in walkers)
                {
                    var position = entManager.GetComponent<TransformComponent>(walkerUid).LocalPosition;
                    through &= position.X >= 3f;
                    positions += $" ({position.X:0.0}, {position.Y:0.0})";
                }
            });
        }

        Assert.That(through, $"each should have got through the gap and on, but they are at{positions}");
    }

    /// <summary>
    ///     One NPC, weightless, walking from the open to the outside corner of a walled-off room, which its path reaches by
    ///         going a long way north and then turning: it gets there. It accelerates so slowly that it went on north well
    ///         past the turn, and steering only at its next node, it then went round and round that node for good. Every new
    ///         path it asked for started with the same node. So did one of a squad shut out of the room, sent to hold that
    ///         corner.
    /// </summary>
    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public async Task TestWeightlessWalkerGetsRoundATurn(int startY)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        var min = new Vector2i(-12, -10);
        var max = new Vector2i(16, 12);
        EntityUid gridUid = default;

        await server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, min, max).Owner;

            var moles = new float[Atmospherics.AdjustedNumberOfGases];
            moles[(int) Gas.Oxygen] = 21.824779f;
            moles[(int) Gas.Nitrogen] = 82.10312f;
            entManager.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, space: false, new GasMixture(moles, Atmospherics.T20C));
        });

        await WaitForNavmesh(Pair, gridUid, min, max);

        // The room's wall, x 3 to 11 and y -5 to 5.
        await server.WaitPost(() =>
        {
            for (var x = 3; x <= 11; x++)
            {
                for (var y = -5; y <= 5; y++)
                {
                    if (x is 3 or 11 || y is -5 or 5)
                        SpawnAt(entManager, "WallSolid", gridUid, x, y);
                }
            }
        });

        await Pair.RunTicksSync(60);

        var destination = new EntityCoordinates(gridUid, new Vector2(2.5f, 5.5f));
        EntityUid walkerUid = default;

        await server.WaitPost(() =>
        {
            walkerUid = SpawnAt(entManager, Walker, gridUid, -5, startY);
            entManager.EnsureComponent<ActiveNPCComponent>(walkerUid);
            entManager.System<NPCSteeringSystem>().Register(walkerUid, destination).Range = 0.8f;
        });

        // Shoved on from behind, part way up, as by a squadmate walking into it with mob pushing on.
        var shoved = false;

        for (var i = 0; i < 120 && !shoved; i++)
        {
            await Pair.RunTicksSync(1);
            await server.WaitPost(() =>
            {
                if (entManager.GetComponent<TransformComponent>(walkerUid).LocalPosition.Y < 2.5f)
                    return;

                entManager.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>().SetLinearVelocity(walkerUid, new Vector2(0f, 3.5f));
                shoved = true;
            });
        }

        Assert.That(shoved, "the test needs it to have gone north, up the room's side");

        var arrived = false;
        var position = Vector2.Zero;

        // Under ten tiles, weightless: about five seconds, shove and all. Steering only at its node, it took seven to ten,
        //      going round in a loop or two first: or, as often in a squad, for good.
        for (var i = 0; i < 12 && !arrived; i++)
        {
            await Pair.RunTicksSync(15);
            await server.WaitPost(() =>
            {
                position = entManager.GetComponent<TransformComponent>(walkerUid).LocalPosition;
                arrived = (position - destination.Position).Length() <= 1f;
            });
        }

        Assert.That(arrived, $"it should have got to the corner, but it is at ({position.X:0.0}, {position.Y:0.0})");
    }
}
