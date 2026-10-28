using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenPackingEligibilityTests
{
    private static KsProcgenLayoutFamily Family() => new("Choice",
        [new("Good", [new(0, 0), new(1, 0)], [new("A", [new(0, 0)]), new("B", [new(1, 0)])]),
         new("Other", [new(0, 0), new(1, 0)], [new("Large", [new(0, 0), new(1, 0)])])], [0, 1, 2, 3]);

    [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
    public void EligibilitySelectsOnlyTheAllowedCompleteAlternativeAtItsAllowedRotation(int turns)
    {
        var family = Family();
        Assert.That(KsProcgenPackingEligibility.TryCreate(new[] { new KsProcgenLayoutOrientation("Choice", "Good", turns) },
            out var eligibility, out var issue), Is.True, message: issue?.Code);
        var origin = new Vector2i(-3, 4);
        var request = new KsProcgenRequest { RequestId = "Eligibility", Mode = KsProcgenMode.Prefabs,
            Shape = new() { Cells = new[] { new Vector2i(0, 0), new Vector2i(1, 0) }
                .Select(cell => KsProcgenGeometry.TransformCell(cell, default, origin, turns)).ToList() } };
        var result = KsProcgenPackingPlanner.Plan(request, [family], eligibility: eligibility);
        Assert.That(result.Status, Is.EqualTo(KsProcgenPackingStatus.GeometryReady), message: result.Issue?.Code);
        Assert.That(result.Placements, Has.Count.EqualTo(1));
        Assert.That(result.Placements[0].OptionId, Is.EqualTo("Good"));
        Assert.That(result.Placements[0].QuarterTurns, Is.EqualTo(turns));
        Assert.That(result.Placements[0].Origin, Is.EqualTo(origin));
        Assert.That(result.CellClaims.All(entry => entry.Claim.OwnerId.Contains("/Good@", StringComparison.Ordinal)), Is.True);
        Assert.That(result.PrefabCells, Is.EqualTo(2));
    }

    [TestCase(KsProcgenMode.Prefabs, KsProcgenPackingStatus.NoGeometricCover)]
    [TestCase(KsProcgenMode.Hybrid, KsProcgenPackingStatus.GeometryReady)]
    public void EmptyEligibilityNeverUsesAnExcludedRoomAndPreservesResidualSemantics(KsProcgenMode mode, KsProcgenPackingStatus status)
    {
        Assert.That(KsProcgenPackingEligibility.TryCreate(Array.Empty<KsProcgenLayoutOrientation>(), out var eligibility, out _), Is.True);
        var request = new KsProcgenRequest { RequestId = "Empty", Mode = mode,
            Shape = new() { Cells = [new(0, 0), new(1, 0)] } };
        var result = KsProcgenPackingPlanner.Plan(request, [Family()], eligibility: eligibility);
        Assert.That(result.Status, Is.EqualTo(status)); Assert.That(result.Placements, Is.Empty);
        Assert.That(result.PrefabCells, Is.Zero);
        if (mode == KsProcgenMode.Hybrid)
            Assert.That(result.CellClaims.All(entry => entry.Claim.Disposition == KsProcgenCellDisposition.ProceduralFloor), Is.True);
    }

    [Test]
    public void EligibilityIsDetachedAndCannotBeChangedThroughItsExposedCollection()
    {
        var input = new List<KsProcgenLayoutOrientation> { new("Choice", "Good", 0) };
        Assert.That(KsProcgenPackingEligibility.TryCreate(input, out var eligibility, out _), Is.True);
        input[0] = new("Choice", "Other", 2); input.Clear();
        Assert.That(eligibility!.Orientations, Is.EqualTo(new[] { new KsProcgenLayoutOrientation("Choice", "Good", 0) }));
        Assert.Throws<NotSupportedException>(() => ((IList<KsProcgenLayoutOrientation>) eligibility.Orientations).Clear());
    }

    [TestCase("Missing", "Good", 0)] [TestCase("Choice", "Missing", 0)] [TestCase("Choice", "Good", 1)]
    public void UnknownAlternativesAndDisallowedFamilyRotationsRejectInsteadOfSilentlyWeakeningEligibility(string familyId, string optionId, int turns)
    {
        Assert.That(KsProcgenPackingEligibility.TryCreate(new[] { new KsProcgenLayoutOrientation(familyId, optionId, turns) },
            out var eligibility, out _), Is.True);
        var family = new KsProcgenLayoutFamily("Choice", Family().Options, [0]);
        var result = KsProcgenPackingPlanner.Plan(new() { RequestId = "Unknown", Mode = KsProcgenMode.Prefabs,
            Shape = new() { Cells = [new(0, 0), new(1, 0)] } }, [family], eligibility: eligibility);
        Assert.That(result.Status, Is.EqualTo(KsProcgenPackingStatus.InvalidInput));
        Assert.That(result.Issue?.Code, Is.EqualTo("UnknownPackingOrientation"));
    }

    [TestCase("family")] [TestCase("option")] [TestCase("negative")] [TestCase("noncardinal")]
    [TestCase("duplicate")] [TestCase("budget")]
    public void MalformedOrOversizedEligibilityYieldsNoDetachedConstraints(string mode)
    {
        var input = new List<KsProcgenLayoutOrientation> { new("Choice", "Good", 0) };
        input[0] = mode switch { "family" => new("", "Good", 0), "option" => new("Choice", "", 0),
            "negative" => new("Choice", "Good", -1), "noncardinal" => new("Choice", "Good", 4), _ => input[0] };
        if (mode == "duplicate") input.Add(input[0]);
        if (mode == "budget") input.AddRange(Enumerable.Repeat(input[0], KsProcgenPackingEligibility.MaximumOrientations));
        Assert.That(KsProcgenPackingEligibility.TryCreate(input, out var eligibility, out var issue), Is.False);
        Assert.That(eligibility, Is.Null); Assert.That(issue?.Code, Is.EqualTo("InvalidPackingEligibility"));
    }
}
