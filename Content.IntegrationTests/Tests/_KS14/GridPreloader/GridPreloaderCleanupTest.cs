#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.GridPreloader;
using Content.Shared.GridPreloader.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.GridPreloader;

[TestFixture]
[TestOf(typeof(GridPreloaderSystem))]
public sealed class GridPreloaderCleanupTest : GameTest
{
    [Test]
    public async Task FinalGridHandoffDeletesPreloaderMapOnly()
    {
        var pair = Pair;
        var server = pair.Server;
        var preloaderMapId = MapId.Nullspace;
        var targetMapId = MapId.Nullspace;
        var handedOffGrids = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            var entityManager = server.ResolveDependency<IEntityManager>();
            var mapSystem = entityManager.System<SharedMapSystem>();
            var transformSystem = entityManager.System<SharedTransformSystem>();
            var preloaderSystem = entityManager.System<GridPreloaderSystem>();

            var targetMap = mapSystem.CreateMap(out targetMapId, runMapInit: false);
            preloaderSystem.EnsurePreloadedGridMap();
            var preloader = preloaderSystem.GetPreloaderEntity();
            Assert.That(preloader, Is.Not.Null);
            preloaderMapId = entityManager.GetComponent<MapComponent>(preloader!.Value.Owner).MapId;

            var gridsToTake = preloader.Value.Comp.PreloadedGrids
                .SelectMany(entry => Enumerable.Repeat(entry.Key, entry.Value.Count))
                .ToList();
            foreach (ProtoId<PreloadedGridPrototype> prototype in gridsToTake)
            {
                Assert.That(preloaderSystem.TryGetPreloadedGrid(prototype, out var handedOff), Is.True);
                handedOffGrids.Add(handedOff!.Value);
                transformSystem.SetParent(handedOff.Value, targetMap);
            }

            Assert.That(mapSystem.MapExists(preloaderMapId), Is.True,
                "the preloader map was deleted before the final grid could be moved");
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            var entityManager = server.ResolveDependency<IEntityManager>();
            var mapSystem = entityManager.System<SharedMapSystem>();

            Assert.Multiple(() =>
            {
                Assert.That(mapSystem.MapExists(preloaderMapId), Is.False);
                Assert.That(mapSystem.MapExists(targetMapId), Is.True);
                foreach (var grid in handedOffGrids)
                {
                    Assert.That(entityManager.EntityExists(grid), Is.True);
                    Assert.That(entityManager.GetComponent<TransformComponent>(grid).MapID, Is.EqualTo(targetMapId));
                }
            });
        });
    }
}
