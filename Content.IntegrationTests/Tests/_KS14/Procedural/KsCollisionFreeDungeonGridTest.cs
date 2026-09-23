#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Procedural;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestFixture]
[TestOf(typeof(KsCollisionFreeDungeonGridSystem))]
public sealed class KsCollisionFreeDungeonGridTest : GameTest
{
    [Test]
    public async Task PlacementSearchContinuesPastFormerAttemptLimit()
    {
        var pair = Pair;
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var found = false;
        var position = Vector2.Zero;

        await server.WaitPost(() =>
        {
            var entityManager = server.ResolveDependency<IEntityManager>();
            var mapSystem = entityManager.System<SharedMapSystem>();
            var transformSystem = entityManager.System<SharedTransformSystem>();
            var placementSystem = entityManager.System<KsCollisionFreeDungeonGridSystem>();

            entityManager.DeleteEntity(testMap.Grid);
            mapSystem.CreateMap(out var stagingMapId);
            var generated = mapSystem.CreateGridEntity(stagingMapId);
            mapSystem.SetTile(generated.Owner, generated.Comp, Vector2i.Zero, new Tile(1));

            const float placementStep = 4f;
            const int blockedPlacements = 40;
            for (var i = 0; i < blockedPlacements; i++)
            {
                var blocker = mapSystem.CreateGridEntity(testMap.MapId);
                mapSystem.SetTile(blocker.Owner, blocker.Comp, Vector2i.Zero, new Tile(1));
                transformSystem.SetLocalPosition(blocker.Owner, new Vector2(i * placementStep, 0f));
            }

            found = placementSystem.TryFindCollisionFreePosition(
                generated.Comp,
                testMap.MapId,
                generated.Comp.LocalAABB.Center,
                Vector2.UnitX,
                0f,
                placementStep,
                out position);

            mapSystem.DeleteMap(stagingMapId);
        });

        Assert.That(found, Is.True);
        Assert.That(position.X, Is.EqualTo(160f).Within(0.01f));
        Assert.That(position.Y, Is.EqualTo(0f).Within(0.01f));
    }
}
