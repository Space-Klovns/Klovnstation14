#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server.NPC.Pathfinding;
using Content.Shared.NPC;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     The navmesh's <see cref="PathfindingBreadcrumbFlag.Access"/> flag marks doors that actually restrict who may
///         pass. Airlocks keep their access on the door electronics board rather than their own reader, so the
///         flag has to follow the board: NPCs price flagged doors as a last resort, and avoid public airlocks
///         entirely if public ones are flagged too.
/// </summary>
public sealed class KsDoorAccessBreadcrumbTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private static readonly Vector2i PublicAirlockTile = new(0, 0);
    private static readonly Vector2i LockedAirlockTile = new(4, 0);

    [Test]
    public async Task TestAccessFlagFollowsDoorElectronics()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var pathfindingSystem = entManager.System<PathfindingSystem>();
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default;

        await server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-3, -3), new Vector2i(7, 3)).Owner;
            SpawnAt(entManager, "Airlock", gridUid, PublicAirlockTile.X, PublicAirlockTile.Y);
            SpawnAt(entManager, "AirlockCommandLocked", gridUid, LockedAirlockTile.X, LockedAirlockTile.Y);
        });

        await Pair.RunTicksSync(150);

        await server.WaitAssertion(() =>
        {
            var publicFlags = GetFlags(pathfindingSystem, gridUid, PublicAirlockTile);
            var lockedFlags = GetFlags(pathfindingSystem, gridUid, LockedAirlockTile);

            Assert.Multiple(() =>
            {
                Assert.That(publicFlags & PathfindingBreadcrumbFlag.Door, Is.Not.EqualTo(PathfindingBreadcrumbFlag.None),
                    "the public airlock should be a door on the navmesh");
                Assert.That(publicFlags & PathfindingBreadcrumbFlag.Access, Is.EqualTo(PathfindingBreadcrumbFlag.None),
                    "a public airlock restricts nobody, so it must not be flagged as needing access");
                Assert.That(lockedFlags & PathfindingBreadcrumbFlag.Access, Is.Not.EqualTo(PathfindingBreadcrumbFlag.None),
                    "a command airlock restricts access, so it must be flagged");
            });
        });
    }

    private static PathfindingBreadcrumbFlag GetFlags(PathfindingSystem pathfindingSystem, EntityUid gridUid, Vector2i tile)
    {
        var poly = pathfindingSystem.GetPoly(new EntityCoordinates(gridUid, new Vector2(tile.X + 0.5f, tile.Y + 0.5f)));
        Assert.That(poly, Is.Not.Null, $"no navmesh poly at {tile}");
        return poly!.Data.Flags;
    }
}
