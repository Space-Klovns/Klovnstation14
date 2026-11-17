#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared._KS14.Fluidmos;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Fluidmos;

/// <summary>
///     Radiators condense and boil what passes them, with their walls carrying the latent heat to their tile as far as
///         their latent heat transfer allows, and move gas out of a net full of liquid as readily as out of a dry one.
/// </summary>
[TestOf(typeof(HeatExchangerSystem))]
public sealed class KsRadiatorFluidTests : GameTest
{
    private static readonly ProtoId<ReagentPrototype> AmmoniaReagent = "Ammonia";
    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";
    private static readonly EntProtoId RadiatorPrototype = "HeatExchanger";

    private const float DeltaTime = 0.5f;

    private sealed record Radiator(EntityUid Uid, HeatExchangerComponent Component, GasMixture Inlet, GasMixture Outlet, GasMixture Tile);

    /// <summary>
    ///     A radiator on a tile of ordinary room air, with each of its nodes alone on its net.
    /// </summary>
    private async Task<Radiator> SpawnRadiator()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        EntityUid radiatorUid = default;
        await server.WaitPost(() =>
        {
            var air = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            air.SetMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            air.SetMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
            server.System<AtmosphereSystem>().SetMapAtmosphere(testMap.MapUid, space: false, air);
            server.EntMan.EnsureComponent<GridAtmosphereComponent>(testMap.Grid.Owner);
            radiatorUid = server.EntMan.SpawnEntity(RadiatorPrototype, testMap.GridCoords);
        });
        await server.WaitRunTicks(30);

        Radiator radiator = null!;
        await server.WaitPost(() =>
        {
            Assert.That(server.System<NodeContainerSystem>().TryGetNodes(radiatorUid, "inlet", "outlet", out PipeNode? inlet, out PipeNode? outlet));
            var tile = server.System<AtmosphereSystem>().GetContainingMixture(radiatorUid, ignoreExposed: true, excite: true);
            Assert.That(tile is { Immutable: false }, "The radiator's tile has no air of its own.");
            tile!.Clear();
            tile.Temperature = Atmospherics.T20C;
            tile.SetMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            tile.SetMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
            radiator = new Radiator(radiatorUid, server.EntMan.GetComponent<HeatExchangerComponent>(radiatorUid), inlet!.Air, outlet!.Air, tile);
        });

