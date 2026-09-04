using Content.IntegrationTests.Fixtures;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._KS14.Atmos.ChemicalFire;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.ChemicalFire;

/// <summary>
///     Covers chemfires against the extinguisher path: a fire extinguisher sprays water, water carries an
///         <c>ExtinguishTileReaction</c>, that reaction gates on
///         <see cref="AtmosphereSystem.IsHotspotActive"/> and then calls
///         <see cref="AtmosphereSystem.HotspotExtinguish"/>, which raises a <c>TileExtinguishEvent</c> on
///         everything standing on the tile.
///     Chemfires answer both ends of that: they report their tile as burning even with nothing flammable in
///         the air, and they die when the tile is doused.
/// </summary>
[TestFixture]
[TestOf(typeof(SharedChemicalFireSystem))]
public sealed class ChemicalFireExtinguisherTest : GameTest
{
    private const string ChemicalFireProto = "ChemicalFire";
    private const string WaterReagent = "Water";

    private static readonly Vector2i FireTile = Vector2i.Zero;

    [Test]
    public async Task TestExtinguisherPutsOutChemicalFire()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.EntMan;
        var atmosphereSystem = entityManager.System<AtmosphereSystem>();
        var chemicalFireSystem = entityManager.System<SharedChemicalFireSystem>();
        var puddleSystem = entityManager.System<SharedPuddleSystem>();

        var testMap = await pair.CreateTestMap();
        var gridUid = testMap.Grid.Owner;

        Entity<ChemicalFireComponent>? fire = null;

        await server.WaitAssertion(() =>
        {
            // HotspotExtinguish only reaches the tile's entities through a grid atmosphere.
            entityManager.EnsureComponent<GridAtmosphereComponent>(gridUid);

            Assert.That(atmosphereSystem.IsHotspotActive(gridUid, FireTile), Is.False,
                "Test tile is somehow already on fire.");

            fire = chemicalFireSystem.SpawnChemicalFire(ChemicalFireProto, (gridUid, null), FireTile);
            Assert.That(fire, Is.Not.Null, "Chemfire failed to spawn.");
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            // Note there is nothing burnable in the air - the chemfire alone has to make this true, or the
            //     water's tile reaction bails out before it can extinguish anything.
            Assert.That(atmosphereSystem.IsHotspotActive(gridUid, FireTile),
                "Chemfire did not report its tile as burning.");

            // The call an extinguisher's water ends up making, via the reagent's tile reaction.
            var water = new Solution(WaterReagent, FixedPoint2.New(100));
            puddleSystem.DoTileReactions(testMap.Tile, water);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entityManager.EntityExists(fire!.Value.Owner), Is.False,
                    "Chemfire survived being sprayed with water.");

                Assert.That(atmosphereSystem.IsHotspotActive(gridUid, FireTile), Is.False,
                    "Tile still reports as burning after its only chemfire was extinguished.");
            });
        });
    }

    /// <summary>
    ///     The <see cref="ChemicalFireComponent.Extinguishable"/> opt-out, for chemfires that are meant to
    ///         burn through a dousing.
    /// </summary>
    [Test]
    public async Task TestInextinguishableChemicalFireSurvives()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.EntMan;
        var chemicalFireSystem = entityManager.System<SharedChemicalFireSystem>();
        var puddleSystem = entityManager.System<SharedPuddleSystem>();

        var testMap = await pair.CreateTestMap();
        var gridUid = testMap.Grid.Owner;

        Entity<ChemicalFireComponent>? fire = null;

        await server.WaitAssertion(() =>
        {
            entityManager.EnsureComponent<GridAtmosphereComponent>(gridUid);

            fire = chemicalFireSystem.SpawnChemicalFire(ChemicalFireProto, (gridUid, null), FireTile);
            Assert.That(fire, Is.Not.Null, "Chemfire failed to spawn.");

            fire!.Value.Comp.Extinguishable = false;
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var water = new Solution(WaterReagent, FixedPoint2.New(100));
            puddleSystem.DoTileReactions(testMap.Tile, water);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entityManager.EntityExists(fire!.Value.Owner),
                "Inextinguishable chemfire was put out anyway.");
        });
    }
}
