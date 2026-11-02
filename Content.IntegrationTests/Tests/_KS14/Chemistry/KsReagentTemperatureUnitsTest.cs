#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Chemistry;

/// <summary>
///     Every reagent's boiling and melting points are in Kelvin. Upstream stored them in Celsius, so a Celsius value
///         reintroduced by a merge (anything at or below absolute zero, or a melting point above the boiling point it
///         should sit under) fails here instead of quietly boiling a reagent 273 K early.
/// </summary>
[TestOf(typeof(ReagentPrototype))]
public sealed class KsReagentTemperatureUnitsTest : GameTest
{
    /// <summary>
    ///     "Never boils" (UnstableMutagen): the largest float, rounded.
    /// </summary>
    private const float NeverBoilsSentinel = 340282300f;

    private const float MaximumKelvin = 10000f;

    /// <summary>
    ///     Reagents whose melting point really is above their boiling point at one atmosphere, so they sublime.
    /// </summary>
    private static readonly HashSet<ProtoId<ReagentPrototype>> Sublimers =
    [
        "WeldingFuel", // acetylene sublimes at one atmosphere
        "CarbonDioxide", // dry ice: its triple point is above one atmosphere
        "SpaceGlue", // upstream's values, kept as they are
    ];

    [Test]
    public async Task BoilingAndMeltingPointsAreKelvin()
    {
        var server = Pair.Server;

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var reagentPrototype in server.ProtoMan.EnumeratePrototypes<ReagentPrototype>())
                {
                    if (reagentPrototype.BoilingPoint is { } boilingPoint && boilingPoint != NeverBoilsSentinel)
                    {
                        Assert.That(boilingPoint, Is.GreaterThan(0f).And.LessThan(MaximumKelvin),
                            $"{reagentPrototype.ID}'s boiling point {boilingPoint} is not a temperature in Kelvin.");
                    }

                    if (reagentPrototype.MeltingPoint is not { } meltingPoint)
                        continue;

                    Assert.That(meltingPoint, Is.GreaterThan(0f).And.LessThan(MaximumKelvin),
                        $"{reagentPrototype.ID}'s melting point {meltingPoint} is not a temperature in Kelvin.");

                    if (reagentPrototype.BoilingPoint is { } boilingPointAgain && !Sublimers.Contains(reagentPrototype.ID))
                    {
                        Assert.That(meltingPoint, Is.LessThan(boilingPointAgain),
                            $"{reagentPrototype.ID} melts above its boiling point; one of the two is likely in Celsius.");
                    }
                }
            }
        });
    }
}
