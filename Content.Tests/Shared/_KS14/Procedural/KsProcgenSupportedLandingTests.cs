using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenSupportedLandingTests
{
    [Test]
    public void WideMachineRetriesAFrontLandingWhenNearRejectsTheFirstReachableOne()
    {
        var plan = Plan(Assembly());
        Assert.That(plan.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate), plan.Issue?.Code);
        Assert.That(plan.Access.Single().Approach, Is.EqualTo(new Vector2i(3, 1)));
        Assert.That(plan.Relations.Single().PathDistance, Is.EqualTo(1));
        Assert.That(plan.LandingProbes, Is.EqualTo(2));
        Assert.That(plan.Relations.Single().CleanPath.Cells.First(), Is.EqualTo(new Vector2i(3, 1)));
    }

    [Test]
    public void LaterChairChoicesCanForceRetryOfAnEarlierMachineLanding()
    {
        var assembly = Assembly();
        var target = assembly.Members.Single(member => member.Id == "Target");
        target.Entry.Role = KsProcgenEntityRole.Seat;
        var plan = Plan(assembly, targetPosition: new(4f, 0f));
        Assert.That(plan.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate), plan.Issue?.Code);
        Assert.That(plan.Access.Single(access => access.MemberId == "Device").Approach, Is.EqualTo(new Vector2i(3, 1)));
        Assert.That(plan.Access.Single(access => access.MemberId == "Target").Approach, Is.EqualTo(new Vector2i(3, 0)));
        Assert.That(plan.LandingProbes, Is.GreaterThan(3));
        Assert.That(plan.Relations.Single().PathDistance, Is.EqualTo(1));
    }

    [Test]
    public void ExplicitLandingRestrictsChoicesAndBudgetsReturnNoPartialModel()
    {
        var assembly = Assembly();
        var exhausted = Plan(assembly, maximumLandingProbes: 1);
        AssertFailure(exhausted, KsProcgenSupportedAccessStatus.BudgetExceeded);
        Assert.That(exhausted.LandingProbes, Is.EqualTo(1));
        AssertFailure(Plan(assembly, maximumLandingProbes: 0), KsProcgenSupportedAccessStatus.BudgetExceeded);
        assembly = assembly with { Members = assembly.Members.Select(member => member.Id == "Device" ?
            member with { ApproachLanding = new(1, -1) } : member).ToArray() };
        var fixedLanding = Plan(assembly, maximumLandingProbes: 1);
        Assert.That(fixedLanding.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate));
        Assert.That(fixedLanding.LandingProbes, Is.EqualTo(1));
        AssertFailure(Plan(assembly, maximumLandingProbes: -1), KsProcgenSupportedAccessStatus.InvalidInput);
        AssertFailure(Plan(assembly, maximumLandingProbes: 65537), KsProcgenSupportedAccessStatus.InvalidInput);
    }

    [Test]
    public void SearchSharesLandingProbesAcrossCompletePoseAttempts()
    {
        var assembly = Assembly();
        foreach (var member in assembly.Members)
            member.Entry.AllowedQuarterTurns = [0];
        var exhausted = KsProcgenSupportedAssemblySearch.Search(assembly, ["Root", "Device", "Target"], Report(assembly), Room(), 0,
            fixedFloorRoots: [new("Root", new(2f, 2f)), new("Target", new(4f, 1f))], maximumLandingProbes: 1);
        Assert.That(exhausted.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.BudgetExceeded));
        Assert.That(exhausted.LandingProbes, Is.EqualTo(1));
        Assert.That(exhausted.Access, Is.Null);
        var candidate = KsProcgenSupportedAssemblySearch.Search(assembly, ["Root", "Device", "Target"], Report(assembly), Room(), 0,
            fixedFloorRoots: [new("Root", new(2f, 2f)), new("Target", new(4f, 1f))], maximumLandingProbes: 2);
        Assert.That(candidate.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate));
        Assert.That(candidate.LandingProbes, Is.EqualTo(2));
    }

    [Test]
    public void ExhaustedChoicesRejectAndReplayIgnoresUnusedProbeAllowance()
    {
        var assembly = Assembly();
        var candidate = Plan(assembly, maximumLandingProbes: 2);
        var replay = Plan(assembly with { Members = assembly.Members.Reverse().ToArray(), Relations = assembly.Relations.Reverse().ToArray() },
            maximumLandingProbes: 1000);
        Assert.That(replay.Access, Is.EqualTo(candidate.Access));
        Assert.That(replay.Relations, Is.EqualTo(candidate.Relations));
        Assert.That(replay.AccessHash, Is.EqualTo(candidate.AccessHash));
        assembly = assembly with { Relations = assembly.Relations.Select(relation => relation.Id == "Near" ?
            relation with { MinimumDistance = 0, MaximumDistance = 0 } : relation).ToArray() };
        AssertFailure(Plan(assembly), KsProcgenSupportedAccessStatus.Rejected);
    }

    private static KsProcgenRoomAccessMask Room() => new(
        Enumerable.Range(0, 5).SelectMany(x => Enumerable.Range(0, 5).Select(y => new Vector2i(x, y))).ToHashSet(),
        new HashSet<Vector2i>(), new HashSet<Vector2i>(), new HashSet<Vector2i>(), new HashSet<Vector2i> { new(0, 0) }, Vector2.Zero);

    private static KsProcgenResolvedAssembly Assembly() => new("LandingFixture", "Base", "Root",
        new[] { "Root", "Device", "Target" }.Select(id => new KsProcgenResolvedAssemblyMember(id, id, true,
            new() { Id = id, Entity = id, Movement = id == "Target" ? KsProcgenMovementClass.Clear : KsProcgenMovementClass.VaultRequired,
                Footprint = id == "Target" ? [new(0, 0)] : [new(0, 0), new(1, 0)],
                RequiresInteractionApproach = id == "Device" }, KsProcgenMemberRotation.Independent, 0)).ToArray(),
        [new("Top", "Device", KsProcgenRelationKind.OnSurface, "Root", KsProcgenRelationSeverity.Required, 1, 4,
            null, null, KsProcgenApproachPolicy.EmptyFloor),
            new("Near", "Device", KsProcgenRelationKind.Near, "Target", KsProcgenRelationSeverity.Required, 1, 1,
                null, null, KsProcgenApproachPolicy.EmptyFloor)]);

    private static KsProcgenAssemblyCapabilityReport Report(KsProcgenResolvedAssembly assembly) => new(
        assembly.Members.Select(member => new KsProcgenMemberCapabilityDeclaration(member.Id,
            new(member.Entry.Entity, true, true, true, Vector2.Zero, true, false, false, false, 0.0, [], []))).ToArray(), []);

    private static KsProcgenSupportedAccessPlan Plan(KsProcgenResolvedAssembly assembly, Vector2? targetPosition = null,
        int maximumLandingProbes = 4096) => KsProcgenSupportedAccessPlanner.Plan(assembly, ["Root", "Device", "Target"], Report(assembly),
        [new("Root", new(2f, 2f)), new("Target", targetPosition ?? new(4f, 1f))],
        [new("Root", 0), new("Device", 0), new("Target", 0)], 0, Room(), maximumLandingProbes: maximumLandingProbes);

    private static void AssertFailure(KsProcgenSupportedAccessPlan plan, KsProcgenSupportedAccessStatus status)
    {
        Assert.That(plan.Status, Is.EqualTo(status), plan.Issue?.Code);
        Assert.That(plan.Poses, Is.Null);
        Assert.That(plan.Access, Is.Empty);
        Assert.That(plan.Relations, Is.Empty);
        Assert.That(plan.AccessHash, Is.Zero);
    }
}
