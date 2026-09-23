#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.SaturnClouds;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._KS14.SaturnClouds;

[TestFixture]
[TestOf(typeof(SaturnCloudSystem))]
public sealed class SaturnCloudMooringTest : GameTest
{
    [Test]
    public async Task CloudMapGrantsInnateMooringOnlyToNonStationGrids()
    {
        var pair = Pair;
        var server = pair.Server;
        var ordinaryMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entityManager = server.ResolveDependency<IEntityManager>();
            var mapSystem = entityManager.System<SharedMapSystem>();
            var transformSystem = entityManager.System<SharedTransformSystem>();
            var ordinaryGrid = mapSystem.CreateGridEntity(ordinaryMap.MapId);

            mapSystem.CreateMap(out var cloudMapId);
            var cloudMapUid = mapSystem.GetMap(cloudMapId);
            entityManager.AddComponent<SaturnCloudMapComponent>(cloudMapUid);

            var protectedGrid = mapSystem.CreateGridEntity(cloudMapId);
            var stationGrid = mapSystem.CreateGridEntity(ordinaryMap.MapId);
            entityManager.AddComponent<SaturnMainStationGridComponent>(stationGrid.Owner);
            transformSystem.SetMapCoordinates(stationGrid.Owner, new MapCoordinates(default, cloudMapId));

            Assert.Multiple(() =>
            {
                Assert.That(entityManager.HasComponent<InnateMooringComponent>(ordinaryGrid.Owner), Is.False);
                Assert.That(entityManager.HasComponent<InnateMooringComponent>(protectedGrid.Owner), Is.True);
                Assert.That(entityManager.HasComponent<InnateMooringComponent>(stationGrid.Owner), Is.False);
            });
        });
    }
}
