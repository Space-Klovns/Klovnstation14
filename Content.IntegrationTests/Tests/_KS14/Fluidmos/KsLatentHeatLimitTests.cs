#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.Fluidmos;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Fluidmos;

/// <summary>
///     Latent heat has to agree with the vapor-pressure curve. A loop's compression cost follows the curve and its cooling
///         follows the latent heat, so a reagent with more of it than the curve allows would beat Carnot however fairly its
///         pump was paid.
/// </summary>
[TestOf(typeof(KsPhaseMath))]
public sealed class KsLatentHeatLimitTests : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: reagent
  id: KsTestLatentLight
  name: reagent-name-nothing
  desc: reagent-desc-nothing
  physicalDesc: reagent-physical-desc-nothing
  boilingPoint: 400
  fluidmos:
    molarMass: 20

- type: reagent
  id: KsTestLatentHeavy
  name: reagent-name-nothing
  desc: reagent-desc-nothing
  physicalDesc: reagent-physical-desc-nothing
  boilingPoint: 400
  fluidmos:
    molarMass: 1000

- type: reagent
  id: KsTestLatentExplicit
  name: reagent-name-nothing
  desc: reagent-desc-nothing
  physicalDesc: reagent-physical-desc-nothing
  boilingPoint: 400
  fluidmos:
    molarMass: 1000
    latentHeat: 77
";

    // Deliberately NOT static: these only exist in the test YAML above, and the YAML linter validates static ids.
    private readonly ProtoId<ReagentPrototype> _lightReagent = "KsTestLatentLight";
    private readonly ProtoId<ReagentPrototype> _heavyReagent = "KsTestLatentHeavy";
    private readonly ProtoId<ReagentPrototype> _explicitReagent = "KsTestLatentExplicit";

    private const string TestPrefix = "KsTest";

    /// <summary>
    ///     A reagent light enough for the game's scale to sit below the curve keeps it. A heavy one, which would sit above
    ///         it, is held to the curve. A value written down is left alone: <see cref="EveryReagentFitsItsCurve"/> checks those.
    /// </summary>
    [Test]
    public async Task DefaultLatentHeatStopsAtTheCurve()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var propertiesSystem = Pair.Server.System<KsFluidmosPropertiesSystem>();
            Assert.That(propertiesSystem.TryGetProperties(_lightReagent, out var light));
            Assert.That(propertiesSystem.TryGetProperties(_heavyReagent, out var heavy));
            Assert.That(propertiesSystem.TryGetProperties(_explicitReagent, out var written));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(light!.LatentHeat, Is.EqualTo(KsPhaseMath.DefaultLatentHeat(light.BoilingPoint, light.SpecificHeat)),
                    "A reagent under the curve lost latent heat.");
                Assert.That(heavy!.LatentHeat, Is.LessThan(KsPhaseMath.DefaultLatentHeat(heavy.BoilingPoint, heavy.SpecificHeat)),
                    "A heavy reagent kept more latent heat than its curve allows.");
                Assert.That(heavy.LatentHeat, Is.EqualTo(MoleHeatAtBoilingPoint(heavy) * heavy.MolesPerUnit).Within(0.01f),
                    "A capped reagent's latent heat isn't the one that fits its curve.");
                Assert.That(written!.LatentHeat, Is.EqualTo(77f), "A latent heat written in YAML was overridden.");
            }
        });
    }

    /// <summary>
    ///     Walks every volatile reagent, so that a new one can't be left above its curve. What a mole takes at the boiling
    ///         point is the latent heat plus the change in heat capacity, since atmos stores <c>E = C T</c>.
    /// </summary>
    [Test]
    public async Task EveryReagentFitsItsCurve()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var propertiesSystem = Pair.Server.System<KsFluidmosPropertiesSystem>();
            var offenders = new List<string>();

            foreach (var reagent in Pair.Server.ProtoMan.EnumeratePrototypes<ReagentPrototype>())
            {
                if (reagent.ID.StartsWith(TestPrefix) ||
                    !propertiesSystem.TryGetProperties(reagent.ID, out var properties) ||
                    !properties.Volatile)
                    continue;

                var heatChange = properties.VaporHeatCapacityPerUnit - properties.SpecificHeat;
                var moleHeat = (properties.LatentHeat + properties.BoilingPoint * heatChange) / properties.MolesPerUnit;
                var limit = properties.VaporizationEntropy * Atmospherics.R * properties.BoilingPoint;
                if (moleHeat <= 0f || moleHeat > limit * 1.001f)
                    offenders.Add($"{reagent.ID}: {moleHeat:0} J/mol against {limit:0}");
            }

            Assert.That(offenders, Is.Empty, "These reagents' latent heat doesn't fit their vapor-pressure curve.");
        });
    }

    private static float MoleHeatAtBoilingPoint(KsFluidReagentProperties properties)
        => properties.VaporizationEntropy * Atmospherics.R * properties.BoilingPoint;
}
