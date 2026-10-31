#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Fluidmos;
using Content.Server._KS14.Fluidmos.Devices;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared._KS14.Fluidmos;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Fluidmos;

/// <summary>
///     The Fluidmos devices: the fluid port moves reagents between its tank and its pipe with their data, the phase
///         separator splits a pipe by phase, the expansion valve reads out its flash and steers itself towards its
///         superheat setpoint, and the UI entries sum what a mixture carries.
/// </summary>
[TestOf(typeof(KsFluidPortSystem))]
public sealed class KsFluidmosNewDeviceTests : GameTest
{
    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";
    private static readonly ProtoId<ReagentPrototype> EthanolReagent = "Ethanol";
    private static readonly ProtoId<ReagentPrototype> BloodReagent = "Blood";

    private static readonly EntProtoId FluidPortPrototype = "KsFluidPort";
    private static readonly EntProtoId PhaseSeparatorPrototype = "KsPhaseSeparator";
    private static readonly EntProtoId ExpansionValvePrototype = "KsExpansionValve";

    private static readonly KsFluidReagentKey WaterKey = new(WaterReagent, null);
    private static readonly KsFluidReagentKey EthanolKey = new(EthanolReagent, null);

    private static ReagentId Blood(string dna)
    {
        return new ReagentId(BloodReagent, new List<ReagentData> { new DnaData { DNA = dna } });
    }

    private static void Update(IEntityManager entityManager, EntityUid deviceUid, float deltaTime)
    {
        var ev = new AtmosDeviceUpdateEvent(deltaTime, null, null);
        entityManager.EventBus.RaiseLocalEvent(deviceUid, ref ev);
    }

    private static void SetGas(GasMixture mixture, float pressure, float temperature)
    {
        mixture.Clear();
        mixture.Temperature = temperature;
        mixture.AdjustMoles(Gas.Nitrogen, pressure * mixture.Volume / (Atmospherics.R * temperature));
    }

