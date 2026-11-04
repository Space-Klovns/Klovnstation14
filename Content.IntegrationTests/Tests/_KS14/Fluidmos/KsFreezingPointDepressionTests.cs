#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Fluidmos;
using Content.Shared._KS14.Fluidmos;
using Content.Shared.Atmos;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._KS14.Fluidmos;

/// <summary>
///     What is dissolved in a liquid lowers its freezing point: a pure solvent freezes where the same solvent with
///         something dissolved in it does not.
/// </summary>
[TestOf(typeof(KsFluidmosPhaseSystem))]
public sealed class KsFreezingPointDepressionTests : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: reagent
  id: KsTestFreezingSolvent
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-translucent
  boilingPoint: 5000
  meltingPoint: 273
  fluidmos:
    molarMass: 18

- type: reagent
  id: KsTestFreezingSolute
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-translucent
  boilingPoint: 5000
  meltingPoint: 100
  fluidmos:
    molarMass: 46
";

    // Instance fields: these ids exist only in the test prototypes above.
    private readonly KsFluidReagentKey _solventKey = new("KsTestFreezingSolvent", null);
    private readonly KsFluidReagentKey _soluteKey = new("KsTestFreezingSolute", null);

    /// <summary>
    ///     Under the solvent's melting point, and above where the mixture below depresses it to.
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
    public async Task SoluteKeepsSolventLiquidBelowItsMeltingPoint()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            var pure = MakeColdPipe();
            pure.Fluid!.AddLiquid(_solventKey, 70f);

            // About 85 mol% solvent.
            var solution = MakeColdPipe();
            solution.Fluid!.AddLiquid(_solventKey, 70f);
            solution.Fluid.AddLiquid(_soluteKey, 30f);

            StepCold(phaseSystem, gameTiming, pure, 40);
            StepCold(phaseSystem, gameTiming, solution, 40);

            pure.Fluid!.TryGetAmount(_solventKey, out var pureSolvent);
            solution.Fluid!.TryGetAmount(_solventKey, out var dissolvedSolvent);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(pureSolvent.Solid, Is.GreaterThan(0f), "A pure solvent held below its melting point didn't freeze.");
                Assert.That(dissolvedSolvent.Solid, Is.Zero, "A solvent with something dissolved in it froze above its depressed freezing point.");
            }
        });
    }
}
