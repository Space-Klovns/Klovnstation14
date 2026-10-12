using Content.IntegrationTests.Fixtures;
using Content.Server.Fluids.EntitySystems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._KS14.Chemistry;

/// <summary>
///     Two puddles spawned on one tile (a map file, or a direct spawn) must end up as one puddle holding both volumes.
/// </summary>
[TestFixture]
[TestOf(typeof(PuddleSystem))]
public sealed class PuddleSingleTilePerPuddleTest : GameTest
{
    private const string Reagent = "Water";

    [Test]
    public async Task TestDuplicatePuddlesMergeIntoOne()
    {
        var pair = Pair;
        var server = pair.Server;
        var entityManager = server.EntMan;
        var puddleSystem = entityManager.System<PuddleSystem>();
        var solutionContainerSystem = entityManager.System<SharedSolutionContainerSystem>();

        var testMap = await pair.CreateTestMap();

        EntityUid first = default;
        EntityUid second = default;

        await server.WaitPost(() =>
        {
            first = entityManager.SpawnEntity("Puddle", testMap.GridCoords);
            second = entityManager.SpawnEntity("Puddle", testMap.GridCoords);

            foreach (var (uid, amount) in new[] { (first, 10), (second, 15) })
            {
                var puddle = entityManager.GetComponent<PuddleComponent>(uid);
                Assert.That(solutionContainerSystem.ResolveSolution(uid, puddle.SolutionName, ref puddle.Solution),
                    "Could not resolve a puddle solution.");
                solutionContainerSystem.TryAddSolution(puddle.Solution!.Value, new Solution(Reagent, FixedPoint2.New(amount)));
            }
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(puddleSystem.TryGetCachedPuddle(testMap.Tile, out Entity<PuddleComponent> cached), Is.True,
                "The tile has no cached puddle.");

            var count = 0;
            var query = entityManager.EntityQueryEnumerator<PuddleComponent>();
            while (query.MoveNext(out _, out _))
                count++;

            Assert.That(count, Is.EqualTo(1), "Duplicate puddle was not deleted.");
            Assert.That(entityManager.EntityExists(first) && entityManager.EntityExists(second), Is.False,
                "Both original puddles still exist.");

            Assert.That(solutionContainerSystem.ResolveSolution(cached.Owner, cached.Comp.SolutionName,
                    ref cached.Comp.Solution, out var solution),
                "Could not resolve the surviving puddle solution.");
            Assert.That(solution!.Volume, Is.EqualTo(FixedPoint2.New(25)), "Volumes were not summed.");
        });
    }
}