    private async Task<EntityUid> SpawnDevice(EntProtoId prototype)
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        EntityUid deviceUid = default;
        await server.WaitPost(() => deviceUid = server.EntMan.SpawnEntity(prototype, testMap.GridCoords));
        await server.WaitRunTicks(5);
        return deviceUid;
    }

    [Test]
    public async Task FluidPortRoundTripsReagentData()
    {
        var server = Pair.Server;
        var portUid = await SpawnDevice(FluidPortPrototype);
        var nodeContainerSystem = server.System<NodeContainerSystem>();
        var solutionContainerSystem = server.System<SharedSolutionContainerSystem>();
        var fluidPortSystem = server.System<KsFluidPortSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.That(nodeContainerSystem.TryGetNode(portUid, "pipe", out PipeNode? pipe));
            Assert.That(solutionContainerSystem.TryGetSolution(portUid, "fluidport", out var solutionEntity, out var solution));
            var port = server.EntMan.GetComponent<KsFluidPortComponent>(portUid);
            SetGas(pipe!.Air, Atmospherics.OneAtmosphere, Atmospherics.T20C);

            var units = port.Rate;
            solutionContainerSystem.TryAddReagent(solutionEntity!.Value, new ReagentQuantity(Blood("GATTACA"), FixedPoint2.New(units)), out _);

            // One second injects the port's rate.
            Update(server.EntMan, portUid, deltaTime: 1f);
            Assert.That(pipe.Air.Fluid?.TryGetAmount(KsFluidReagentKey.From(Blood("GATTACA")), out var injected) == true &&
                        injected.Liquid >= units - 0.01f,
                "The port didn't inject its tank, data and all, into the pipe.");
            Assert.That(solution!.Volume, Is.EqualTo(FixedPoint2.Zero), "The injected reagents stayed in the tank.");

            // Ice in the pipe is left behind; only liquid comes out.
            pipe.Air.Fluid!.SetAmount(WaterKey, new KsFluidAmount(5f, 0f, 0f));
            fluidPortSystem.SetMode((portUid, port), KsFluidPortMode.Extract);
            Update(server.EntMan, portUid, deltaTime: 1f);

            using (Assert.EnterMultipleScope())
            {
                Assert.That((float)solution.GetReagentQuantity(Blood("GATTACA")), Is.EqualTo(units).Within(0.02f),
                    "The extracted blood lost its DNA, or didn't come back.");
                Assert.That((float)solution.GetTotalPrototypeQuantity(WaterReagent), Is.Zero, "The port extracted ice.");
                Assert.That(pipe.Air.Fluid!.SolidUnits, Is.EqualTo(5f).Within(0.01f), "The pipe's ice left with the liquid.");
            }
        });
    }

    [Test]
    public async Task PhaseSeparatorSplitsByPhase()
    {
        var server = Pair.Server;
        var separatorUid = await SpawnDevice(PhaseSeparatorPrototype);
        var nodeContainerSystem = server.System<NodeContainerSystem>();
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.That(nodeContainerSystem.TryGetNodes(separatorUid, "inlet", "outlet", "filter",
                out PipeNode? inlet, out PipeNode? gasOutlet, out PipeNode? liquidOutlet));
            Assert.That(propertiesSystem.TryGetProperties(EthanolReagent, out var ethanol));
            var separator = server.EntMan.GetComponent<KsPhaseSeparatorComponent>(separatorUid);

            SetGas(inlet!.Air, 3f * Atmospherics.OneAtmosphere, Atmospherics.T20C);
            inlet.Air.Fluid = new KsFluidMixture();
            inlet.Air.Fluid.AddLiquid(WaterKey, 500f);
            inlet.Air.Fluid.SetAmount(EthanolKey, new KsFluidAmount(0f, 0f, 2f * ethanol!.MolesPerUnit));
            gasOutlet!.Air.Clear();
            liquidOutlet!.Air.Clear();

            const float deltaTime = 0.5f;
            Update(server.EntMan, separatorUid, deltaTime);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(gasOutlet.Air.TotalMoles, Is.GreaterThan(0f), "No gas left by the gas outlet.");
                Assert.That(gasOutlet.Air.Fluid?.VaporMoles ?? 0f, Is.GreaterThan(0f), "Vapor didn't leave with the gas.");
                Assert.That(gasOutlet.Air.Fluid?.LiquidUnits ?? 0f, Is.Zero, "Liquid left by the gas outlet.");
                Assert.That(liquidOutlet.Air.Fluid?.LiquidUnits ?? 0f, Is.EqualTo(separator.LiquidRate * deltaTime).Within(0.01f),
                    "The liquid outlet didn't get its rate of liquid.");
                Assert.That(liquidOutlet.Air.TotalMoles, Is.Zero, "Gas left by the liquid outlet.");
            }
        });
    }

    [Test]
    public async Task ExpansionValveReadsItsFlashAndSteersTowardsItsSetpoint()
    {
        var server = Pair.Server;
        var valveUid = await SpawnDevice(ExpansionValvePrototype);
        var nodeContainerSystem = server.System<NodeContainerSystem>();
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.That(nodeContainerSystem.TryGetNodes(valveUid, "inlet", "outlet", out PipeNode? inlet, out PipeNode? outlet));
            Assert.That(propertiesSystem.TryGetProperties(WaterReagent, out var water));
            var valve = server.EntMan.GetComponent<KsExpansionValveComponent>(valveUid);

            // Hot pressurised water into a low-pressure net: part of it flashes.
            const float inletTemperature = 360f;
            const float setpoint = 10f;
            SetGas(inlet!.Air, 3f * Atmospherics.OneAtmosphere, inletTemperature);
            inlet.Air.Fluid = new KsFluidMixture();
            inlet.Air.Fluid.AddLiquid(WaterKey, 1000f);
            var outletBoilingPoint = KsPhaseMath.BoilingPointAt(water!.VaporizationEntropy, water.BoilingPoint, setpoint);
            SetAutomatic(server.EntMan, valveUid, valve, setpoint);

            // An outlet above the setpoint boils too warm: automatic mode closes the valve.
            SetGas(outlet!.Air, 2f * setpoint, outletBoilingPoint);
            var openingBefore = valve.Opening;
            Update(server.EntMan, valveUid, deltaTime: 1f);
            var tooHigh = valve.Opening;

            // One below it is starved: it opens.
            const float starvedPressure = 0.5f * setpoint;
            SetGas(outlet.Air, starvedPressure, outletBoilingPoint);
            var starvedBoilingPoint = KsPhaseMath.BoilingPointAt(water.VaporizationEntropy, water.BoilingPoint, starvedPressure);
            var expectedFraction = Math.Clamp(water.SpecificHeat * (inletTemperature - starvedBoilingPoint) / water.LatentHeat, 0f, 1f);
            var outletMoles = outlet.Air.TotalMoles;
            Update(server.EntMan, valveUid, deltaTime: 1f);
            var vaporFraction = valve.VaporFraction;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(tooHigh, Is.LessThan(openingBefore), "An outlet above the setpoint didn't close the automatic valve.");
                Assert.That(valve.Opening, Is.GreaterThan(tooHigh), "An outlet below the setpoint didn't open the automatic valve.");
                Assert.That(valve.OutletBoilingPoint, Is.EqualTo(starvedBoilingPoint).Within(0.01f),
                    "The valve's outlet boiling point isn't the refrigerant's at the outlet pressure.");
                Assert.That(vaporFraction, Is.EqualTo(expectedFraction).Within(1e-3f), "The valve's flash readout isn't the energy balance.");
                Assert.That(vaporFraction, Is.GreaterThan(0f).And.LessThan(1f), "The test's temperatures don't make a partial flash.");
                Assert.That(outlet.Air.Fluid?.LiquidUnits ?? 0f, Is.GreaterThan(0f), "No liquid crossed the valve.");
                Assert.That(outlet.Air.TotalMoles, Is.EqualTo(outletMoles).Within(1e-4f),
                    "Gas crossed alongside liquid that filled the orifice.");
            }
        });
    }

    [Test]
    public async Task ExpansionValvePassesVaporWithNoLiquid()
    {
        var server = Pair.Server;
        var valveUid = await SpawnDevice(ExpansionValvePrototype);
        var nodeContainerSystem = server.System<NodeContainerSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.That(nodeContainerSystem.TryGetNodes(valveUid, "inlet", "outlet", out PipeNode? inlet, out PipeNode? outlet));

            // A condenser that hasn't delivered liquid yet: the loop must keep moving, or nothing ever condenses.
            SetGas(inlet!.Air, 8f * Atmospherics.OneAtmosphere, Atmospherics.T20C);
            SetGas(outlet!.Air, Atmospherics.OneAtmosphere, Atmospherics.T20C);
            var inletMoles = inlet.Air.TotalMoles;
            Update(server.EntMan, valveUid, deltaTime: 1f);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(inlet.Air.TotalMoles, Is.LessThan(inletMoles), "A valve with only vapor at its inlet passed nothing.");
                Assert.That(inlet.Air.Pressure, Is.GreaterThan(outlet.Air.Pressure), "The vapor overshot the pressures meeting.");
                Assert.That(outlet.Air.Temperature, Is.LessThan(Atmospherics.T20C), "Throttled nitrogen didn't cool.");
            }
        });
    }

    [Test]
    public async Task AutomaticValveHoldsItsPressure()
    {
        var server = Pair.Server;
        var valveUid = await SpawnDevice(ExpansionValvePrototype);
        var nodeContainerSystem = server.System<NodeContainerSystem>();
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var gameTiming = server.ResolveDependency<Robust.Shared.Timing.IGameTiming>();

        await server.WaitAssertion(() =>
        {
            Assert.That(nodeContainerSystem.TryGetNodes(valveUid, "inlet", "outlet", out PipeNode? inlet, out PipeNode? outlet));
            Assert.That(propertiesSystem.TryGetProperties(WaterReagent, out var water));
            var valve = server.EntMan.GetComponent<KsExpansionValveComponent>(valveUid);

            // The head of an evaporator, fed hot liquid, part of which flashes, and a compressor of fixed displacement
            //      drawing the vapor off: the more the valve opens, the more vapor and the higher the pressure. Started shut, so the
            //      valve has to find its opening.
            const float setpoint = 5f;
            const float inletTemperature = 360f;
            const float displacement = 1f;
            const float deltaTime = 0.5f;
            SetAutomatic(server.EntMan, valveUid, valve, setpoint);
            server.EntMan.EventBus.RaiseLocalEvent(valveUid, new Content.Shared._KS14.Fluidmos.Devices.KsExpansionValveSetOpeningMessage(0f));
            var boilingPoint = KsPhaseMath.BoilingPointAt(water!.VaporizationEntropy, water.BoilingPoint, setpoint);
            outlet!.Air.Clear();
            outlet.Air.Temperature = boilingPoint + 20f;
            outlet.Air.AdjustMoles(Gas.WaterVapor, 0.5f * setpoint * outlet.Air.Volume / (Atmospherics.R * outlet.Air.Temperature));

            var pressures = new List<float>();
            for (var step = 0; step < 1200; step++)
            {
                // The high side: warm liquid under pressure, as a condenser's receiver would hold it.
                SetGas(inlet!.Air, 3f * Atmospherics.OneAtmosphere, inletTemperature);
                inlet.Air.Fluid = new KsFluidMixture();
                inlet.Air.Fluid.AddLiquid(WaterKey, 1000f);

                Update(server.EntMan, valveUid, deltaTime);

                if (outlet.Air.Fluid != null)
                    outlet.Air.Fluid.LastPhaseTime = gameTiming.CurTime - TimeSpan.FromSeconds(deltaTime);
                phaseSystem.UpdatePhases(outlet.Air, holder: null);

                outlet.Air.AdjustMoles(Gas.WaterVapor, -displacement * deltaTime * outlet.Air.GetMoles(Gas.WaterVapor));

                // What doesn't flash goes on into the evaporator, rather than pooling here to flash in bursts.
                if (outlet.Air.Fluid is { LiquidUnits: > 0f })
                    outlet.Air.RemoveLiquidRatio(1f);
                pressures.Add(outlet.Air.Pressure);
            }

            // Settled over the second half: on the setpoint on average, and never far off it.
            var settled = pressures.GetRange(pressures.Count / 2, pressures.Count - pressures.Count / 2);
            var mean = 0f;
            var worst = 0f;
            foreach (var pressure in settled)
            {
                mean += pressure / settled.Count;
                worst = MathF.Max(worst, MathF.Abs(pressure - setpoint));
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(mean, Is.EqualTo(setpoint).Within(0.05f * setpoint), "The automatic valve didn't hold its pressure on average.");
                Assert.That(worst, Is.LessThan(0.25f * setpoint), "The automatic valve swung far off its setpoint.");
                Assert.That(valve.Opening, Is.InRange(0.02f, 0.98f), "The valve ended shut or wide open, so the test didn't exercise its control.");
            }
        });
    }

    private static void SetAutomatic(IEntityManager entityManager, EntityUid valveUid, KsExpansionValveComponent valve, float setpoint)
    {
        // Through the window's own messages, as a player would.
        entityManager.EventBus.RaiseLocalEvent(valveUid, new Content.Shared._KS14.Fluidmos.Devices.KsExpansionValveSetAutomaticMessage(true));
        entityManager.EventBus.RaiseLocalEvent(valveUid, new Content.Shared._KS14.Fluidmos.Devices.KsExpansionValveSetPressureMessage(setpoint));
        Assert.That(valve.Automatic, "The automatic message was ignored.");
        Assert.That(valve.PressureSetpoint, Is.EqualTo(setpoint), "The pressure message was ignored.");
    }

    [Test]
    public async Task FluidEntriesSumDataVariantsAndShowVaporInUnits()
    {
        var server = Pair.Server;
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.That(propertiesSystem.TryGetProperties(EthanolReagent, out var ethanol));
            var fluid = new KsFluidMixture();
            var blood = new Solution();
            blood.AddReagent(Blood("AAAA"), FixedPoint2.New(3));
            blood.AddReagent(Blood("CCCC"), FixedPoint2.New(4));
            fluid.AddSolution(blood);
            fluid.SetAmount(EthanolKey, new KsFluidAmount(0f, 0f, 2f * ethanol!.MolesPerUnit));

            var entries = KsFluidEntry.FromMixture(fluid, propertiesSystem);
            Assert.That(entries, Is.Not.Null);
            var bloodEntry = Array.Find(entries!, entry => entry.Reagent == BloodReagent);
            var ethanolEntry = Array.Find(entries!, entry => entry.Reagent == EthanolReagent);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entries!, Has.Length.EqualTo(2), "Data variants weren't summed into one entry per reagent.");
                Assert.That(bloodEntry.Liquid, Is.EqualTo(7f).Within(0.01f), "The blood variants didn't sum.");
                Assert.That(ethanolEntry.Vapor, Is.EqualTo(2f).Within(0.01f), "Vapor wasn't shown in units.");
                Assert.That(KsFluidEntry.FromMixture(new KsFluidMixture(), propertiesSystem), Is.Null, "An empty mixture listed entries.");
            }
        });
    }
}
