#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Fluidmos;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Fluidmos;

/// <summary>
///     The saturation-pressure curve every reagent shares: it passes through one atmosphere at the boiling point, lands
///         on real vapor pressures, and its steepness is a real per-mole entropy rather than anything derived from the
///         game-scale latent heat.
/// </summary>
[TestOf(typeof(KsPhaseMath))]
public sealed class KsPhaseMathTests : GameTest
{
    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";

    /// <summary>
    ///     Water's real saturation pressure at 293.15 K, in kPa. A physical reference, not a tuning value.
    /// </summary>
    private const float RealWaterSaturationPressure = 2.34f;

    /// <summary>
    ///     Real <c>ΔS_vap / R</c> values sit around 8–14. Outside this, the value is a unit mix-up (deriving it from the
    ///         game-scale latent heat gives about 0.17 or 3.1).
    /// </summary>
    private const float MinimumVaporizationEntropy = 5f;
    private const float MaximumVaporizationEntropy = 20f;

    [Test]
    public async Task CurvePassesThroughOneAtmosphereAtTheBoilingPoint()
    {
        var server = Pair.Server;
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var reagentPrototype in server.ProtoMan.EnumeratePrototypes<ReagentPrototype>())
                {
                    Assert.That(propertiesSystem.TryGetProperties(reagentPrototype.ID, out var properties),
                        $"{reagentPrototype.ID} has no Fluidmos properties.");

                    Assert.That(properties!.VaporizationEntropy,
                        Is.InRange(MinimumVaporizationEntropy, MaximumVaporizationEntropy),
                        $"{reagentPrototype.ID}'s vaporization entropy is not a real per-mole ΔS_vap / R.");

                    if (!properties.Volatile)
                        continue;

                    Assert.That(KsPhaseMath.SaturationPressure(properties.VaporizationEntropy, properties.BoilingPoint, properties.BoilingPoint),
                        Is.EqualTo(KsPhaseMath.ReferencePressure).Within(0.01f).Percent,
                        $"{reagentPrototype.ID}'s curve misses one atmosphere at its boiling point.");
                    Assert.That(KsPhaseMath.BoilingPointAt(properties.VaporizationEntropy, properties.BoilingPoint, KsPhaseMath.ReferencePressure),
                        Is.EqualTo(properties.BoilingPoint).Within(0.01f).Percent,
                        $"{reagentPrototype.ID}'s boiling point at one atmosphere is not its boiling point.");
                }
            }
        });
    }

    [Test]
    public async Task WaterHoldsRealHumidityAtRoomTemperature()
    {
        var server = Pair.Server;
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.That(propertiesSystem.TryGetProperties(WaterReagent, out var water));

            // Deriving the curve from the game-scale 540 J/u latent heat gives about 43 kPa here.
            var saturationPressure = KsPhaseMath.SaturationPressure(water!.VaporizationEntropy, water.BoilingPoint, 293.15f);
            Assert.That(saturationPressure, Is.EqualTo(RealWaterSaturationPressure).Within(25f).Percent,
                "Water's saturation pressure at room temperature is far from the real one.");
        });
    }
}
