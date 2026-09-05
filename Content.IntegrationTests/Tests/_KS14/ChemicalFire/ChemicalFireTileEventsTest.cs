using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Atmos.ChemicalFire;
using Content.Shared.Atmos.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.ChemicalFire;

/// <summary>
///     Chemfires announce themselves to their tile with the same <c>TileFireEvent</c> /
///         <c>TileExtinguishEvent</c> pair a gas hotspot uses, so that standing in one reads the same as
///         standing in a gas fire.
///     Chemfires themselves are excluded from that, which this pins: a chemfire also subscribes
///         <c>TileExtinguishEvent</c> to be putoutable, so one expiring on a shared tile would otherwise take
///         its neighbours down with it.
/// </summary>
[TestFixture]
[TestOf(typeof(SharedChemicalFireSystem))]
public sealed class ChemicalFireTileEventsTest : GameTest
{
    private const string ChemicalFireProto = "ChemicalFire";

    /// <summary>Shares no connection key with <see cref="ChemicalFireProto"/>, so the two stack on one tile.</summary>
    private const string OtherChemicalFireProto = "ChemicalFireFrost";

    private static readonly Vector2i FireTile = Vector2i.Zero;

    [Test]
    public async Task TestExpiringChemicalFireDoesNotExtinguishItsTileMates()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.EntMan;
        var chemicalFireSystem = entityManager.System<SharedChemicalFireSystem>();

        var testMap = await pair.CreateTestMap();
        var gridUid = testMap.Grid.Owner;

        Entity<ChemicalFireComponent>? expiringFire = null;
        Entity<ChemicalFireComponent>? survivingFire = null;

        await server.WaitAssertion(() =>
        {
            entityManager.EnsureComponent<GridAtmosphereComponent>(gridUid);

            expiringFire = chemicalFireSystem.SpawnChemicalFire(ChemicalFireProto, (gridUid, null), FireTile);
            survivingFire = chemicalFireSystem.SpawnChemicalFire(OtherChemicalFireProto, (gridUid, null), FireTile);

            Assert.Multiple(() =>
            {
                Assert.That(expiringFire, Is.Not.Null, "Chemfire failed to spawn.");
                Assert.That(survivingFire, Is.Not.Null, "Second chemfire failed to stack on the tile.");
            });
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entityManager.EntityExists(survivingFire!.Value.Owner),
                "Second chemfire did not survive the first one being spawned.");

            // Force it to expire on the next update rather than waiting out its whole duration.
            expiringFire!.Value.Comp.EndTime = TimeSpan.Zero;
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entityManager.EntityExists(expiringFire!.Value.Owner), Is.False,
                    "Chemfire did not expire.");

                Assert.That(entityManager.EntityExists(survivingFire!.Value.Owner),
                    "Chemfire expiring put out the other chemfire sharing its tile.");
            });
        });
    }
}
