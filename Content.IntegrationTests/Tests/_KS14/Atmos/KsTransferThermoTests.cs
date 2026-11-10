#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._KS14.Atmos;
using Content.Server._KS14.Fluidmos;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Binary.Components;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.Piping.Binary.Components;
using Content.Shared._KS14.Atmos;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Fluidmos;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Prototypes;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Atmos;

/// <summary>
///     The thermodynamics of transfers: pumps heat what they compress by the adiabatic rise, with the heat capacity ratio
///         derived from atmos's own heat capacities; throttled gas cools or warms by Joule–Thomson from van der Waals
///         constants; and liquid crosses passive devices by the orifice law without ever reversing the pressure difference.
/// </summary>
[TestOf(typeof(KsTransferThermoSystem))]
public sealed class KsTransferThermoTests : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
# Boils far below anything here, and no gas of its own, so its vapor lives in the fluid.
- type: reagent
  id: KsTestUnlinkedVapor
  name: reagent-name-nothing
  desc: reagent-desc-nothing
  physicalDesc: reagent-physical-desc-nothing
  boilingPoint: 150
  fluidmos:
    molarMass: 60
";

    // Deliberately NOT static: it only exists in the test YAML above, and the YAML linter validates static ids.
    private readonly ProtoId<ReagentPrototype> _vaporReagent = "KsTestUnlinkedVapor";

    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";
    private static readonly KsFluidReagentKey WaterKey = new(WaterReagent, null);
    private static readonly ProtoId<GasPrototype> TritiumGas = "Tritium";

    private static readonly EntProtoId PassiveGatePrototype = "GasPassiveGate";
    private static readonly EntProtoId PressureRegulatorPrototype = "GasPressureRegulator";

    private const float NodeVolume = 200f;

    private static GasMixture MakeGas(float pressure, float temperature, params (Gas Gas, float Fraction)[] composition)
    {
        var mixture = new GasMixture(NodeVolume) { Temperature = temperature, FluidCapable = true };
        var moles = pressure * NodeVolume / (Atmospherics.R * temperature);
        foreach (var (gas, fraction) in composition)
        {
            mixture.AdjustMoles(gas, moles * fraction);
        }

        return mixture;
    }

    /// <summary>
    ///     A packet with <paramref name="vaporFraction"/> of its moles as unlinked vapor, and the rest nitrogen.
    /// </summary>
    private GasMixture MakeVapor(float pressure, float temperature, float vaporFraction)
    {
        var mixture = MakeGas(pressure, temperature, (Gas.Nitrogen, 1f - vaporFraction));
        mixture.Fluid = new KsFluidMixture();
        mixture.Fluid.SetAmount(new KsFluidReagentKey(_vaporReagent, null),
            new KsFluidAmount { Vapor = vaporFraction * pressure * NodeVolume / (Atmospherics.R * temperature) });
        return mixture;
    }

    /// <summary>
    ///     The compressed packet's temperature, by the formula: <c>T1 + f T1 ((P2/P1)^((γ-1)/γ) - 1) / η</c>.
    /// </summary>
    private static float ExpectedCompressed(IConfigurationManager configurationManager, float heatCapacityRatio, float temperature, float pressureIn, float pressureOut)
    {
        var efficiency = configurationManager.GetCVar(KsCCVars.AtmosPumpEfficiency);
        var fraction = configurationManager.GetCVar(KsCCVars.AtmosPumpCompressionHeatFraction);
        var exponent = (heatCapacityRatio - 1f) / heatCapacityRatio;
        return temperature + fraction * temperature * (MathF.Pow(pressureOut / pressureIn, exponent) - 1f) / efficiency;
    }

    [Test]
    public async Task EveryGasHasAPhysicalHeatCapacityRatioAndConsistentConstants()
    {
        var server = Pair.Server;
        var gasThermoSystem = server.System<KsGasThermoSystem>();
        var protoMan = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                for (var i = 0; i < Atmospherics.TotalNumberOfGases; i++)
                {
                    var gas = (Gas)i;
                    var gasPrototype = protoMan.Index<GasPrototype>(gas.ToString());
                    var pure = MakeGas(Atmospherics.OneAtmosphere, Atmospherics.T20C, (gas, 1f));

                    var expected = gasPrototype.KsHeatCapacityRatio
                                   ?? MathF.Min(1f + Atmospherics.R / gasPrototype.MolarHeatCapacity, KsGasThermoSystem.MaxHeatCapacityRatio);
                    var ratio = gasThermoSystem.MixtureHeatCapacityRatio(pure);

                    Assert.That(ratio, Is.EqualTo(expected).Within(1e-4f), $"{gas}'s heat capacity ratio isn't 1 + R / Cv.");
                    Assert.That(ratio, Is.GreaterThan(1f).And.LessThanOrEqualTo(KsGasThermoSystem.MaxHeatCapacityRatio + 1e-5f),
                        $"{gas}'s heat capacity ratio is unphysical.");

                    if (gasPrototype is not { KsVanDerWaalsA: { } a, KsVanDerWaalsB: { } b })
                        continue;

                    Assert.That(a, Is.GreaterThanOrEqualTo(0f), $"{gas} has a negative van der Waals a.");
                    Assert.That(b, Is.GreaterThan(0f), $"{gas} has a non-positive van der Waals b.");
                    Assert.That(KsThrottling.InversionTemperature(a, b), Is.GreaterThan(KsThrottling.CriticalTemperature(a, b)),
                        $"{gas} inverts below its critical temperature.");
                }

                Assert.That(protoMan.Index(TritiumGas).KsHeatCapacityRatio, Is.EqualTo(1.40f).Within(1e-4f),
                    "Tritium lost its heat capacity ratio override.");
                Assert.That(gasThermoSystem.MixtureHeatCapacityRatio(MakeGas(Atmospherics.OneAtmosphere, Atmospherics.T20C, (Gas.Tritium, 1f))),
                    Is.EqualTo(1.40f).Within(1e-4f), "Tritium's override isn't used.");
            }
        });
    }

    [Test]
    public async Task PumpingUphillHeatsByTheAdiabaticRise()
    {
        var server = Pair.Server;
        // The physics itself: every joule of the work in the gas. Shedding the rest is its own test.
        await OverrideCVar(Side.Server, KsCCVars.AtmosPumpCompressionHeatFraction, 1f);
        var atmosphereSystem = server.System<AtmosphereSystem>();
        var gasThermoSystem = server.System<KsGasThermoSystem>();
        var configurationManager = server.ResolveDependency<IConfigurationManager>();

        await server.WaitAssertion(() =>
        {
            var inlet = MakeGas(Atmospherics.OneAtmosphere, Atmospherics.T20C, (Gas.Nitrogen, 1f));
            var outlet = MakeGas(Atmospherics.MaxOutputPressure, Atmospherics.T20C, (Gas.Nitrogen, 1f));
            var inletPressure = inlet.Pressure;
            var outletPressure = outlet.Pressure;
            var inletMoles = inlet.TotalMoles;
            var outletMoles = outlet.TotalMoles;

            Assert.That(atmosphereSystem.PumpGasTo(inlet, outlet, Atmospherics.MaxOutputPressure + 100f), "The pump moved nothing.");

            var packetMoles = inletMoles - inlet.TotalMoles;
            var packetTemperature = ExpectedCompressed(configurationManager,
                gasThermoSystem.MixtureHeatCapacityRatio(outlet),
                Atmospherics.T20C,
                inletPressure,
                outletPressure);
            // One gas, so the merge is mole-weighted.
            var expected = (outletMoles * Atmospherics.T20C + packetMoles * packetTemperature) / (outletMoles + packetMoles);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(packetTemperature, Is.GreaterThan(Atmospherics.T20C + 100f), "The test's pressure ratio heats too little to tell.");
                Assert.That(outlet.Temperature, Is.EqualTo(expected).Within(0.05f), "The pumped packet wasn't heated by the adiabatic rise.");
                Assert.That(inlet.Temperature, Is.EqualTo(Atmospherics.T20C).Within(1e-3f), "Pumping changed what it left behind.");
            }
        });
    }

    [Test]
    public async Task CompressionHeatTheGasDoesNotKeepGoesToTheSurroundings()
    {
        var server = Pair.Server;
        var atmosphereSystem = server.System<AtmosphereSystem>();
        var transferThermoSystem = server.System<KsTransferThermoSystem>();
        const float fraction = 0.5f;
        await OverrideCVar(Side.Server, KsCCVars.AtmosPumpCompressionHeatFraction, fraction);

        await server.WaitAssertion(() =>
        {
            var shed = MakeGas(Atmospherics.OneAtmosphere, Atmospherics.T20C, (Gas.Nitrogen, 1f));
            var kept = MakeGas(Atmospherics.OneAtmosphere, Atmospherics.T20C, (Gas.Nitrogen, 1f));
            var room = MakeGas(Atmospherics.OneAtmosphere, Atmospherics.T20C, (Gas.Nitrogen, 0.79f), (Gas.Oxygen, 0.21f));

            var packetHeatCapacity = atmosphereSystem.GetHeatCapacity(shed, applyScaling: false);
            var roomHeatCapacity = atmosphereSystem.GetHeatCapacity(room, applyScaling: false);
            // The rise with every joule of the work in the gas.
            var efficiency = server.ResolveDependency<IConfigurationManager>().GetCVar(KsCCVars.AtmosPumpEfficiency);
            var ratio = server.System<KsGasThermoSystem>().MixtureHeatCapacityRatio(shed);
            var fullTemperature = KsThrottling.CompressedTemperature(ratio, efficiency, Atmospherics.T20C,
                Atmospherics.OneAtmosphere, Atmospherics.MaxOutputPressure);
            var work = packetHeatCapacity * (fullTemperature - Atmospherics.T20C);

            transferThermoSystem.Apply(shed, Atmospherics.OneAtmosphere, Atmospherics.MaxOutputPressure, doesWork: true, heatSink: room);
            transferThermoSystem.Apply(kept, Atmospherics.OneAtmosphere, Atmospherics.MaxOutputPressure, doesWork: true);

            var packetGain = packetHeatCapacity * (shed.Temperature - Atmospherics.T20C);
            var roomGain = roomHeatCapacity * (room.Temperature - Atmospherics.T20C);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(packetGain, Is.EqualTo(fraction * work).Within(work * 0.001f), "The packet didn't keep its share of the work.");
                Assert.That(packetGain + roomGain, Is.EqualTo(work).Within(work * 0.001f), "Compression heat was created or destroyed.");
                Assert.That(kept.Temperature, Is.EqualTo(fullTemperature).Within(0.05f),
                    "A pump with nowhere to shed its heat didn't keep it in the gas.");
            }
        });
    }

    /// <summary>
    ///     Vapor with no gas of its own lives in the fluid, not the gas moles, but compressing it is still work. A loop charged
    ///         with such a reagent compressed for free otherwise: no heat, and nothing for the pump to pay.
    /// </summary>
    [Test]
    public async Task CompressingUnlinkedVaporIsWork()
    {
        var server = Pair.Server;
        await OverrideCVar(Side.Server, KsCCVars.AtmosPumpCompressionHeatFraction, 1f);
        var configurationManager = server.ResolveDependency<IConfigurationManager>();

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<KsFluidmosPropertiesSystem>().TryGetProperties(_vaporReagent, out var properties));
            var molarHeatCapacity = properties!.VaporHeatCapacityPerUnit / properties.MolesPerUnit;
            var heatCapacityRatio = KsGasThermoSystem.HeatCapacityRatio(molarHeatCapacity);

            const float inletPressure = Atmospherics.OneAtmosphere;
            const float outletPressure = 3f * Atmospherics.OneAtmosphere;
            var packet = MakeVapor(inletPressure, Atmospherics.T20C, vaporFraction: 1f);
            Assert.That(packet.TotalMoles, Is.Zero, "The vapor is in the gas moles, so this proves nothing.");

            server.System<KsTransferThermoSystem>().Apply(packet, inletPressure, outletPressure, doesWork: true);

            var expected = ExpectedCompressed(configurationManager, heatCapacityRatio, Atmospherics.T20C, inletPressure, outletPressure);
            Assert.That(expected, Is.GreaterThan(Atmospherics.T20C + 1f));
            Assert.That(packet.Temperature, Is.EqualTo(expected).Within(0.1f), "Compressing vapor didn't heat it by the adiabatic rise.");
        });
    }

    /// <summary>
    ///     A packet of gas and vapor compresses as one mixture, between what each would do alone.
    /// </summary>
    [Test]
    public async Task VaporAndGasCompressAsOneMixture()
    {
        var server = Pair.Server;
        await OverrideCVar(Side.Server, KsCCVars.AtmosPumpCompressionHeatFraction, 1f);

        await server.WaitAssertion(() =>
        {
            float Rise(float vaporFraction)
            {
                var packet = MakeVapor(Atmospherics.OneAtmosphere, Atmospherics.T20C, vaporFraction);
                server.System<KsTransferThermoSystem>().Apply(packet, Atmospherics.OneAtmosphere, 3f * Atmospherics.OneAtmosphere, doesWork: true);
                return packet.Temperature - Atmospherics.T20C;
            }

            var gasRise = Rise(0f);
            var vaporRise = Rise(1f);
            Assert.That(vaporRise, Is.GreaterThan(0f), "Compressing vapor did nothing.");
            Assert.That(gasRise, Is.GreaterThan(vaporRise), "Nitrogen should rise further than a reagent with far more heat capacity a mole.");
            Assert.That(Rise(0.5f), Is.InRange(vaporRise, gasRise), "A mixture didn't land between its parts.");
        });
    }

    /// <summary>
    ///     Throttling a packet with no gas moles has no van der Waals constants to go on, so it stays as it was.
    /// </summary>
    [Test]
    public async Task ThrottlingUnlinkedVaporLeavesItAlone()
    {
        var server = Pair.Server;

        await server.WaitAssertion(() =>
        {
            var packet = MakeVapor(3f * Atmospherics.OneAtmosphere, Atmospherics.T20C, vaporFraction: 1f);
            server.System<KsTransferThermoSystem>().Apply(packet, 3f * Atmospherics.OneAtmosphere, Atmospherics.OneAtmosphere, doesWork: false);
            Assert.That(packet.Temperature, Is.EqualTo(Atmospherics.T20C).Within(0.001f), "Throttling ideal vapor changed its temperature.");
        });
    }

    /// <summary>
    ///     Vapor has no van der Waals constants, so in a mixture it only dilutes the gas's, as a gas without any does: a trace of
    ///         real gas mustn't chill a packet that is mostly vapor as though the whole of it were that gas.
    /// </summary>
    [Test]
    public async Task VaporDilutesTheConstantsOfWhatItThrottlesWith()
    {
        var server = Pair.Server;

        await server.WaitAssertion(() =>
        {
            const float inletPressure = 50f * Atmospherics.OneAtmosphere;
            const float outletPressure = Atmospherics.OneAtmosphere;
            const float gasFraction = 0.25f;

            var (pureA, pureB) = server.System<KsGasThermoSystem>().MixtureVanDerWaals(MakeGas(inletPressure, Atmospherics.T20C, (Gas.Nitrogen, 1f)));

            var packet = MakeVapor(inletPressure, Atmospherics.T20C, vaporFraction: 1f - gasFraction);
            var moles = packet.TotalMoles + packet.Fluid!.VaporMoles;
            var molarHeatCapacity = server.System<AtmosphereSystem>().KsGetCompressibleHeatCapacity(packet, applyScaling: false) / moles;

            var expected = KsThrottling.ThrottledTemperature(pureA * gasFraction * gasFraction, pureB * gasFraction, molarHeatCapacity,
                Atmospherics.T20C, inletPressure, outletPressure);
            var undiluted = KsThrottling.ThrottledTemperature(pureA, pureB, molarHeatCapacity, Atmospherics.T20C, inletPressure, outletPressure);
            Assert.That(MathF.Abs(expected - undiluted), Is.GreaterThan(0.1f), "Diluting changes nothing here, so this proves nothing.");

            server.System<KsTransferThermoSystem>().Apply(packet, inletPressure, outletPressure, doesWork: false);
            Assert.That(packet.Temperature, Is.EqualTo(expected).Within(0.01f), "Vapor didn't dilute the gas it throttled with.");
        });
    }

    [Test]
    public async Task ImmutableAirTakesTheShedHeatButVacuumDoesNot()
    {
        var server = Pair.Server;
        var transferThermoSystem = server.System<KsTransferThermoSystem>();
        const float fraction = 0.5f;
        await OverrideCVar(Side.Server, KsCCVars.AtmosPumpCompressionHeatFraction, fraction);

        await server.WaitAssertion(() =>
        {
            // A pump standing out under a planet's air: the bath takes the heat and stays as it was.
            var outdoors = MakeGas(Atmospherics.OneAtmosphere, Atmospherics.T20C, (Gas.Nitrogen, 0.79f), (Gas.Oxygen, 0.21f));
            outdoors.MarkImmutable();
            var intoAir = MakeGas(Atmospherics.OneAtmosphere, Atmospherics.T20C, (Gas.Nitrogen, 1f));
            var intoVacuum = MakeGas(Atmospherics.OneAtmosphere, Atmospherics.T20C, (Gas.Nitrogen, 1f));
            var alone = MakeGas(Atmospherics.OneAtmosphere, Atmospherics.T20C, (Gas.Nitrogen, 1f));

            transferThermoSystem.Apply(intoAir, Atmospherics.OneAtmosphere, Atmospherics.MaxOutputPressure, doesWork: true, heatSink: outdoors);
            transferThermoSystem.Apply(intoVacuum, Atmospherics.OneAtmosphere, Atmospherics.MaxOutputPressure, doesWork: true, heatSink: GasMixture.SpaceGas);
            transferThermoSystem.Apply(alone, Atmospherics.OneAtmosphere, Atmospherics.MaxOutputPressure, doesWork: true);

            var fullRise = alone.Temperature - Atmospherics.T20C;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(intoAir.Temperature - Atmospherics.T20C, Is.EqualTo(fraction * fullRise).Within(0.001f * fullRise),
                    "A pump in immutable air kept the heat it should have shed.");
                Assert.That(outdoors.Temperature, Is.EqualTo(Atmospherics.T20C), "Immutable air warmed.");
                Assert.That(intoVacuum.Temperature, Is.EqualTo(alone.Temperature).Within(0.05f), "Vacuum took a pump's heat.");
            }
        });
    }

    [Test]
    public async Task PlasmaStaysBelowIgnitionButAnOxygenPremixIgnites()
    {
        var server = Pair.Server;
        // At the physical fraction: the worst case, with every joule of the work in the gas.
        await OverrideCVar(Side.Server, KsCCVars.AtmosPumpCompressionHeatFraction, 1f);
        var atmosphereSystem = server.System<AtmosphereSystem>();
        var gasThermoSystem = server.System<KsGasThermoSystem>();
        var transferThermoSystem = server.System<KsTransferThermoSystem>();
        var configurationManager = server.ResolveDependency<IConfigurationManager>();

        await server.WaitAssertion(() =>
        {
            // Canister grade: a trace of oxygen, so a fire could run if the packet got hot enough.
            var fuel = MakeGas(Atmospherics.OneAtmosphere, Atmospherics.T20C, (Gas.Plasma, 0.99f), (Gas.Oxygen, 0.01f));
            var premix = MakeGas(Atmospherics.OneAtmosphere, Atmospherics.T20C, (Gas.Plasma, 0.5f), (Gas.Oxygen, 0.5f));

            var fuelExpected = ExpectedCompressed(configurationManager, gasThermoSystem.MixtureHeatCapacityRatio(fuel),
                Atmospherics.T20C, Atmospherics.OneAtmosphere, Atmospherics.MaxOutputPressure);
            var premixExpected = ExpectedCompressed(configurationManager, gasThermoSystem.MixtureHeatCapacityRatio(premix),
                Atmospherics.T20C, Atmospherics.OneAtmosphere, Atmospherics.MaxOutputPressure);

            transferThermoSystem.Apply(fuel, Atmospherics.OneAtmosphere, Atmospherics.MaxOutputPressure, doesWork: true);
            transferThermoSystem.Apply(premix, Atmospherics.OneAtmosphere, Atmospherics.MaxOutputPressure, doesWork: true);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(fuel.Temperature, Is.EqualTo(fuelExpected).Within(0.05f), "Plasma wasn't heated by the adiabatic rise.");
                Assert.That(premix.Temperature, Is.EqualTo(premixExpected).Within(0.05f), "The premix wasn't heated by the adiabatic rise.");
                Assert.That(fuelExpected, Is.LessThan(Atmospherics.PlasmaMinimumBurnTemperature),
                    "Plasma's heat capacity ratio heats it past ignition when a canister is filled.");
                Assert.That(premixExpected, Is.GreaterThan(Atmospherics.PlasmaMinimumBurnTemperature),
                    "A 50/50 premix no longer reaches ignition when compressed: compression ignition is gone.");
            }

            var fuelPlasma = fuel.GetMoles(Gas.Plasma);
            var premixPlasma = premix.GetMoles(Gas.Plasma);
            atmosphereSystem.React(fuel, holder: null);
            atmosphereSystem.React(premix, holder: null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(fuel.GetMoles(Gas.Plasma), Is.EqualTo(fuelPlasma), "Compressed plasma caught fire.");
                Assert.That(premix.GetMoles(Gas.Plasma), Is.LessThan(premixPlasma), "The compressed premix didn't ignite in the pipe.");
            }
        });
    }

    [Test]
    public async Task ThrottledGasCoolsByTheJouleThomsonIntegral()
    {
        var server = Pair.Server;
        var gasThermoSystem = server.System<KsGasThermoSystem>();
        var transferThermoSystem = server.System<KsTransferThermoSystem>();
        var protoMan = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            var nitrogen = protoMan.Index<GasPrototype>(nameof(Gas.Nitrogen));
            var a = nitrogen.KsVanDerWaalsA!.Value;
            var b = nitrogen.KsVanDerWaalsB!.Value;

            // The integral of μ dP, from the van der Waals constants alone, finely stepped.
            var expected = Atmospherics.T20C;
            const int steps = 10000;
            var stepPressure = (Atmospherics.OneAtmosphere - Atmospherics.MaxOutputPressure) / steps;
            for (var i = 0; i < steps; i++)
            {
                expected += (2f * a / (Atmospherics.R * expected) - b) / nitrogen.MolarHeatCapacity * stepPressure;
            }

            var bled = MakeGas(Atmospherics.MaxOutputPressure, Atmospherics.T20C, (Gas.Nitrogen, 1f));
            transferThermoSystem.Apply(bled, Atmospherics.MaxOutputPressure, Atmospherics.OneAtmosphere, doesWork: false);

            // Hotter than its inversion temperature, nitrogen warms instead; tritium already does at room temperature.
            var hotTemperature = KsThrottling.InversionTemperature(a, b) + 200f;
            var hot = MakeGas(Atmospherics.MaxOutputPressure, hotTemperature, (Gas.Nitrogen, 1f));
            transferThermoSystem.Apply(hot, Atmospherics.MaxOutputPressure, Atmospherics.OneAtmosphere, doesWork: false);

            var tritium = MakeGas(Atmospherics.MaxOutputPressure, Atmospherics.T20C, (Gas.Tritium, 1f));
            transferThermoSystem.Apply(tritium, Atmospherics.MaxOutputPressure, Atmospherics.OneAtmosphere, doesWork: false);

            // A passive device never compresses, whichever way the pressures lie.
            var uphill = MakeGas(Atmospherics.OneAtmosphere, Atmospherics.T20C, (Gas.Nitrogen, 1f));
            transferThermoSystem.Apply(uphill, Atmospherics.OneAtmosphere, Atmospherics.MaxOutputPressure, doesWork: false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Atmospherics.T20C - expected, Is.GreaterThan(5f), "The test's pressure drop cools too little to tell.");
                Assert.That(bled.Temperature, Is.EqualTo(expected).Within(0.2f), "Throttled nitrogen didn't cool by the Joule–Thomson integral.");
                Assert.That(hot.Temperature, Is.GreaterThan(hotTemperature), "Nitrogen above its inversion temperature didn't warm.");
                Assert.That(tritium.Temperature, Is.GreaterThan(Atmospherics.T20C), "Tritium at room temperature didn't warm on throttling.");
                Assert.That(uphill.Temperature, Is.EqualTo(Atmospherics.T20C), "A passive transfer did compression work.");
            }
        });
    }

    [Test]
    public async Task LiquidCrossesAGateByTheOrificeLaw()
    {
        var server = Pair.Server;
        var liquidFlowSystem = server.System<KsLiquidFlowSystem>();

        await server.WaitAssertion(() =>
        {
            const float conductance = KsFluidmos.DefaultLiquidConductance;
            const float deltaTime = 0.5f;
            const float waterUnits = 1000f;

            var inlet = MakeGas(3f * Atmospherics.OneAtmosphere, Atmospherics.T20C, (Gas.Nitrogen, 1f));
            inlet.Fluid = new KsFluidMixture();
            inlet.Fluid.AddLiquid(WaterKey, waterUnits);
            var outlet = MakeGas(Atmospherics.OneAtmosphere, Atmospherics.T20C, (Gas.Nitrogen, 1f));
            var pressureDifference = inlet.Pressure - outlet.Pressure;
            var outletGas = outlet.TotalMoles;

            var moved = liquidFlowSystem.Transfer(inlet, outlet, conductance, deltaTime);
            var expected = conductance * MathF.Sqrt(pressureDifference) * deltaTime;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(expected, Is.LessThan(waterUnits), "The test's inlet runs dry, so the law isn't what limits it.");
                Assert.That(moved, Is.EqualTo(expected).Within(expected * 0.01f), "Liquid didn't cross at conductance √ΔP.");
                Assert.That(outlet.Fluid?.LiquidUnits ?? 0f, Is.EqualTo(moved).Within(0.01f), "The moved liquid didn't arrive.");
                Assert.That(inlet.Fluid!.LiquidUnits, Is.EqualTo(waterUnits - moved).Within(0.01f), "The moved liquid didn't leave.");
                Assert.That(outlet.TotalMoles, Is.EqualTo(outletGas), "The liquid flow moved gas.");
                Assert.That(liquidFlowSystem.Transfer(outlet, inlet, conductance, deltaTime), Is.Zero, "Liquid flowed uphill.");
            }
        });
    }

    [Test]
    public async Task PassiveGateThrottlesItsGasAndPassesLiquidByTheOrificeLaw()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var nodeContainerSystem = server.System<NodeContainerSystem>();
        EntityUid gateUid = default;

        await server.WaitPost(() => gateUid = server.EntMan.SpawnEntity(PassiveGatePrototype, testMap.GridCoords));
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(nodeContainerSystem.TryGetNodes(gateUid, "inlet", "outlet", out PipeNode? inlet, out PipeNode? outlet));
            var gateComponent = server.EntMan.GetComponent<GasPassiveGateComponent>(gateUid);

            inlet!.Air.Clear();
            inlet.Air.Temperature = Atmospherics.T20C;
            inlet.Air.AdjustMoles(Gas.Nitrogen, Atmospherics.MaxOutputPressure * inlet.Air.Volume / (Atmospherics.R * Atmospherics.T20C));
            inlet.Air.Fluid = new KsFluidMixture();
            inlet.Air.Fluid.AddLiquid(WaterKey, 1000f);
            outlet!.Air.Clear();
            outlet.Air.Temperature = Atmospherics.T20C;
            outlet.Air.AdjustMoles(Gas.Nitrogen, Atmospherics.OneAtmosphere * outlet.Air.Volume / (Atmospherics.R * Atmospherics.T20C));

            const float deltaTime = 0.5f;
            var ev = new AtmosDeviceUpdateEvent(deltaTime, null, null);
            server.EntMan.EventBus.RaiseLocalEvent(gateUid, ref ev);

            // The gas equalised in one step (so the liquid saw the equalised pressures), then the liquid crossed at most
            //      conductance √ΔP.
            using (Assert.EnterMultipleScope())
            {
                Assert.That(outlet.Air.Temperature, Is.LessThan(Atmospherics.T20C - 1f), "The gate's gas didn't cool on throttling.");
                Assert.That(outlet.Air.Fluid?.LiquidUnits ?? 0f, Is.GreaterThan(0f), "No liquid crossed the gate.");
                Assert.That(outlet.Air.Fluid?.LiquidUnits ?? 0f,
                    Is.LessThan(gateComponent.KsLiquidConductance * MathF.Sqrt(Atmospherics.MaxOutputPressure) * deltaTime),
                    "Liquid rode along with the gas instead of crossing by the orifice law.");
            }
        });
    }

    /// <summary>
    ///     A regulator relieves a net packed with liquid, over its threshold with almost no gas in it, down to the
    ///         threshold and no further.
    /// </summary>
    [Test]
    public async Task RegulatorRelievesALiquidPackedNetDownToItsThreshold()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var nodeContainerSystem = server.System<NodeContainerSystem>();
        EntityUid regulatorUid = default;

        await server.WaitPost(() => regulatorUid = server.EntMan.SpawnEntity(PressureRegulatorPrototype, testMap.GridCoords));
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(nodeContainerSystem.TryGetNodes(regulatorUid, "inlet", "outlet", out PipeNode? inlet, out PipeNode? outlet));
            var regulator = server.EntMan.GetComponent<GasPressureRegulatorComponent>(regulatorUid);
            regulator.Threshold = Atmospherics.OneAtmosphere * 5f;

            inlet!.Air.Clear();
            inlet.Air.Temperature = Atmospherics.T20C;
            inlet.Air.AdjustMoles(Gas.Nitrogen, 0.01f);
            inlet.Air.Fluid = new KsFluidMixture();
            // Past the hydraulic floor: thousands of kPa, nearly all of it the liquid's.
            inlet.Air.Fluid.AddLiquid(WaterKey, 1.02f * inlet.Air.Volume / KsFluidmos.LitersPerUnit);
            outlet!.Air.Clear();
            outlet.Air.Temperature = Atmospherics.T20C;
            outlet.Air.AdjustMoles(Gas.Nitrogen, Atmospherics.OneAtmosphere * outlet.Air.Volume / (Atmospherics.R * Atmospherics.T20C));
            var pressureBefore = inlet.Air.Pressure;
            Assert.That(pressureBefore, Is.GreaterThan(regulator.Threshold * 2f), "The packed net isn't over the threshold, so this proves nothing.");

            for (var i = 0; i < 20; i++)
            {
                var ev = new AtmosDeviceUpdateEvent(0.5f, null, null);
                server.EntMan.EventBus.RaiseLocalEvent(regulatorUid, ref ev);
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(outlet.Air.Fluid?.LiquidUnits ?? 0f, Is.GreaterThan(0f), "The regulator didn't relieve the packed net of any liquid.");
                Assert.That(inlet.Air.Pressure, Is.LessThan(pressureBefore * 0.5f), "The regulator barely relieved the packed net.");
                Assert.That(inlet.Air.Pressure, Is.GreaterThanOrEqualTo(regulator.Threshold - 1f), "The regulator relieved past its threshold.");
            }
        });
    }

    [Test]
    public async Task OverfullLiquidNetsSettleWithoutFlipping()
    {
        var server = Pair.Server;
        var liquidFlowSystem = server.System<KsLiquidFlowSystem>();

        await server.WaitAssertion(() =>
        {
            // Both past the hydraulic floor, by different amounts, joined by a valve that lets liquid either way (as a
            //      pressure-controlled valve does), opened wide.
            var fullUnits = NodeVolume / KsFluidmos.LitersPerUnit;
            var high = new GasMixture(NodeVolume) { Temperature = Atmospherics.T20C, FluidCapable = true, Fluid = new KsFluidMixture() };
            high.Fluid.AddLiquid(WaterKey, 1.10f * fullUnits);
            var low = new GasMixture(NodeVolume) { Temperature = Atmospherics.T20C, FluidCapable = true, Fluid = new KsFluidMixture() };
            low.Fluid.AddLiquid(WaterKey, 1.02f * fullUnits);

            const float conductance = 100f * KsFluidmos.DefaultLiquidConductance;
            var initialDifference = high.Pressure - low.Pressure;
            Assert.That(initialDifference, Is.GreaterThan(0f));

            var flips = 0;
            var previousSign = MathF.Sign(initialDifference);
            for (var tick = 0; tick < 100; tick++)
            {
                if (high.Pressure >= low.Pressure)
                    liquidFlowSystem.Transfer(high, low, conductance, deltaTime: 0.5f);
                else
                    liquidFlowSystem.Transfer(low, high, conductance, deltaTime: 0.5f);

                var sign = MathF.Sign(high.Pressure - low.Pressure);
                if (sign != 0 && sign != previousSign)
                    flips++;
                if (sign != 0)
                    previousSign = sign;
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(float.IsFinite(high.Pressure) && float.IsFinite(low.Pressure), "An overfull net's pressure isn't finite.");
                Assert.That(flips, Is.Zero, "Liquid ping-ponged between the nets.");
                Assert.That(MathF.Abs(high.Pressure - low.Pressure), Is.LessThan(0.01f * initialDifference), "The nets never settled.");
            }
        });
    }
}
