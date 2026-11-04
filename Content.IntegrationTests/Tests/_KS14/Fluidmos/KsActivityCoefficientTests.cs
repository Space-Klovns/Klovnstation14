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
///     Non-ideal mixing in pipes: Margules activity coefficients make ethanol and water an azeotrope that distillation
///         cannot pass, immiscible liquids each keep their own full vapor pressure, and a minimum-boiling mixture's
///         bubble point lies below both of its parts' boiling points.
/// </summary>
[TestOf(typeof(KsFluidmosPhaseSystem))]
public sealed class KsActivityCoefficientTests : GameTest
{
    // An oil that does not mix with water at all.
    [TestPrototypes]
    private const string Prototypes = @"
- type: ksFluidMixingFamily
  id: KsTestOilFamily
  immiscibleWith: [ KsAqueous ]

- type: reagent
  id: KsTestImmiscibleOil
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-oily
  boilingPoint: 450
  meltingPoint: 200
  fluidmos:
    molarMass: 100
    mixingFamily: KsTestOilFamily
";

    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";
    private static readonly ProtoId<ReagentPrototype> EthanolReagent = "Ethanol";
    private static readonly KsFluidReagentKey WaterKey = new(WaterReagent, null);
    private static readonly KsFluidReagentKey EthanolKey = new(EthanolReagent, null);

    // Instance field: this id exists only in the test prototypes above.
    private readonly ProtoId<ReagentPrototype> _oilReagent = "KsTestImmiscibleOil";

    private const float NodeVolume = 200f;

    private static GasMixture MakePipe(float temperature)
    {
        var mixture = new GasMixture(NodeVolume) { Temperature = temperature, FluidCapable = true, Fluid = new KsFluidMixture() };
        return mixture;
    }

    private static void AddHeadspace(GasMixture mixture)
    {
        mixture.AdjustMoles(Gas.Nitrogen, Atmospherics.OneAtmosphere * mixture.FreeVolume / (Atmospherics.R * mixture.Temperature));
    }

    /// <summary>
    ///     Steps until no vapor changes any more, so the liquid and its vapor are at equilibrium.
    /// </summary>
    private static void Settle(KsFluidmosPhaseSystem phaseSystem, IGameTiming gameTiming, GasMixture mixture)
    {
        var lastVapor = -1f;
        for (var steps = 0; steps < 5000; steps++)
        {
            mixture.Fluid!.LastPhaseTime = gameTiming.CurTime - TimeSpan.FromSeconds(0.5f);
            phaseSystem.UpdatePhases(mixture, holder: null);
            var vapor = mixture.GetMoles(Gas.WaterVapor) + mixture.Fluid!.VaporMoles;
            if (MathF.Abs(vapor - lastVapor) <= 1e-7f * vapor)
                return;

            lastVapor = vapor;
        }
    }

    /// <summary>
    ///     Ethanol's mole fraction in the vapor, and in the liquid.
    /// </summary>
    private static (float Vapor, float Liquid) EthanolFractions(GasMixture mixture, KsFluidReagentProperties water, KsFluidReagentProperties ethanol)
    {
        mixture.Fluid!.TryGetAmount(WaterKey, out var waterAmount);
        mixture.Fluid.TryGetAmount(EthanolKey, out var ethanolAmount);
        var waterVapor = mixture.GetMoles(Gas.WaterVapor);
        var waterLiquid = waterAmount.Liquid * water.MolesPerUnit;
        var ethanolLiquid = ethanolAmount.Liquid * ethanol.MolesPerUnit;
        return (ethanolAmount.Vapor / (ethanolAmount.Vapor + waterVapor), ethanolLiquid / (ethanolLiquid + waterLiquid));
    }

    [Test]
    public async Task EthanolWaterAzeotropeCannotBeDistilledPast()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            Assert.That(propertiesSystem.TryGetProperties(WaterReagent, out var water));
            Assert.That(propertiesSystem.TryGetProperties(EthanolReagent, out var ethanol));

            // Equal moles: well below the azeotrope (about 89 mol% ethanol).
            var dilute = MakePipe(340f);
            dilute.Fluid!.AddLiquid(WaterKey, 100f * 18f / 46f);
            dilute.Fluid.AddLiquid(EthanolKey, 100f);
            AddHeadspace(dilute);

