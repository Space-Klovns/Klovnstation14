#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Fluidmos;
using Content.Shared._KS14.Fluidmos;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._KS14.Fluidmos;

/// <summary>
///     What is dissolved in a liquid lowers its freezing point: pure water freezes where water with ethanol in it does
///         not.
/// </summary>
[TestOf(typeof(KsFluidmosPhaseSystem))]
public sealed class KsFreezingPointDepressionTests : GameTest
{
    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";
    private static readonly ProtoId<ReagentPrototype> EthanolReagent = "Ethanol";
    private static readonly KsFluidReagentKey WaterKey = new(WaterReagent, null);
    private static readonly KsFluidReagentKey EthanolKey = new(EthanolReagent, null);

    /// <summary>
    ///     About 8 K under water's melting point: a tenth of its moles dissolved would hold it liquid down to 263 K.
    /// </summary>
    private const float ColdTemperature = 265f;

    private static GasMixture MakeColdPipe()
    {
        var mixture = new GasMixture(200f) { Temperature = ColdTemperature, FluidCapable = true, Fluid = new KsFluidMixture() };
        mixture.AdjustMoles(Gas.Nitrogen, Atmospherics.OneAtmosphere * mixture.Volume / (Atmospherics.R * ColdTemperature));
        return mixture;
    }

    /// <summary>
    ///     Steps the mixture, holding it at <see cref="ColdTemperature"/> as a cold room would, so freezing is never left
    ///         short of heat to shed.
    /// </summary>
    private static void StepCold(KsFluidmosPhaseSystem phaseSystem, IGameTiming gameTiming, GasMixture mixture, int steps)
    {
        for (var i = 0; i < steps; i++)
        {
            mixture.Temperature = ColdTemperature;
            mixture.Fluid!.LastPhaseTime = gameTiming.CurTime - TimeSpan.FromSeconds(0.5f);
            phaseSystem.UpdatePhases(mixture, holder: null);
        }
    }

    [Test]
    public async Task EthanolKeepsWaterLiquidBelowItsMeltingPoint()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            var pure = MakeColdPipe();
            pure.Fluid!.AddLiquid(WaterKey, 70f);

            // About 85 mol% water.
            var solution = MakeColdPipe();
            solution.Fluid!.AddLiquid(WaterKey, 70f);
            solution.Fluid.AddLiquid(EthanolKey, 30f);

            StepCold(phaseSystem, gameTiming, pure, 40);
            StepCold(phaseSystem, gameTiming, solution, 40);

            pure.Fluid!.TryGetAmount(WaterKey, out var pureWater);
            solution.Fluid!.TryGetAmount(WaterKey, out var solutionWater);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(pureWater.Solid, Is.GreaterThan(35f), "Pure water held below its melting point didn't freeze.");
                Assert.That(solutionWater.Solid, Is.Zero, "Water with ethanol dissolved in it froze above its depressed freezing point.");
            }
        });
    }
}
