using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenNearRelationTests
{
    [Test]
    public void WallDetourUsesShortestCleanDistanceAndPersistsItsPath()
    {
        var floor = Rectangle(3, 3);
        var blocked = new HashSet<Vector2i> { new(1, 0), new(1, 1) };
        var evaluation = Evaluate(new(0, 0), new(2, 0), floor, blocked, maximum: 6);
        var witness = evaluation.Witnesses.Single();
        Assert.That(evaluation.RequiredSatisfied, Is.True);
        Assert.That(witness.PathDistance, Is.EqualTo(6));
        Assert.That(witness.CleanPath.Cells.Count, Is.EqualTo(7));
        Assert.That(witness.CleanPath.Cells.All(cell => floor.Contains(cell) && !blocked.Contains(cell)), Is.True);
        Assert.That(witness.CleanPath.Cells.Zip(witness.CleanPath.Cells.Skip(1))
            .All(pair => System.Math.Abs(pair.First.X - pair.Second.X) +
                         System.Math.Abs(pair.First.Y - pair.Second.Y) == 1), Is.True);
        Assert.That(Evaluate(new(0, 0), new(2, 0), floor, blocked, maximum: 6).Witnesses,
            Is.EqualTo(evaluation.Witnesses), "Path witnesses must compare by value on replay.");
        var shortRange = Evaluate(new(0, 0), new(2, 0), floor, blocked, maximum: 2);
        Assert.That(shortRange.RequiredSatisfied, Is.False);
        Assert.That(shortRange.Witnesses.Single().ReasonCode, Is.EqualTo("NearPathUnavailable"));
        Assert.That(shortRange.Witnesses.Single().PathDistance, Is.Null);
    }

    [TestCase(KsProcgenMovementClass.Blocks)]
    [TestCase(KsProcgenMovementClass.VaultRequired)]
    public void BlockingAndVaultOnlyTrialMembersCannotShortenNearDistance(KsProcgenMovementClass movement)
    {
        var assembly = Assembly();
        var proposals = new[] { Proposal("A", new(0, 0)), Proposal("B", new(2, 0)),
            Proposal("Blocker", new(1, 0)) with { Movement = movement } };
        var evaluation = KsProcgenAssemblyRelationEvaluator.Evaluate("Pack", "Core", 0, assembly,
            proposals, new HashSet<Vector2i>(), floor: Rectangle(3, 1), blockingCells: new HashSet<Vector2i>());
        Assert.That(evaluation.RequiredSatisfied, Is.False);
        Assert.That(evaluation.Witnesses.Single().CleanPath.Cells, Is.Empty);
    }

    [Test]
    public void MinimumDistanceAppliesToShortestPathRatherThanAnInventedDetour()
    {
        var evaluation = Evaluate(new(0, 0), new(2, 0), Rectangle(3, 3),
            new HashSet<Vector2i>(), minimum: 3, maximum: 6);
        Assert.That(evaluation.RequiredSatisfied, Is.False);
        Assert.That(evaluation.Witnesses.Single().PathDistance, Is.EqualTo(2));
        Assert.That(evaluation.Witnesses.Single().ReasonCode, Is.EqualTo("NearDistanceBelowMinimum"));
    }

    [Test]
    public void SharedLandingCanHaveZeroDistanceWithoutExpandingNodes()
    {
        var budget = new KsProcgenRelationPathBudget(maximumExpandedCells: 0);
        var proposals = new[]
        {
            Proposal("A", new(0, 0)) with { InteractionApproach = new Vector2i(1, 0) },
            Proposal("B", new(2, 0)) with { InteractionApproach = new Vector2i(1, 0) },
        };
        var evaluation = KsProcgenAssemblyRelationEvaluator.Evaluate("Pack", "Core", 0,
            Assembly(minimum: 0, maximum: 0), proposals, new HashSet<Vector2i>(),
            floor: Rectangle(3, 1), blockingCells: new HashSet<Vector2i>(), pathBudget: budget);
        Assert.That(evaluation.RequiredSatisfied, Is.True);
        Assert.That(evaluation.Witnesses.Single().PathDistance, Is.Zero);
        Assert.That(budget.ExpandedCells, Is.Zero);
        Assert.That(budget.Truncated, Is.False);
    }

    [Test]
    public void PathBudgetIsSharedAcrossCandidateEvaluationsAndNeverReportsAFalseMiss()
    {
        var budget = new KsProcgenRelationPathBudget(maximumExpandedCells: 2);
        Assert.That(Evaluate(new(0, 0), new(1, 0), Rectangle(4, 1),
            new HashSet<Vector2i>(), budget: budget).RequiredSatisfied, Is.True);
        var exhausted = Evaluate(new(0, 0), new(3, 0), Rectangle(4, 1),
            new HashSet<Vector2i>(), budget: budget, severity: KsProcgenRelationSeverity.Preferred);
        Assert.That(exhausted.RequiredSatisfied, Is.False);
        Assert.That(exhausted.PathSearchTruncated, Is.True);
        Assert.That(exhausted.Witnesses.Single().State, Is.EqualTo(KsProcgenConstraintState.Unverified));
        Assert.That(exhausted.Witnesses.Single().ReasonCode, Is.EqualTo("NearPathBudget"));
        Assert.That(exhausted.PreferredApplicable, Is.Zero);
        Assert.That(budget.ExpandedCells, Is.EqualTo(2));
    }

    [Test]
    public void DeclaredLandingRotatesWithBlockingFurniture()
    {
        var assembly = Assembly(landing: new Vector2i(0, -1), sourceMovement: KsProcgenMovementClass.VaultRequired);
        var evaluation = KsProcgenAssemblyRelationEvaluator.Evaluate("Pack", "Core", 0, assembly,
            [Proposal("A", new(1, 1)) with { Movement = KsProcgenMovementClass.VaultRequired, QuarterTurns = 1 },
                Proposal("B", new(0, 3))], new HashSet<Vector2i>(),
            floor: Rectangle(4, 4), blockingCells: new HashSet<Vector2i>());
        Assert.That(evaluation.RequiredSatisfied, Is.True);
        Assert.That(evaluation.Witnesses.Single().CleanPath.Cells[0], Is.EqualTo(new Vector2i(0, 1)));
        Assert.That(evaluation.Witnesses.Single().PathDistance, Is.EqualTo(2));
    }

    [Test]
    public void BlockingFurnitureWithoutALandingDoesNotInventOne()
    {
        var evaluation = KsProcgenAssemblyRelationEvaluator.Evaluate("Pack", "Core", 0,
            Assembly(sourceMovement: KsProcgenMovementClass.Blocks),
            [Proposal("A", new(0, 0)) with { Movement = KsProcgenMovementClass.Blocks }, Proposal("B", new(2, 0))],
            new HashSet<Vector2i>(), floor: Rectangle(3, 1), blockingCells: new HashSet<Vector2i>());
        Assert.That(evaluation.RequiredSatisfied, Is.False);
        Assert.That(evaluation.Witnesses.Single().ReasonCode, Is.EqualTo("NearLandingUnavailable"));
    }

    [Test]
    public void BlockedLandingDoesNotMoveToADifferentSideOfFurniture()
    {
        var evaluation = KsProcgenAssemblyRelationEvaluator.Evaluate("Pack", "Core", 0,
            Assembly(landing: new Vector2i(0, -1), sourceMovement: KsProcgenMovementClass.Blocks),
            [Proposal("A", new(1, 1)) with { Movement = KsProcgenMovementClass.Blocks }, Proposal("B", new(2, 0))],
            new HashSet<Vector2i>(), floor: Rectangle(3, 3),
            blockingCells: new HashSet<Vector2i> { new(1, 0) });
        Assert.That(evaluation.RequiredSatisfied, Is.False);
        Assert.That(evaluation.Witnesses.Single().ReasonCode, Is.EqualTo("NearLandingBlocked"));
    }

    [Test]
    public void ExtremeCoordinatesCannotWrapIntoAnAdjacentTile()
    {
        var evaluation = Evaluate(new(int.MaxValue, 0), new(int.MinValue, 0),
            new HashSet<Vector2i> { new(int.MaxValue, 0), new(int.MinValue, 0) },
            new HashSet<Vector2i>(), maximum: 1);
        Assert.That(evaluation.RequiredSatisfied, Is.False);
    }

    [TestCase(0, 0)]
    [TestCase(1, 1)]
    [TestCase(10, 0)]
    [TestCase(int.MinValue, 0)]
    public void BlockingMemberLandingMustBeBoundedAndCardinallyBesideItsFootprint(int x, int y)
    {
        var definition = new KsProcgenAssemblyVariant
        {
            Id = "Base", AnchorMember = "Table",
            Members = [new() { Id = "Table", Entity = "Table", ApproachLanding = new Vector2i(x, y) }],
        };
        Assert.That(KsProcgenAssemblyCompiler.TryCompileVariant("Fixture", definition,
            new Dictionary<string, string>(), _ => true, out _, out var issue), Is.False);
        Assert.That(issue?.Code, Is.EqualTo("InvalidAssemblyApproachLanding"));
    }

    [TestCase(KsProcgenRelationSeverity.Required, false)]
    [TestCase(KsProcgenRelationSeverity.Preferred, true)]
    public void DisconnectedNearRetainsRequiredOrPreferredSeverity(KsProcgenRelationSeverity severity, bool valid)
    {
        var evaluation = Evaluate(new(0, 0), new(2, 0), Rectangle(3, 1),
            new HashSet<Vector2i> { new(1, 0) }, severity: severity);
        Assert.That(evaluation.RequiredSatisfied, Is.EqualTo(valid));
        Assert.That(evaluation.Witnesses.Single().State, Is.EqualTo(KsProcgenConstraintState.Missed));
        Assert.That(evaluation.Witnesses.Single().ReasonCode, Is.EqualTo("NearPathUnavailable"));
    }

    [Test]
    public void NearCannotCrossADiagonalGap()
    {
        var evaluation = Evaluate(new(0, 0), new(1, 1),
            new HashSet<Vector2i> { new(0, 0), new(1, 1) }, new HashSet<Vector2i>());
        Assert.That(evaluation.RequiredSatisfied, Is.False);
        Assert.That(evaluation.Witnesses.Single().CleanPath.Cells, Is.Empty);
    }

    [Test]
    public void DeclaredLandingCannotWrapAcrossCoordinateBounds()
    {
        var evaluation = KsProcgenAssemblyRelationEvaluator.Evaluate("Pack", "Core", 0,
            Assembly(landing: new Vector2i(1, 0), sourceMovement: KsProcgenMovementClass.Blocks),
            [Proposal("A", new(int.MaxValue, 0)) with { Movement = KsProcgenMovementClass.Blocks },
                Proposal("B", new(int.MinValue, 0))], new HashSet<Vector2i>(),
            floor: new HashSet<Vector2i> { new(int.MaxValue, 0), new(int.MinValue, 0) },
            blockingCells: new HashSet<Vector2i>());
        Assert.That(evaluation.RequiredSatisfied, Is.False);
        Assert.That(evaluation.Witnesses.Single().ReasonCode, Is.EqualTo("NearLandingUnavailable"));
    }

    private static KsProcgenAssemblyRelationEvaluation Evaluate(Vector2i start, Vector2i goal,
        IReadOnlySet<Vector2i> floor, IReadOnlySet<Vector2i> blocked, int minimum = 1, int maximum = 4,
        KsProcgenRelationPathBudget? budget = null, KsProcgenRelationSeverity severity = KsProcgenRelationSeverity.Required) =>
        KsProcgenAssemblyRelationEvaluator.Evaluate("Pack", "Core", 0,
            Assembly(minimum: minimum, maximum: maximum, severity: severity),
            [Proposal("A", start), Proposal("B", goal)], new HashSet<Vector2i>(),
            floor: floor, blockingCells: blocked, pathBudget: budget);

    private static KsProcgenResolvedAssembly Assembly(int minimum = 1, int maximum = 4,
        Vector2i? landing = null, KsProcgenMovementClass sourceMovement = KsProcgenMovementClass.Clear,
        KsProcgenRelationSeverity severity = KsProcgenRelationSeverity.Required)
    {
        Assert.That(KsProcgenAssemblyCompiler.TryCompileVariant("Fixture", new KsProcgenAssemblyVariant
        {
            Id = "Base", AnchorMember = "A",
            Members =
            [
                new() { Id = "A", Entity = "A", Movement = sourceMovement, ApproachLanding = landing },
                new() { Id = "B", Entity = "B", Movement = KsProcgenMovementClass.Clear },
                new() { Id = "Blocker", Entity = "Blocker", MinimumCount = 0 },
            ],
            Relations = [new() { Id = "Near", Subject = "A", Target = "B", Kind = KsProcgenRelationKind.Near,
                MinimumDistance = minimum, MaximumDistance = maximum, Severity = severity }],
        }, new Dictionary<string, string>(), _ => true, out var assembly, out var issue), Is.True, issue?.Message);
        return assembly!;
    }

    private static KsProcgenEntityProposal Proposal(string id, Vector2i cell) => new(
        "Pack", id, id, KsProcgenEntityRole.Storage, KsProcgenMovementClass.Clear, cell, 0, null);

    private static HashSet<Vector2i> Rectangle(int width, int height) => Enumerable.Range(0, width)
        .SelectMany(x => Enumerable.Range(0, height).Select(y => new Vector2i(x, y))).ToHashSet();
}
