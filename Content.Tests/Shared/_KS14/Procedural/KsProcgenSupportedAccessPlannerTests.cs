using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenSupportedAccessPlannerTests
{
    [Test]
    public void MachineOnVaultRequiredSupportUsesFrontWithoutWalkingThroughSupport()
    {
        var assembly = Assembly();
        var plan = Plan(assembly, Room(), turn: 1);
        Assert.That(plan.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate));
        var access = plan.Access.Single();
        Assert.That(access.Layer, Is.EqualTo(KsProcgenPlacementLayer.Surface));
        Assert.That(access.Approach, Is.EqualTo(new Vector2i(1, 2)));
        Assert.That(access.CleanPath.Cells, Does.Not.Contain(new Vector2i(2, 2)));
        Assert.That(access.CleanPath.Cells.First(), Is.EqualTo(new Vector2i(0, 0)));
        Assert.That(access.CleanPath.Cells.Last(), Is.EqualTo(access.Approach));
        AssertCardinal(access.CleanPath);
        Assert.That(plan.EnginePlacementVerified, Is.False);
    }

    [Test]
    public void RightWallForbidsFacingIntoWallAndAllowsWestFacingInTinyRoom()
    {
        var room = new KsProcgenRoomAccessMask(new HashSet<Vector2i> { new(0, 2), new(1, 2), new(2, 2) },
            new HashSet<Vector2i> { new(3, 2) }, new HashSet<Vector2i>(), new HashSet<Vector2i>(),
            new HashSet<Vector2i> { new(0, 2) }, Vector2.Zero);
        Assert.That(Plan(Assembly(), room, turn: 1).Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate));
        AssertFailure(Plan(Assembly(), room, turn: 3), "SupportedCleanApproachUnavailable");
    }

    [Test]
    public void DiagonalOnlyReachabilityAndVaultObstaclesCannotBecomeCleanPaths()
    {
        var assembly = Assembly();
        var isolated = Room() with { Blocking = new HashSet<Vector2i> { new(1, 0), new(0, 1) } };
        AssertFailure(Plan(assembly, isolated, turn: 0), "SupportedCleanApproachUnavailable");
        var room = new KsProcgenRoomAccessMask(new HashSet<Vector2i> { new(0, 2), new(1, 2), new(2, 2) },
            new HashSet<Vector2i>(), new HashSet<Vector2i> { new(1, 2) }, new HashSet<Vector2i>(),
            new HashSet<Vector2i> { new(0, 2) }, Vector2.Zero);
        AssertFailure(Plan(assembly, room, turn: 1), "SupportedCleanApproachUnavailable");
        var withObstacle = assembly with { Members = assembly.Members.Append(
            Member("Obstacle", movement: KsProcgenMovementClass.VaultRequired)).ToArray() };
        var corridor = room with
        {
            Floor = new HashSet<Vector2i> { new(0, 0), new(1, 0), new(2, 0), new(2, 1), new(2, 2) },
            Blocking = new HashSet<Vector2i>(), Network = new HashSet<Vector2i> { new(0, 0) },
        };
        AssertFailure(Plan(withObstacle, corridor, turn: 0), "SupportedCleanApproachUnavailable");
    }

    [Test]
    public void UsesSeatRequiresThatChairAndTheChairGetsAnIndependentCleanApproach()
    {
        var assembly = Assembly() with { Members = Assembly().Members.Append(Member("Seat", role: KsProcgenEntityRole.Seat,
            movement: KsProcgenMovementClass.Clear)).ToArray(), Relations = Assembly().Relations.Append(
                Relation("SeatUse", "Device", "Seat", KsProcgenRelationKind.UsesSeat)).ToArray() };
        var plan = Plan(assembly, Room(), turn: 0);
        Assert.That(plan.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate), plan.Issue?.Code);
        var machine = plan.Access.Single(access => access.MemberId == "Device");
        var chair = plan.Access.Single(access => access.MemberId == "Seat");
        Assert.That(machine.Approach, Is.EqualTo(new Vector2i(2, 1)));
        Assert.That(machine.AssociatedSeatMemberId, Is.EqualTo("Seat"));
        Assert.That(chair.CleanPath.Cells, Does.Not.Contain(new Vector2i(2, 1)));
        AssertCardinal(chair.CleanPath);
        assembly = assembly with { Relations = assembly.Relations.Append(Relation("Open", "Device", null,
            KsProcgenRelationKind.FacingOpenSpace)).ToArray() };
        AssertFailure(Plan(assembly, Room(), turn: 0), "SupportedCleanApproachUnavailable");
        assembly = assembly with { Relations = assembly.Relations.Select(relation => relation.Id == "Open" ?
            relation with { ApproachPolicy = KsProcgenApproachPolicy.EmptyOrAssociatedSeat } : relation).ToArray() };
        Assert.That(Plan(assembly, Room(), turn: 0).Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate));
        AssertFailure(Plan(assembly, Room() with { Occupied = new HashSet<Vector2i> { new(2, 1) } }, turn: 0),
            "SupportedCleanApproachUnavailable");
        assembly.Members.Single(member => member.Id == "Seat").Entry.Footprint = [new(0, 0), new(1, 0)];
        AssertFailure(Plan(assembly, Room(), turn: 0), "SupportedSeatFootprintUnsupported");
    }

    [Test]
    public void WideMachinesHonorAnExplicitFrontLanding()
    {
        var assembly = Assembly();
        foreach (var member in assembly.Members)
            member.Entry.Footprint = [new(0, 0), new(1, 0)];
        assembly = assembly with { Members = assembly.Members.Select(member => member.Id == "Device" ?
            member with { ApproachLanding = new(1, -1) } : member).ToArray() };
        var plan = Plan(assembly, Room(), turn: 0);
        Assert.That(plan.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate));
        Assert.That(plan.Access.Single().Approach, Is.EqualTo(new Vector2i(3, 1)));
        Assert.That(plan.Access.Single().CleanPath.Cells, Does.Not.Contain(new Vector2i(2, 2)));
        Assert.That(plan.Access.Single().CleanPath.Cells, Does.Not.Contain(new Vector2i(3, 2)));
        assembly = assembly with { Members = assembly.Members.Select(member => member.Id == "Device" ?
            member with { ApproachLanding = new(-1, 0) } : member).ToArray() };
        AssertFailure(Plan(assembly, Room(), turn: 0), "SupportedCleanApproachUnavailable");
    }

    [Test]
    public void FractionalOffsetsConservativelyCoverTilesAndRespectCoordinateOrigin()
    {
        var assembly = Assembly();
        var report = Report(assembly) with { Members = Report(assembly).Members.Select(member => member.MemberId == "Root" ?
            member with { Capabilities = member.Capabilities with { SurfaceOffset = new(0.25f, 0.25f) } } : member).ToArray() };
        var plan = Plan(assembly, Room(), turn: 0, report: report);
        Assert.That(plan.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate));
        Assert.That(plan.Footprints.Single(footprint => footprint.MemberId == "Device").Cells,
            Is.EquivalentTo(new Vector2i[] { new(2, 2), new(3, 2), new(2, 3), new(3, 3) }));
        var shifted = KsProcgenSupportedAccessPlanner.Plan(assembly, ["Root", "Device"], report,
            [new("Root", new(2.5f, 2.5f))], [new("Root", 0), new("Device", 0)], 0,
            Room() with { CellCenterOrigin = new(0.5f, 0.5f) });
        Assert.That(shifted.Access, Is.EqualTo(plan.Access));
        Assert.That(shifted.Footprints.SelectMany(footprint => footprint.Cells),
            Is.EqualTo(plan.Footprints.SelectMany(footprint => footprint.Cells)));
        Assert.That(shifted.AccessHash, Is.Not.EqualTo(plan.AccessHash));
    }

    [Test]
    public void HiddenContentsDoNotBlockButHiddenOperatingAccessIsUnsupported()
    {
        var assembly = Assembly() with { Members = Assembly().Members.Append(Member("Stored")).ToArray(),
            Relations = Assembly().Relations.Append(Relation("Inside", "Stored", "Device", KsProcgenRelationKind.InContainer)).ToArray() };
        var plan = Plan(assembly, Room(), turn: 0);
        Assert.That(plan.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate));
        Assert.That(plan.Footprints.Any(footprint => footprint.MemberId == "Stored"), Is.False);
        assembly.Members.Single(member => member.Id == "Stored").Entry.RequiresInteractionApproach = true;
        AssertFailure(Plan(assembly, Room(), turn: 0), "ContainedOperatingAccessUnsupported");
    }

    [Test]
    public void ExhaustedBudgetAndLateMemberFailureNeverReturnPartialAccess()
    {
        var assembly = Assembly();
        var exhausted = Plan(assembly, Room(), turn: 0, maximumExpandedCells: 1);
        Assert.That(exhausted.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.BudgetExceeded));
        Assert.That(exhausted.ExpandedCells, Is.EqualTo(1));
        AssertFailure(exhausted, "SupportedAccessPathBudget");
        assembly = assembly with { Members = assembly.Members.Append(Member("Seat", role: KsProcgenEntityRole.Seat,
            movement: KsProcgenMovementClass.Clear)).ToArray() };
        var blocked = Room() with { Blocking = new HashSet<Vector2i> { new(2, 0), new(1, 1), new(3, 1) } };
        AssertFailure(Plan(assembly, blocked, turn: 3), "SupportedCleanApproachUnavailable");
    }

    [Test]
    public void ReplayIsIndependentOfInputOrderingAndChangesWithTheRoom()
    {
        var assembly = Assembly();
        var room = Room();
        var original = Plan(assembly, room, turn: 0);
        var reversed = Plan(assembly with { Members = assembly.Members.Reverse().ToArray() },
            room with { Floor = room.Floor.Reverse().ToHashSet() }, turn: 0);
        Assert.That(reversed.Access, Is.EqualTo(original.Access));
        Assert.That(reversed.AccessHash, Is.EqualTo(original.AccessHash));
        Assert.That(Plan(assembly, room with { Occupied = new HashSet<Vector2i> { new(4, 4) } }, turn: 0).AccessHash,
            Is.Not.EqualTo(original.AccessHash));
        AssertFailure(Plan(assembly, room with { Network = new HashSet<Vector2i> { new(2, 2) } }, turn: 0),
            "SupportedAccessNetworkBlocked");
        AssertFailure(Plan(assembly, room with { CellCenterOrigin = new(float.NaN, 0f) }, turn: 0), "InvalidSupportedAccessMask");
    }

    private static KsProcgenRoomAccessMask Room() => new(
        Enumerable.Range(0, 5).SelectMany(x => Enumerable.Range(0, 5).Select(y => new Vector2i(x, y))).ToHashSet(),
        new HashSet<Vector2i>(), new HashSet<Vector2i>(), new HashSet<Vector2i>(),
        new HashSet<Vector2i> { new(0, 0) }, Vector2.Zero);

    private static KsProcgenResolvedAssemblyMember Member(string id, KsProcgenEntityRole role = KsProcgenEntityRole.Equipment,
        KsProcgenMovementClass movement = KsProcgenMovementClass.Blocks) =>
        new(id, id, true, new() { Id = id, Entity = id, Role = role, Movement = movement,
            RequiresInteractionApproach = id == "Device" }, KsProcgenMemberRotation.Independent, 0);

    private static KsProcgenResolvedAssemblyRelation Relation(string id, string subject, string? target, KsProcgenRelationKind kind) =>
        new(id, subject, kind, target, KsProcgenRelationSeverity.Required, 1, 4, null,
            kind == KsProcgenRelationKind.InContainer ? "Slot" : null, KsProcgenApproachPolicy.EmptyFloor);

    private static KsProcgenResolvedAssembly Assembly() => new("Fixture", "Base", "Root",
        [Member("Root", movement: KsProcgenMovementClass.VaultRequired), Member("Device")],
        [Relation("Top", "Device", "Root", KsProcgenRelationKind.OnSurface)]);

    private static KsProcgenAssemblyCapabilityReport Report(KsProcgenResolvedAssembly assembly) => new(
        assembly.Members.Select(member => new KsProcgenMemberCapabilityDeclaration(member.Id,
            new(member.Entry.Entity, true, true, true, Vector2.Zero, true, false, false, false, 0.0,
                [], [new("Slot", KsProcgenDeclaredContainerKind.Container, true, false, false, null, 0, 0, null, null)]))).ToArray(), []);

    private static KsProcgenSupportedAccessPlan Plan(KsProcgenResolvedAssembly assembly, KsProcgenRoomAccessMask room,
        int turn, KsProcgenAssemblyCapabilityReport? report = null, int maximumExpandedCells = 4096) =>
        KsProcgenSupportedAccessPlanner.Plan(assembly, assembly.Members.Select(member => member.Id).ToArray(), report ?? Report(assembly),
            assembly.Members.Where(member => !assembly.Relations.Any(relation => relation.Subject == member.Id &&
                relation.Kind is KsProcgenRelationKind.OnSurface or KsProcgenRelationKind.InContainer)).Select(member =>
                new KsProcgenFloorRootPose(member.Id, member.Id == "Seat" ? new(2f, 1f) :
                    member.Id == "Obstacle" ? new(1f, 0f) : new(2f, 2f))).ToArray(),
            assembly.Members.Select(member => new KsProcgenMemberOrientation(member.Id, turn)).ToArray(), 0, room,
            maximumExpandedCells: maximumExpandedCells);

    private static void AssertFailure(KsProcgenSupportedAccessPlan plan, string code)
    {
        Assert.That(plan.Status, Is.Not.EqualTo(KsProcgenSupportedAccessStatus.Candidate));
        Assert.That(plan.Issue?.Code, Is.EqualTo(code));
        Assert.That(plan.Access, Is.Empty);
        Assert.That(plan.Footprints, Is.Empty);
        Assert.That(plan.Poses, Is.Null);
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
