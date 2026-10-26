#nullable enable
using System;
using System.Linq;
using Content.Shared._KS14.Procedural;
using Content.Shared.Atmos;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenGasTopologyTests
{
    private static readonly Vector2i[] Strip = [new(-1, 0), new(0, 0), new(1, 0)];
    private static KsProcgenGasPlacement Placement(int turns = 0, KsProcgenPlacementLayer layer = KsProcgenPlacementLayer.Floor) =>
        new("Blocker", new(0, 0), turns, layer);

    [TestCase(0, 1)] [TestCase(1, 2)] [TestCase(2, 1)] [TestCase(3, 2)]
    public void DirectionalBlockersRotateAndEitherSideOfAnEdgeCanBlockIt(int turns, int domains)
    {
        var context = Context(AtmosDirection.South, noAir: false);
        var result = context.PlanTopology(Strip, [Placement(turns: turns)]);
        Assert.That(result.Status, Is.EqualTo(KsProcgenGasBindingStatus.Bound), message: result.Issue?.Code);
        Assert.That(result.Domains.Count, Is.EqualTo(domains));
        Assert.That(result.Domains.Sum(domain => domain.Cells.Count), Is.EqualTo(3));
        if (turns == 1)
            Assert.That(result.Domains.Single(domain => domain.Cells.Contains(new(1, 0))).Cells, Is.EquivalentTo(new[] { new Vector2i(0, 0), new Vector2i(1, 0) }));
        if (turns == 3)
            Assert.That(result.Domains.Single(domain => domain.Cells.Contains(new(-1, 0))).Cells, Is.EquivalentTo(new[] { new Vector2i(-1, 0), new Vector2i(0, 0) }));
    }

    [TestCase(KsProcgenPlacementLayer.Floor, 2)]
    [TestCase(KsProcgenPlacementLayer.Surface, 1)]
    [TestCase(KsProcgenPlacementLayer.Container, 1)]
    public void OnlyAnchoredFloorMembersCanContributeGasBarriers(KsProcgenPlacementLayer layer, int domains)
    {
        var result = Context(AtmosDirection.All).PlanTopology(Strip, [Placement(layer: layer)]);
        Assert.That(result.Domains.Count, Is.EqualTo(domains));
        Assert.That(result.Domains.Sum(domain => domain.Cells.Count), Is.EqualTo(layer == KsProcgenPlacementLayer.Floor ? 2 : 3));
        var unanchored = Context(AtmosDirection.All, anchored: false).PlanTopology(Strip, [Placement(layer: layer)]);
        Assert.That(unanchored.Domains.Count, Is.EqualTo(1));
        Assert.That(unanchored.Domains.Single().Cells.Count, Is.EqualTo(3));
    }

    [Test]
    public void AnchoredWallRootRetainsItsNativeGasBarrierContribution()
    {
        var result = Context(AtmosDirection.All).PlanTopology(Strip, [Placement(layer: KsProcgenPlacementLayer.Wall)]);
        Assert.That(result.Status, Is.EqualTo(KsProcgenGasBindingStatus.Bound), result.Issue?.Code);
        Assert.That(result.Domains.Count, Is.EqualTo(2));
        Assert.That(result.Domains.Sum(domain => domain.Cells.Count), Is.EqualTo(2));
    }

    [TestCase(false)] [TestCase(true)]
    public void BlockersAggregateButNoAirRequiresAllDirectionsToBeBlocked(bool zeroDirections)
    {
        Assert.That(KsProcgenGasPlanningContext.TryCreate([
            new("Blocker", true, zeroDirections ? AtmosDirection.Invalid : AtmosDirection.North | AtmosDirection.South, true, false),
            new("Other", true, zeroDirections ? AtmosDirection.All : AtmosDirection.East | AtmosDirection.West, false, false)], "Captured",
            out var context, out _), Is.True);
        var first = context!.PlanTopology(Strip, [Placement()]);
        Assert.That(first.Domains.Single().Cells.Count, Is.EqualTo(3));
        var both = context.PlanTopology(Strip, [Placement(), new("Other", new(0, 0), 0, KsProcgenPlacementLayer.Floor)]);
        Assert.That(both.Domains.SelectMany(domain => domain.Cells), Is.EquivalentTo(new[] { Strip[0], Strip[2] }));
        var withVolume = Context(AtmosDirection.All, noAir: false).PlanTopology(Strip, [Placement()]);
        Assert.That(withVolume.Domains.Count, Is.EqualTo(3));
        Assert.That(withVolume.Domains.SelectMany(domain => domain.Cells), Is.EquivalentTo(Strip));
    }

    [Test]
    public void DiagonalCellsRemainSeparateAndDomainIdsAndContextFactsAreCanonical()
    {
        var facts = new[] { new KsProcgenGasPrototypeFact("Blocker", true, AtmosDirection.All, true, false) };
        Assert.That(KsProcgenGasPlanningContext.TryCreate(facts, "Captured", out var context, out _), Is.True);
        var hash = context!.DeclarationHash; facts[0] = facts[0] with { Anchored = false };
        Assert.That(context.DeclarationHash, Is.EqualTo(hash));
        Assert.That(KsProcgenGasPlanningContext.TryCreate(facts, "Captured", out var changed, out _), Is.True);
        Assert.That(changed!.DeclarationHash, Is.Not.EqualTo(hash));
        var result = context.PlanTopology([new(2, 2), new(1, 1)], []);
        Assert.That(result.Domains.Select(domain => domain.Id), Is.EqualTo(new[] { "Gas/1/1", "Gas/2/2" }));
        Assert.That(context.PlanTopology(Strip, [Placement()]).Domains.Sum(domain => domain.Cells.Count), Is.EqualTo(2));
        Assert.That(changed.PlanTopology(Strip, [Placement()]).Domains.Sum(domain => domain.Cells.Count), Is.EqualTo(3));
    }

    [Test]
    public void ExactWorkAllowanceSucceedsAndOneLessReturnsNoPartialTopology()
    {
        var context = Context(AtmosDirection.South); var result = context.PlanTopology(Strip, [Placement(turns: 1)]);
        var exact = context.PlanTopology(Strip, [Placement(turns: 1)], maximumWork: result.Work);
        Assert.That(exact.Status, Is.EqualTo(KsProcgenGasBindingStatus.Bound));
        foreach (var allowance in new[] { 0, result.Work - 1 })
        {
            var shortResult = context.PlanTopology(Strip, [Placement(turns: 1)], maximumWork: allowance);
            Assert.That(shortResult.Status, Is.EqualTo(KsProcgenGasBindingStatus.BudgetExceeded));
            Assert.That(shortResult.Domains, Is.Empty); Assert.That(shortResult.Work, Is.LessThanOrEqualTo(allowance));
        }
    }

    [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)]
    public void BadPlacementAndTerrainCannotProducePartialTopology(int variant)
    {
        var context = Context(AtmosDirection.All); var placement = Placement(); Vector2i[] cells = Strip;
        if (variant == 0) placement = placement with { PrototypeId = "Unknown" };
        if (variant == 1) placement = placement with { Cell = new(8, 8) };
        if (variant == 2) placement = placement with { QuarterTurns = 4 };
        if (variant == 3) cells = [Strip[0], Strip[0]];
        if (variant == 4) cells = [new(int.MaxValue, 0)];
        var result = context.PlanTopology(cells, [placement]);
        Assert.That(result.Status, Is.Not.EqualTo(KsProcgenGasBindingStatus.Bound)); Assert.That(result.Domains, Is.Empty);
    }

    private static KsProcgenGasPlanningContext Context(AtmosDirection directions, bool noAir = true, bool anchored = true)
    {
        Assert.That(KsProcgenGasPlanningContext.TryCreate([new("Blocker", anchored, directions, noAir, false)], "Captured", out var context, out var issue),
            Is.True, message: issue?.Code); return context!;
    }
}