        return radiator;
    }

    private static void Fill(GasMixture mixture, Gas gas, float pressure, float temperature)
    {
        mixture.Clear();
        mixture.Fluid = null;
        mixture.Temperature = temperature;
        mixture.AdjustMoles(gas, pressure * mixture.Volume / (Atmospherics.R * temperature));
    }

    private static void Update(IEntityManager entityManager, EntityUid uid)
    {
        var ev = new AtmosDeviceUpdateEvent(DeltaTime, null, null);
        entityManager.EventBus.RaiseLocalEvent(uid, ref ev);
    }

    private static float Liquid(Radiator radiator)
    {
        return (radiator.Inlet.Fluid?.LiquidUnits ?? 0f) + (radiator.Outlet.Fluid?.LiquidUnits ?? 0f);
    }

    private static float HeatPerUnit(KsFluidReagentProperties properties, float temperature)
    {
        return properties.LatentHeat + temperature * (properties.VaporHeatCapacityPerUnit - properties.SpecificHeat);
    }

    [Test]
    public async Task RadiatorCondensesWithinItsLatentHeatTransfer()
    {
        var server = Pair.Server;
        var radiator = await SpawnRadiator();
        var atmosphereSystem = server.System<AtmosphereSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<KsFluidmosPropertiesSystem>().TryGetProperties(AmmoniaReagent, out var ammonia));

            // Ammonia vapor flowing at well over its saturation pressure at the room's temperature, as in a condenser,
            //      and far more of it than one update of the radiator's walls can condense.
            const float linePressure = 1500f;
            Fill(radiator.Inlet, Gas.Ammonia, linePressure, Atmospherics.T20C);
            Fill(radiator.Outlet, Gas.Ammonia, 0.5f * linePressure, Atmospherics.T20C);
            var tileHeatCapacity = atmosphereSystem.GetHeatCapacity(radiator.Tile, applyScaling: false);
            var outletVaporBefore = radiator.Outlet.GetMoles(Gas.Ammonia);

            Update(server.EntMan, radiator.Uid);

            var condensed = Liquid(radiator);
            var tileHeat = tileHeatCapacity * (radiator.Tile.Temperature - Atmospherics.T20C);
            // The walls sit at the vapor's dew point at the pressure it flowed at: its source's, once the packet left.
            var plateau = KsPhaseMath.BoilingPointAt(ammonia!.VaporizationEntropy, ammonia.BoilingPoint, radiator.Inlet.Pressure);
            var budget = radiator.Component.K * atmosphereSystem.HeatScale * radiator.Component.KsLatentHeatTransferScale *
                         (plateau - Atmospherics.T20C) * DeltaTime;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(condensed, Is.GreaterThan(0f), "Nothing condensed in the radiator.");
                Assert.That(tileHeat, Is.EqualTo(condensed * HeatPerUnit(ammonia, Atmospherics.T20C)).Within(0.02f * tileHeat),
                    "The latent heat didn't go to the radiator's tile.");
                Assert.That(tileHeat, Is.EqualTo(budget).Within(0.01f * budget),
                    "The radiator didn't carry off exactly the latent heat its walls can, with more vapor than that to condense.");
                Assert.That(radiator.Outlet.GetMoles(Gas.Ammonia), Is.GreaterThan(outletVaporBefore),
                    "Every bit of vapor condensed in one pass: the radiator's latent heat transfer isn't limited.");
                Assert.That(radiator.Outlet.Temperature, Is.EqualTo(Atmospherics.T20C).Within(0.5f),
                    "The latent heat stayed in the pipe instead of going to the tile.");
            }
        });
    }

    [Test]
    public async Task RadiatorBoilsWithHeatFromItsTile()
    {
        var server = Pair.Server;
        var radiator = await SpawnRadiator();
        var atmosphereSystem = server.System<AtmosphereSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<KsFluidmosPropertiesSystem>().TryGetProperties(AmmoniaReagent, out var ammonia));

            // Liquid ammonia carried along at a pressure it boils far below the room's temperature, as in an evaporator.
            const float linePressure = 150f;
            var boilingPoint = KsPhaseMath.BoilingPointAt(ammonia!.VaporizationEntropy, ammonia.BoilingPoint, linePressure);
            Fill(radiator.Inlet, Gas.Ammonia, linePressure, boilingPoint);
            radiator.Inlet.Fluid = new KsFluidMixture();
            radiator.Inlet.Fluid.AddLiquid(new KsFluidReagentKey(AmmoniaReagent, null), 2000f);
            Fill(radiator.Outlet, Gas.Ammonia, 0.5f * linePressure, boilingPoint);
            var liquidBefore = Liquid(radiator);
            var tileHeatCapacity = atmosphereSystem.GetHeatCapacity(radiator.Tile, applyScaling: false);

            Update(server.EntMan, radiator.Uid);

            var boiled = liquidBefore - Liquid(radiator);
            var tileHeat = tileHeatCapacity * (Atmospherics.T20C - radiator.Tile.Temperature);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(boiled, Is.GreaterThan(0f), "Nothing boiled in the radiator.");
                // The tile also warmed the packet's vapor and liquid to its own temperature on the way.
                Assert.That(tileHeat, Is.GreaterThanOrEqualTo(boiled * HeatPerUnit(ammonia, Atmospherics.T20C) * 0.98f),
                    "The heat of boiling didn't come from the radiator's tile.");
            }
        });
    }

    [Test]
    public async Task RadiatorMovesGasOutOfALiquidLadenNetAsFromADryOne()
    {
        var server = Pair.Server;
        var dry = await SpawnRadiator();
        var laden = await SpawnRadiator();

        await server.WaitAssertion(() =>
        {
            foreach (var radiator in new[] { dry, laden })
            {
                Fill(radiator.Inlet, Gas.Nitrogen, 3f * Atmospherics.OneAtmosphere, Atmospherics.T20C);
                Fill(radiator.Outlet, Gas.Nitrogen, Atmospherics.OneAtmosphere, Atmospherics.T20C);
            }

            // Enough liquid to outweigh the gas many times over in TransferMoles.
            laden.Inlet.Fluid = new KsFluidMixture();
            laden.Inlet.Fluid.AddLiquid(new KsFluidReagentKey(WaterReagent, null), 5000f);
            var dryBefore = dry.Outlet.GetMoles(Gas.Nitrogen);
            var ladenBefore = laden.Outlet.GetMoles(Gas.Nitrogen);

            Update(server.EntMan, dry.Uid);
            Update(server.EntMan, laden.Uid);

            var dryMoved = dry.Outlet.GetMoles(Gas.Nitrogen) - dryBefore;
            var ladenMoved = laden.Outlet.GetMoles(Gas.Nitrogen) - ladenBefore;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(dryMoved, Is.GreaterThan(0f), "The dry radiator moved no gas.");
                // The liquid takes up some of the net, so the gas it leaves is a little more compressed and moves a
                //      little more.
                Assert.That(ladenMoved, Is.GreaterThanOrEqualTo(0.95f * dryMoved), "Liquid in the net choked the radiator's gas flow.");
                Assert.That(laden.Outlet.Fluid?.LiquidUnits ?? 0f, Is.GreaterThan(0f), "The gas didn't sweep any liquid along.");
            }
        });
    }
}
