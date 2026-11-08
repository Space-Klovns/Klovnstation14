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
}
