using Content.IntegrationTests.Fixtures;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Atmos;

/// <summary>
///     The air on a grid's tile that atmos does not simulate is the map's, on the server and, as far as it can know, the
///         client: a station on a planet has the planet's air outside it, not space.
/// </summary>
[TestFixture]
[TestOf(typeof(AtmosphereSystem))]
public sealed class KsTileAirTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    /// <summary>
    ///     Well off the test grid's one tile.
    /// </summary>
    private static readonly Vector2i Outside = new(40, 40);

    [Test]
    public async Task TestOutsideIsTheMapsAir()
    {
        var server = Pair.Server;
        var atmosphereSystem = server.EntMan.System<AtmosphereSystem>();
        var testMap = await Pair.CreateTestMap();
        var gridUid = testMap.Grid.Owner;

        await server.WaitAssertion(() =>
        {
            Assert.That(atmosphereSystem.IsGridTileSpace(gridUid, Outside), "a map with no atmosphere is space");
            Assert.That(atmosphereSystem.GetGridTileMixture(gridUid, Outside)!.Pressure, Is.Zero);

            var moles = new float[Atmospherics.AdjustedNumberOfGases];
            moles[(int) Gas.Oxygen] = 21.824779f;
            moles[(int) Gas.Nitrogen] = 82.10312f;
            atmosphereSystem.SetMapAtmosphere(testMap.MapUid, space: false, new GasMixture(moles, Atmospherics.T20C));

            Assert.That(atmosphereSystem.IsGridTileSpace(gridUid, Outside), Is.False, "a planet is not");
            Assert.That(atmosphereSystem.GetGridTileMixture(gridUid, Outside)!.Pressure, Is.GreaterThan(90f), "and has its air outside");
        });

        await Pair.RunTicksSync(10);

        await Client.WaitAssertion(() =>
        {
            var clientAtmosphereSystem = Client.EntMan.System<SharedAtmosphereSystem>();
            var clientMapUid = Client.EntMan.GetEntity(server.EntMan.GetNetEntity(testMap.MapUid));

            Assert.That(clientAtmosphereSystem.IsMapSpace(clientMapUid), Is.False, "the client should be told");
            Assert.That(clientAtmosphereSystem.GetMapMixture(clientMapUid).Pressure, Is.GreaterThan(90f));
            Assert.That(clientAtmosphereSystem.GetMapMixture(clientMapUid).Immutable);
        });

        // Space again: the client hears of that too.
        await server.WaitPost(() => atmosphereSystem.SetMapSpace(testMap.MapUid, true));
        await Pair.RunTicksSync(10);

        await Client.WaitAssertion(() =>
        {
            var clientMapUid = Client.EntMan.GetEntity(server.EntMan.GetNetEntity(testMap.MapUid));
            Assert.That(Client.EntMan.System<SharedAtmosphereSystem>().IsMapSpace(clientMapUid));
        });
    }
}
