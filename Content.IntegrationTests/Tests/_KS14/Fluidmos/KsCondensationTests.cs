#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Fluidmos;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Reactions;
using Content.Server.Fluids.EntitySystems;
using Content.Shared._KS14.Atmos;
using Content.Shared._KS14.Fluidmos;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.Prototypes;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Fluidmos;

/// <summary>
///     Linked gases condense out of tile air into the tile's puddle past saturation, and not within the supersaturation
///         margin, and their reactions stop at each gas's critical temperature.
/// </summary>
[TestOf(typeof(KsCondensationReaction))]
public sealed class KsCondensationTests : GameTest
{
    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";

    private const float ColdTemperature = 283.15f;

    /// <summary>
    ///     A cold test map whose whole atmosphere holds <paramref name="saturation"/> times the water vapor that saturates
    ///         it, so the tile under test stays that humid rather than losing its vapor to its neighbours.
    /// </summary>
    private async Task<TestMapData> CreateHumidTestMap(float saturation)
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<KsFluidmosPropertiesSystem>().TryGetProperties(WaterReagent, out var water));
            var saturationPressure = KsPhaseMath.SaturationPressure(water!.VaporizationEntropy, water.BoilingPoint, ColdTemperature);

            var mixture = new GasMixture(Atmospherics.CellVolume) { Temperature = ColdTemperature };
            mixture.SetMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            mixture.SetMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
            mixture.SetMoles(Gas.WaterVapor, saturation * saturationPressure * mixture.Volume / (Atmospherics.R * ColdTemperature));

            server.EntMan.System<AtmosphereSystem>().SetMapAtmosphere(testMap.MapUid, space: false, mixture);
            server.EntMan.EnsureComponent<GridAtmosphereComponent>(testMap.Grid.Owner);
        });

        return testMap;
    }

    private int PuddlesWithWater(TestMapData testMap)
    {
        var server = Pair.Server;
        var count = 0;
        var query = server.EntMan.AllEntityQueryEnumerator<PuddleComponent, TransformComponent>();
        while (query.MoveNext(out _, out var puddleComponent, out var transformComponent))
        {
            if (transformComponent.GridUid == testMap.Grid.Owner &&
                puddleComponent.Solution is { } solution &&
                solution.Comp.Solution.GetTotalPrototypeQuantity(WaterReagent) > FixedPoint2.Zero)
                count++;
        }

        return count;
    }

    [Test]
    public async Task SupersaturatedSteamOnAColdTileLeavesOnePuddle()
    {
        var server = Pair.Server;
        var testMap = await CreateHumidTestMap(saturation: 5f);
        await server.WaitRunTicks(90);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<PuddleSystem>().TryGetCachedPuddle(testMap.Grid.Owner, testMap.Tile.GridIndices, out var puddle),
                "Supersaturated steam on a cold tile left no puddle.");
            Assert.That(PuddlesWithWater(testMap), Is.EqualTo(1), "Condensation made more than one puddle on the tile.");
        });
    }

    /// <summary>
    ///     Within the margin, nothing condenses; just past it, something does. Checked on the rate itself, since a single
    ///         reaction just past saturation condenses less than the 0.01u a puddle can hold either way.
    /// </summary>
    [Test]
    public async Task NothingCondensesWithinTheSupersaturationMargin()
    {
        var server = Pair.Server;

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<KsFluidmosPropertiesSystem>().TryGetProperties(WaterReagent, out var water));
            var saturationPressure = KsPhaseMath.SaturationPressure(water!.VaporizationEntropy, water.BoilingPoint, ColdTemperature);

            GasMixture Humid(float saturation)
            {
                var mixture = new GasMixture(Atmospherics.CellVolume) { Temperature = ColdTemperature };
                mixture.SetMoles(Gas.WaterVapor, saturation * saturationPressure * mixture.Volume / (Atmospherics.R * ColdTemperature));
                return mixture;
            }

            const float seconds = 1f;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(KsCondensationReaction.GetCondensingMoles(water, Humid(1f + KsFluidmos.SupersaturationMargin * 0.9f), Gas.WaterVapor, seconds),
                    Is.Zero, "Vapor within the supersaturation margin condensed.");
                Assert.That(KsCondensationReaction.GetCondensingMoles(water, Humid(1f + KsFluidmos.SupersaturationMargin * 2f), Gas.WaterVapor, seconds),
                    Is.GreaterThan(0f), "Vapor past the supersaturation margin did not condense.");
            }
        });
    }

    [Test]
    public async Task ReactionsStopAtEachLinkedGassCriticalTemperature()
    {
        var server = Pair.Server;
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                for (var gasIndex = 0; gasIndex < propertiesSystem.ByGas.Length; gasIndex++)
                {
                    if (propertiesSystem.ByGas[gasIndex] == null)
                        continue;

                    var gas = (Gas)gasIndex;
                    var reactionId = new ProtoId<GasReactionPrototype>($"KsCondensation{gas}");
                    Assert.That(server.ProtoMan.TryIndex(reactionId, out var reaction), $"Linked gas {gas} has no condensation reaction.");

                    var gasPrototype = server.ProtoMan.Index<GasPrototype>(gas.ToString());
                    Assert.That(gasPrototype.KsVanDerWaalsA, Is.Not.Null, $"Linked gas {gas} has no van der Waals constants.");
                    var criticalTemperature = KsThrottling.CriticalTemperature(gasPrototype.KsVanDerWaalsA!.Value, gasPrototype.KsVanDerWaalsB!.Value);

                    Assert.That(reaction!.MaximumTemperatureRequirement, Is.EqualTo(criticalTemperature).Within(0.5f),
                        $"{reactionId}'s maximum temperature is not {gas}'s critical temperature.");
                }
            }
        });
    }
}
