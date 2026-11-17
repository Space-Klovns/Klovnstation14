#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Examine;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.Examine;

/// <summary>
///     <see cref="ExamineSystemShared.InRangeUnOccluded(MapCoordinates, MapCoordinates, float, ExamineSystemShared.Ignored?)"/>
///         along a tile edge. The occluder tree it asks reports every occluder on such a line - behind where the line
///         starts and past where it ends included - so without throwing those out, anything lined up along a tile edge
///         was hidden by any wall anywhere in line with it.
/// </summary>
public sealed class KsExamineEdgeRayTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const float Range = 15f;

    [Test]
    public async Task TestEdgeLineIsOnlyBlockedByWallsOnIt()
    {
        var tileDefinitionManager = Server.ResolveDependency<ITileDefinitionManager>();
        var examineSystem = SEntMan.System<ExamineSystemShared>();
        var transformSystem = SEntMan.System<SharedTransformSystem>();
        var map = await Pair.CreateTestMap();
        var gridUid = EntityUid.Invalid;

        await Server.WaitPost(() =>
        {
            gridUid = MakeGrid(SEntMan, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-10, -10), new Vector2i(10, 10)).Owner;

            // On the line x = 0, the edge between the columns either side of it: one wall behind where the lines start,
            //      one past where the first ends.
            SpawnAt(SEntMan, "WallSolid", gridUid, 0, -5);
            SpawnAt(SEntMan, "WallSolid", gridUid, 0, 5);
        });

        await Pair.RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            MapCoordinates At(float x, float y) => transformSystem.ToMapCoordinates(new EntityCoordinates(gridUid, new Vector2(x, y)));

            Assert.Multiple(() =>
            {
                Assert.That(examineSystem.InRangeUnOccluded(At(0f, 0.5f), At(0f, 3.5f), Range, null), Is.True,
                    "nothing lies between the ends of a line along a tile edge, so it should be clear");
                Assert.That(examineSystem.InRangeUnOccluded(At(0f, 0.5f), At(0f, 8.5f), Range, null), Is.False,
                    "a line along a tile edge that runs past a wall's face should be blocked by it");
                Assert.That(examineSystem.InRangeUnOccluded(At(0.5f, 0.5f), At(0.5f, 8.5f), Range, null), Is.False,
                    "an ordinary line through a wall should still be blocked");
            });
        });
    }
}
