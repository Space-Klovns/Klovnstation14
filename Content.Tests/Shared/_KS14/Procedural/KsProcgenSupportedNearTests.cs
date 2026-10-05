using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenSupportedNearTests
{
    [Test]
    public void DistanceUsesCardinalDetourAroundVaultSupportAndPersistsThePath()
    {
        var plan = Plan(Assembly(minimum: 4, maximum: 4));
        Assert.That(plan.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate), plan.Issue?.Code);
        var near = plan.Relations.Single();
        Assert.That(near.State, Is.EqualTo(KsProcgenConstraintState.Satisfied));
        Assert.That(near.PathDistance, Is.EqualTo(4));
        Assert.That(near.CleanPath.Cells.First(), Is.EqualTo(new Vector2i(1, 2)));
        Assert.That(near.CleanPath.Cells.Last(), Is.EqualTo(new Vector2i(3, 2)));
        Assert.That(near.CleanPath.Cells, Does.Not.Contain(new Vector2i(2, 2)));
        AssertCardinal(near.CleanPath);
        Assert.That(plan.EnginePlacementVerified, Is.False);
    }

    [Test]
    public void TooCloseDoesNotInventALongerRouteAndPreferredMissesKeepTheirWitnesses()
    {
        var assembly = Assembly(minimum: 5, maximum: 6);
        AssertFailure(Plan(assembly), KsProcgenSupportedAccessStatus.Rejected);
        assembly = assembly with { Relations = assembly.Relations.Select(relation => relation.Id == "Near" ?
            relation with { Severity = KsProcgenRelationSeverity.Preferred } : relation).ToArray() };
        var preferred = Plan(assembly);
        Assert.That(preferred.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate));
        Assert.That(preferred.Relations.Single().ReasonCode, Is.EqualTo("NearDistanceBelowMinimum"));
        Assert.That(preferred.Relations.Single().PathDistance, Is.EqualTo(4));
        Assert.That(preferred.PreferredRelationsApplicable, Is.EqualTo(1));
        Assert.That(preferred.PreferredRelationsSatisfied, Is.Zero);
        AssertFailure(Plan(Assembly(minimum: 1, maximum: 3)), KsProcgenSupportedAccessStatus.Rejected);
    }

    [Test]
    public void ClearSupportStillCannotBecomeAShortcutAndWallsCanCutTheRoute()
    {
        var assembly = Assembly(minimum: 4, maximum: 4);
        foreach (var member in assembly.Members.Where(member => member.Id is "Root" or "Device"))
            member.Entry.Movement = KsProcgenMovementClass.Clear;
        var plan = Plan(assembly);
        Assert.That(plan.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate));
        Assert.That(plan.Relations.Single().PathDistance, Is.EqualTo(4));
        Assert.That(plan.Relations.Single().CleanPath.Cells, Does.Not.Contain(new Vector2i(2, 2)));
        var divided = Room() with { Walls = Enumerable.Range(0, 5).Select(y => new Vector2i(2, y)).Where(cell => cell != new Vector2i(2, 2)).ToHashSet() };
        AssertFailure(Plan(assembly, room: divided), KsProcgenSupportedAccessStatus.Rejected);
    }

    [Test]
    public void AccessAndAllNearRelationsSpendOneAllowanceEvenForPreferredRelations()
    {
        var assembly = Assembly(minimum: 1, maximum: 8);
        var accessOnly = Plan(assembly with { Relations = assembly.Relations.Where(relation => relation.Id != "Near").ToArray() });
        Assert.That(accessOnly.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate));
        var exhausted = Plan(assembly, maximumExpandedCells: accessOnly.ExpandedCells);
        AssertFailure(exhausted, KsProcgenSupportedAccessStatus.BudgetExceeded);
        Assert.That(exhausted.ExpandedCells, Is.EqualTo(accessOnly.ExpandedCells));
        var successful = Plan(assembly);
        Assert.That(successful.ExpandedCells, Is.GreaterThan(accessOnly.ExpandedCells));
        assembly = assembly with { Relations = assembly.Relations.Append(assembly.Relations.Single(relation => relation.Id == "Near") with
            { Id = "SecondNear", Severity = KsProcgenRelationSeverity.Preferred }).ToArray() };
        AssertFailure(Plan(assembly, maximumExpandedCells: successful.ExpandedCells), KsProcgenSupportedAccessStatus.BudgetExceeded);
    }

    [Test]
    public void SearchRevisitsRootPositionsToMeetARequiredNearDistance()
    {
        var assembly = Assembly(minimum: 1, maximum: 1);
        assembly.Members.Single(member => member.Id == "Device").Entry.AllowedQuarterTurns = [1];
        var result = KsProcgenSupportedAssemblySearch.Search(assembly, ["Root", "Device", "Target"], Report(assembly), Room(), 14,
            fixedFloorRoots: [new("Target", new(0f, 2f))]);
        Assert.That(result.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate), result.Issue?.Code);
        Assert.That(result.Access!.Relations.Single().PathDistance, Is.EqualTo(1));
        Assert.That(result.Access.Relations.Single().CleanPath.Cells.Last(), Is.EqualTo(new Vector2i(0, 2)));
        Assert.That(result.ExpandedCells, Is.GreaterThanOrEqualTo(result.Access.ExpandedCells));
    }

    [Test]
    public void BoundsAndReplayIncludeDistancesAndPaths()
    {
        var assembly = Assembly(minimum: 4, maximum: 4);
        var original = Plan(assembly);
        var replay = Plan(assembly with { Members = assembly.Members.Reverse().ToArray(), Relations = assembly.Relations.Reverse().ToArray() },
            room: Room() with { Floor = Room().Floor.Reverse().ToHashSet() }, maximumExpandedCells: 10000);
        Assert.That(replay.AccessHash, Is.EqualTo(original.AccessHash));
        Assert.That(replay.Relations, Is.EqualTo(original.Relations));
        Assert.That(Plan(Assembly(minimum: 1, maximum: 4)).AccessHash, Is.Not.EqualTo(original.AccessHash));
        AssertFailure(Plan(Assembly(minimum: -1, maximum: 4)), KsProcgenSupportedAccessStatus.InvalidInput);
        AssertFailure(Plan(Assembly(minimum: 4, maximum: 3)), KsProcgenSupportedAccessStatus.InvalidInput);
        AssertFailure(Plan(Assembly(minimum: 0, maximum: 65)), KsProcgenSupportedAccessStatus.InvalidInput);
    }

    private static KsProcgenRoomAccessMask Room() => new(
        Enumerable.Range(0, 5).SelectMany(x => Enumerable.Range(0, 5).Select(y => new Vector2i(x, y))).ToHashSet(),
        new HashSet<Vector2i>(), new HashSet<Vector2i>(), new HashSet<Vector2i>(), new HashSet<Vector2i> { new(0, 0) }, Vector2.Zero);

    private static KsProcgenResolvedAssembly Assembly(int minimum, int maximum) => new("NearFixture", "Base", "Root",
        new[] { "Root", "Device", "Target" }.Select(id => new KsProcgenResolvedAssemblyMember(id, id, true,
            new() { Id = id, Entity = id, Movement = id == "Target" ? KsProcgenMovementClass.Clear : KsProcgenMovementClass.VaultRequired,
                RequiresInteractionApproach = id == "Device" }, KsProcgenMemberRotation.Independent, 0)).ToArray(),
        [new("Top", "Device", KsProcgenRelationKind.OnSurface, "Root", KsProcgenRelationSeverity.Required, 1, 4,
            null, null, KsProcgenApproachPolicy.EmptyFloor),
            new("Near", "Device", KsProcgenRelationKind.Near, "Target", KsProcgenRelationSeverity.Required, minimum, maximum,
                null, null, KsProcgenApproachPolicy.EmptyFloor)]);

    private static KsProcgenAssemblyCapabilityReport Report(KsProcgenResolvedAssembly assembly) => new(
        assembly.Members.Select(member => new KsProcgenMemberCapabilityDeclaration(member.Id,
            new(member.Entry.Entity, true, true, true, Vector2.Zero, true, false, false, false, 0.0, [], []))).ToArray(), []);

    private static KsProcgenSupportedAccessPlan Plan(KsProcgenResolvedAssembly assembly, KsProcgenRoomAccessMask? room = null,
        int maximumExpandedCells = 4096) => KsProcgenSupportedAccessPlanner.Plan(assembly, ["Root", "Device", "Target"], Report(assembly),
        [new("Root", new(2f, 2f)), new("Target", new(3f, 2f))], [new("Root", 0), new("Device", 1), new("Target", 0)],
        0, room ?? Room(), maximumExpandedCells: maximumExpandedCells);

    private static void AssertFailure(KsProcgenSupportedAccessPlan plan, KsProcgenSupportedAccessStatus status)
    {
        Assert.That(plan.Status, Is.EqualTo(status), plan.Issue?.Code);
        Assert.That(plan.Poses, Is.Null);
        Assert.That(plan.Access, Is.Empty);
        Assert.That(plan.Relations, Is.Empty);
        Assert.That(plan.Footprints, Is.Empty);
        Assert.That(plan.AccessHash, Is.Zero);
    }

    private static void AssertCardinal(KsProcgenRelationPath path)
    {
        for (var index = 1; index < path.Cells.Count; index++)
        {
            var delta = path.Cells[index] - path.Cells[index - 1];
            Assert.That(Math.Abs(delta.X) + Math.Abs(delta.Y), Is.EqualTo(1));
        }
    }
}
