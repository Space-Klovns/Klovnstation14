#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.TileEffects;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.TileEffects;

/// <summary>
///     <see cref="KsTileEffectSystem.CanBeLitByFlame"/>, which decides what flame spreads across: only a reagent whose
///         chemfire a flame alone can start counts, at the hottest of the temperatures its conditions ask for.
/// </summary>
[TestOf(typeof(KsTileEffectSystem))]
public sealed class KsFlameLightingTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: reagent
  id: KsTestFlameLitFuel
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-translucent
  tileEffects:
  - !type:LogicAndTileEffect
    conditions:
    - !type:CheckTemperatureTileEffect
      minimum: 300
    - !type:CheckTemperatureTileEffect
      minimum: 500
    effects:
    - !type:ChemicalFireTileEffect

- type: reagent
  id: KsTestFlameGuardsNoFire
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-translucent
  tileEffects:
  - !type:LogicAndTileEffect
    conditions:
    - !type:CheckTemperatureTileEffect
      minimum: 300
    effects:
    - !type:CheckTemperatureTileEffect
      minimum: 0

- type: reagent
  id: KsTestOnlyHeatLightsIt
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-translucent
  tileEffects:
  - !type:LogicAndTileEffect
    conditions:
    - !type:CheckTemperatureTileEffect
      minimum: 300
      litByFlame: false
    effects:
    - !type:ChemicalFireTileEffect
";

    // Not static: these only exist in the test YAML above, and the YAML linter validates static ids.
    private readonly ProtoId<ReagentPrototype> _flameLitFuel = "KsTestFlameLitFuel";
    private readonly ProtoId<ReagentPrototype> _flameGuardsNoFire = "KsTestFlameGuardsNoFire";
    private readonly ProtoId<ReagentPrototype> _onlyHeatLightsIt = "KsTestOnlyHeatLightsIt";

    [Test]
    public async Task OnlyAFireAFlameCanStartCounts()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var tileEffectSystem = SEntMan.System<KsTileEffectSystem>();
            bool LitAt(ProtoId<ReagentPrototype> reagent, float flameTemperature)
                => tileEffectSystem.CanBeLitByFlame(new Solution(reagent.Id, FixedPoint2.New(10)), flameTemperature);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(LitAt(_flameLitFuel, 600f), Is.True, "the control: a flame hot enough for every condition lights it");
                Assert.That(LitAt(_flameLitFuel, 400f), Is.False, "a flame has to meet the hottest condition, not the coolest");
                Assert.That(LitAt(_flameGuardsNoFire, 1000f), Is.False, "a temperature check guarding something other than fire lights nothing");
                Assert.That(LitAt(_onlyHeatLightsIt, 1000f), Is.False, "a check a flame doesn't count toward can't be met by one");
            }
        });
    }
}
