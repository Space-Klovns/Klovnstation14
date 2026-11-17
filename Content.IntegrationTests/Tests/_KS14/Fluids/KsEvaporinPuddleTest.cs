#nullable enable
using System;
using Content.IntegrationTests.Fixtures;
using Content.Server.Fluids.EntitySystems;
using Content.Shared._KS14.Fluids.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids;
using Content.Shared.Fluids.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Fluids;

/// <summary>
///     Evaporin evaporates a whole puddle, whatever is in it. A solution can hold one reagent several times over with
///         different data, as blood from two people does.
/// </summary>
[TestOf(typeof(SharedPuddleSystem))]
public sealed class KsEvaporinPuddleTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: reagent
  id: KsTestEvaporinReagent
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-translucent
  color: '#800000'
";

    // Deliberately NOT static: it only exists in the test YAML above, and the YAML linter validates static ids.
    private readonly ProtoId<ReagentPrototype> _reagent = "KsTestEvaporinReagent";

    private const int TicksPerSecond = 30;
    private const float FirstQuantity = 5f;
    private const float SecondQuantity = 10f;
    private const float EvaporationAmount = 3f;

    [Test]
    public async Task EvaporinEvaporatesEveryVariantOfAReagent()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();

        var firstReagent = new ReagentId(_reagent.Id, [new DnaData { DNA = "first" }]);
        var secondReagent = new ReagentId(_reagent.Id, [new DnaData { DNA = "second" }]);
        Entity<SolutionComponent> puddleSolutionEntity = default;

        await server.WaitPost(() =>
        {
            var spilled = new Solution();
            spilled.AddReagent(firstReagent, FixedPoint2.New(FirstQuantity));
            spilled.AddReagent(secondReagent, FixedPoint2.New(SecondQuantity));
            Assert.That(spilled.Contents, Has.Count.EqualTo(2), "The solution merged the variants, so this proves nothing.");

            var puddleSystem = server.System<PuddleSystem>();
            Assert.That(puddleSystem.TrySpillAt(testMap.Tile, spilled, out var puddleUid, sound: false, tileReact: false));

            var puddleComponent = server.EntMan.GetComponent<PuddleComponent>(puddleUid);
            Assert.That(server.System<SharedSolutionContainerSystem>()
                .ResolveSolution(puddleUid, puddleComponent.SolutionName, ref puddleComponent.Solution, out var puddleSolution));
            puddleSolutionEntity = puddleComponent.Solution!.Value;

            // Long enough to reach an evaporation update, as the reaction that adds this would keep it there.
            var evaporatingComponent = server.EntMan.EnsureComponent<AtmosEvaporatingPuddleComponent>(puddleUid);
            evaporatingComponent.EvaporationAmount = FixedPoint2.New(EvaporationAmount);
            evaporatingComponent.Lifetime = TimeSpan.FromSeconds(10);

            puddleSystem.UpdateEvaporation((puddleUid, puddleComponent), puddleSolution!);
        });

        await server.WaitRunTicks(TicksPerSecond * 2);

        await server.WaitAssertion(() =>
        {
            var solution = puddleSolutionEntity.Comp!.Solution;
            var first = (float)solution.GetReagentQuantity(firstReagent);
            var second = (float)solution.GetReagentQuantity(secondReagent);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(first, Is.LessThan(FirstQuantity), "Evaporin left the first variant alone.");
                Assert.That(second, Is.LessThan(SecondQuantity), "Evaporin left the second variant alone.");

                // Within what splitting the solution can round by, each update, at this many updates.
                Assert.That(second / first, Is.EqualTo(SecondQuantity / FirstQuantity).Within(0.05f), "Evaporin took more of one variant than the other.");
            }
        });
    }
}
