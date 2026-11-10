#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Chemistry;

/// <summary>
///     Foaming agent, fluorosurfactant and sorium sit in the pyrotechnic group but don't burn
///         (<c>Partials/nonflammable.yml</c>). The partial must take off only the fire they inherit.
/// </summary>
public sealed class KsNonflammableReagentsTest : GameTest
{
    private static readonly ProtoId<ReagentPrototype>[] NonflammableReagents = ["FoamingAgent", "Fluorosurfactant", "Sorium"];
    private static readonly ProtoId<ReactiveGroupPrototype> FlammableGroup = "Flammable";
    private static readonly ProtoId<ReagentPrototype> SoriumReagent = "Sorium";
    private static readonly ProtoId<ReagentPrototype> WeldingFuelReagent = "WeldingFuel";

    [Test]
    public async Task TestPyrotechnicsThatDontBurn()
    {
        var protoMan = Pair.Server.ProtoMan;
        await Pair.Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var reagentId in NonflammableReagents)
                {
                    var reagentPrototype = protoMan.Index(reagentId);
                    Assert.That(reagentPrototype.KsTileEffects, Is.Empty, $"{reagentId} still has tile effects, so it can still catch fire.");
                    Assert.That(reagentPrototype.ReactiveEffects is { } reactiveEffects && reactiveEffects.TryGetValue(FlammableGroup, out var flammable)
                            ? flammable.Effects
                            : [], Is.Empty,
                        $"{reagentId} still makes what it touches flammable.");
                    Assert.That(reagentPrototype.Group, Is.EqualTo("Pyrotechnic"), $"{reagentId} lost its group, and with it its guidebook listing.");
                }

                // The partial applied and kept the reagent's own data.
                Assert.That(protoMan.Index(SoriumReagent).Metabolisms, Is.Not.Null, "Sorium lost its metabolism.");

                // And the fire it removed is still on the pyrotechnics that do burn.
                var fuel = protoMan.Index(WeldingFuelReagent);
                Assert.That(fuel.KsTileEffects, Is.Not.Empty, "Welding fuel lost its fire.");
                Assert.That(fuel.ReactiveEffects?.ContainsKey(FlammableGroup) ?? false, "Welding fuel no longer makes what it touches flammable.");
            }
        });
    }
}