            // 98 mol% ethanol: past it.
            var strong = MakePipe(340f);
            strong.Fluid!.AddLiquid(WaterKey, 2f * 18f / 46f);
            strong.Fluid.AddLiquid(EthanolKey, 98f);
            AddHeadspace(strong);

            Settle(phaseSystem, gameTiming, dilute);
            Settle(phaseSystem, gameTiming, strong);

            var (diluteVapor, diluteLiquid) = EthanolFractions(dilute, water!, ethanol!);
            var (strongVapor, strongLiquid) = EthanolFractions(strong, water!, ethanol!);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(diluteVapor, Is.GreaterThan(diluteLiquid), "Below the azeotrope, the vapor isn't richer in ethanol than the liquid.");
                Assert.That(strongVapor, Is.LessThan(strongLiquid),
                    "Past the azeotrope, the vapor is still richer in ethanol than the liquid, so distillation could pass it.");
            }
        });
    }

    [Test]
    public async Task ImmiscibleLiquidsEachKeepTheirFullVaporPressure()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            Assert.That(propertiesSystem.TryGetProperties(WaterReagent, out var water));
            Assert.That(propertiesSystem.TryGetProperties(_oilReagent, out var oil));
            Assert.That(propertiesSystem.Immiscible(water!, oil!), "The test oil and water aren't immiscible.");

            var mixture = MakePipe(350f);
            mixture.Fluid!.AddLiquid(WaterKey, 100f);
            mixture.Fluid.AddLiquid(new KsFluidReagentKey(_oilReagent, null), 100f);
            AddHeadspace(mixture);
            Settle(phaseSystem, gameTiming, mixture);

            var temperature = mixture.Temperature;
            var waterPressure = mixture.GetMoles(Gas.WaterVapor) * Atmospherics.R * temperature / mixture.FreeVolume;
            var waterSaturation = KsPhaseMath.SaturationPressure(water!.VaporizationEntropy, water.BoilingPoint, temperature);

            // Mixed ideally, water would be about 85% of the liquid's moles and hold 85% of its vapor pressure.
            mixture.Fluid.TryGetAmount(WaterKey, out var waterAmount);
            mixture.Fluid.TryGetAmount(new KsFluidReagentKey(_oilReagent, null), out var oilAmount);
            var idealFraction = waterAmount.Liquid * water.MolesPerUnit /
                                (waterAmount.Liquid * water.MolesPerUnit + oilAmount.Liquid * oil!.MolesPerUnit);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(idealFraction, Is.LessThan(0.9f), "The test's oil is too little to tell immiscible from ideal.");
                Assert.That(waterPressure, Is.EqualTo(waterSaturation).Within(0.02f * waterSaturation),
                    "Water under an immiscible oil doesn't hold its full saturation pressure.");
            }
        });
    }

    [Test]
    public void MinimumBoilingMixtureBubblesBelowBothBoilingPoints()
    {
        // Two parts that each boil at 400 K, strongly non-ideal (A = 1.5 there): half and half, its activity coefficients
        //      are e^(A/4) each.
        const float boilingPoint = 400f;
        const float entropy = KsFluidmos.DefaultVaporizationEntropy;
        var gamma = MathF.Exp(1.5f / 4f);
        ReadOnlySpan<float> activities = [0.5f * gamma, 0.5f * gamma];
        ReadOnlySpan<float> entropies = [entropy, entropy];
        ReadOnlySpan<float> boilingPoints = [boilingPoint, boilingPoint];

        var bubble = KsPhaseMath.BubbleTemperature(activities, entropies, boilingPoints, Atmospherics.OneAtmosphere, 420f);
        var bubblePressure = 2f * 0.5f * gamma * KsPhaseMath.SaturationPressure(entropy, boilingPoint, bubble);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(bubble, Is.LessThan(boilingPoint - 5f), "A minimum-boiling mixture doesn't boil below its parts.");
            Assert.That(bubblePressure, Is.EqualTo(Atmospherics.OneAtmosphere).Within(0.5f), "The bubble point isn't where the mixture's vapor pressure meets the pressure.");
        }
    }
}
