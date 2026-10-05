using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenSupportedSpatialRelationTests
{
    [Test]
    public void CornerTableSupportedMachineAndChairProduceGeometricWitnesses()
    {
        var assembly = Assembly();
        var plan = Plan(assembly);
        Assert.That(plan.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate), plan.Issue?.Code);
        Assert.That(plan.Relations.Count, Is.EqualTo(4));
        Assert.That(plan.Relations.All(witness => witness.State == KsProcgenConstraintState.Satisfied), Is.True);
        Assert.That(plan.Relations.Any(witness => witness.Kind == KsProcgenRelationKind.OnSurface), Is.False);
        var corner = plan.Relations.Single(witness => witness.RelationId == "Corner");
        Assert.That(new[] { corner.FirstBackingWall, corner.SecondBackingWall },
            Is.EquivalentTo(new Vector2i?[] { new(3, 2), new(2, 3) }));
        Assert.That(plan.Access.Single(witness => witness.MemberId == "Device").AssociatedSeatMemberId, Is.EqualTo("Seat"));
        Assert.That(plan.Access.Single(witness => witness.MemberId == "Seat").CleanPath.Cells, Does.Not.Contain(new Vector2i(2, 1)));
        Assert.That(plan.EnginePlacementVerified, Is.False);
    }

    [Test]
    public void RequiredFacingAndCornerFailuresAreAtomicButPreferredMissesAreReported()
    {
        var assembly = Assembly();
        AssertFailure(Plan(assembly, chairTurn: 0), KsProcgenSupportedAccessStatus.Rejected, "SupportedSpatialRelationUnmet");
        AssertFailure(Plan(assembly, room: Room() with { Walls = new HashSet<Vector2i>() }),
            KsProcgenSupportedAccessStatus.Rejected, "SupportedSpatialRelationUnmet");
        assembly = assembly with { Relations = assembly.Relations.Select(relation => relation.Id is "Facing" or "Corner" ?
            relation with { Severity = KsProcgenRelationSeverity.Preferred } : relation).ToArray() };
        var preferred = Plan(assembly, chairTurn: 0, room: Room() with { Walls = new HashSet<Vector2i>() });
        Assert.That(preferred.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate));
        Assert.That(preferred.PreferredRelationsApplicable, Is.EqualTo(2));
        Assert.That(preferred.PreferredRelationsSatisfied, Is.Zero);
        Assert.That(preferred.Relations.Count(witness => witness.State == KsProcgenConstraintState.Missed), Is.EqualTo(2));
    }

    [Test]
    public void SearchBacktracksChairRotationsAndFloorRootsForCornerAndFacing()
    {
        var assembly = Assembly();
        // Keep the machine facing south so the selected chair must be its south landing.
        assembly.Members.Single(member => member.Id == "Device").Entry.AllowedQuarterTurns = [0];
        var result = KsProcgenSupportedAssemblySearch.Search(assembly, ["Root", "Device", "Seat"], Report(assembly), Room(), 34,
            fixedFloorRoots: [new("Root", new(2f, 2f)), new("Seat", new(2f, 1f))]);
        Assert.That(result.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate), result.Issue?.Code);
        Assert.That(result.Orientations.Single(turn => turn.MemberId == "Seat").QuarterTurns, Is.EqualTo(2));
        Assert.That(result.Access!.Relations.All(witness => witness.State == KsProcgenConstraintState.Satisfied), Is.True);
        // Restrict roots to the two explicit candidates: only (2,2) has both required backing walls.
        var cornerOnly = assembly with { Members = assembly.Members.Where(member => member.Id != "Seat").ToArray(),
            Relations = assembly.Relations.Where(relation => relation.Id is "Top" or "Corner").ToArray() };
        var narrow = Room() with { Floor = new HashSet<Vector2i> { new(0, 0), new(1, 0), new(2, 0), new(2, 1), new(2, 2) } };
        var automatic = KsProcgenSupportedAssemblySearch.Search(cornerOnly, ["Root", "Device"], Report(cornerOnly), narrow, 12);
        Assert.That(automatic.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate), automatic.Issue?.Code);
        Assert.That(automatic.FloorRoots.Single().Position, Is.EqualTo(new Vector2(2f, 2f)));
    }

    [Test]
    public void HiddenAndFractionalSpatialEndpointsNeverBecomeRoundedGeometry()
    {
        var assembly = Assembly();
        var report = Report(assembly);
        report = report with { Members = report.Members.Select(member => member.MemberId == "Root" ?
            member with { Capabilities = member.Capabilities with { SurfaceOffset = new(0.25f, 0f) } } : member).ToArray() };
        // Give the fractional device an open front instead of a required chair.
        assembly = assembly with { Relations = assembly.Relations.Where(relation => relation.Id != "Use").Append(
            Relation("DeviceFacing", "Device", "Root", KsProcgenRelationKind.FacingTarget)).ToArray() };
        AssertFailure(Plan(assembly, room: Room() with { Walls = new HashSet<Vector2i>() }, report: report), KsProcgenSupportedAccessStatus.UnsupportedContent,
            "FractionalSpatialRelationUnsupported");
        var hidden = Assembly() with { Members = Assembly().Members.Append(Member("Stored")).ToArray(),
            Relations = Assembly().Relations.Append(Relation("Inside", "Stored", "Device", KsProcgenRelationKind.InContainer)).Append(
                Relation("HiddenAdjacent", "Stored", "Root", KsProcgenRelationKind.AdjacentTo)).ToArray() };
        AssertFailure(Plan(hidden), KsProcgenSupportedAccessStatus.UnsupportedContent, "ContainedSpatialRelationUnsupported");
    }

    [Test]
    public void OmittedOptionalSubjectsAndMissingTargetsKeepTheirDifferentMeanings()
    {
        var assembly = Assembly() with { Members = Assembly().Members.Append(Member("Garnish") with { Required = false }).ToArray(),
            Relations = Assembly().Relations.Append(Relation("OptionalFacing", "Garnish", "Root", KsProcgenRelationKind.FacingTarget)).ToArray() };
        var plan = Plan(assembly, selected: ["Root", "Device", "Seat"]);
        Assert.That(plan.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate));
        Assert.That(plan.Relations.Single(witness => witness.RelationId == "OptionalFacing").State,
            Is.EqualTo(KsProcgenConstraintState.NotApplicable));
        assembly = assembly with { Relations = assembly.Relations.Append(Relation("RequiredTarget", "Root", "Garnish",
            KsProcgenRelationKind.AdjacentTo)).ToArray() };
        AssertFailure(Plan(assembly, selected: ["Root", "Device", "Seat"]), KsProcgenSupportedAccessStatus.Rejected,
            "SupportedSpatialRelationUnmet");
    }

    [Test]
    public void SemanticHashTracksRelationSeverityAndCanonicalizesDeclarationOrder()
    {
        var assembly = Assembly();
        var plan = Plan(assembly);
        var reversed = Plan(assembly with { Members = assembly.Members.Reverse().ToArray(), Relations = assembly.Relations.Reverse().ToArray() });
        Assert.That(reversed.AccessHash, Is.EqualTo(plan.AccessHash));
        Assert.That(reversed.Relations, Is.EqualTo(plan.Relations));
        var changed = Plan(assembly with { Relations = assembly.Relations.Select(relation => relation.Id == "Facing" ?
            relation with { Severity = KsProcgenRelationSeverity.Preferred } : relation).ToArray() });
        Assert.That(changed.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate));
        Assert.That(changed.AccessHash, Is.Not.EqualTo(plan.AccessHash));
        Assert.That(changed.PreferredRelationsSatisfied, Is.EqualTo(1));
    }

    private static KsProcgenRoomAccessMask Room() => new(
        Enumerable.Range(0, 5).SelectMany(x => Enumerable.Range(0, 5).Select(y => new Vector2i(x, y))).ToHashSet(),
        new HashSet<Vector2i> { new(3, 2), new(2, 3) }, new HashSet<Vector2i>(), new HashSet<Vector2i>(),
        new HashSet<Vector2i> { new(0, 0) }, Vector2.Zero);

    private static KsProcgenResolvedAssemblyMember Member(string id) => new(id, id, true,
        new() { Id = id, Entity = id, Role = id == "Seat" ? KsProcgenEntityRole.Seat : KsProcgenEntityRole.Equipment,
            Movement = id == "Seat" ? KsProcgenMovementClass.Clear : KsProcgenMovementClass.VaultRequired,
            RequiresInteractionApproach = id == "Device" }, KsProcgenMemberRotation.Independent, 0);

    private static KsProcgenResolvedAssemblyRelation Relation(string id, string subject, string? target, KsProcgenRelationKind kind) =>
        new(id, subject, kind, target, KsProcgenRelationSeverity.Required, 1, 4, null,
            kind == KsProcgenRelationKind.InContainer ? "Slot" : null, KsProcgenApproachPolicy.EmptyFloor);

    private static KsProcgenResolvedAssembly Assembly() => new("SpatialFixture", "Base", "Root",
        [Member("Root"), Member("Device"), Member("Seat")],
        [Relation("Top", "Device", "Root", KsProcgenRelationKind.OnSurface),
            Relation("Corner", "Root", null, KsProcgenRelationKind.AtCorner),
            Relation("Adjacent", "Seat", "Root", KsProcgenRelationKind.AdjacentTo),
            Relation("Facing", "Seat", "Root", KsProcgenRelationKind.FacingTarget),
            Relation("Use", "Device", "Seat", KsProcgenRelationKind.UsesSeat)]);

    private static KsProcgenAssemblyCapabilityReport Report(KsProcgenResolvedAssembly assembly) => new(
        assembly.Members.Select(member => new KsProcgenMemberCapabilityDeclaration(member.Id,
            new(member.Entry.Entity, true, true, true, Vector2.Zero, true, false, false, false, 0.0,
                [], [new("Slot", KsProcgenDeclaredContainerKind.Container, true, false, false, null, 0, 0, null, null)]))).ToArray(), []);

    private static KsProcgenSupportedAccessPlan Plan(KsProcgenResolvedAssembly assembly, int chairTurn = 2,
        KsProcgenRoomAccessMask? room = null, KsProcgenAssemblyCapabilityReport? report = null, string[]? selected = null) =>
        KsProcgenSupportedAccessPlanner.Plan(assembly, selected ?? assembly.Members.Select(member => member.Id).ToArray(), report ?? Report(assembly),
            [new("Root", new(2f, 2f)), new("Seat", new(2f, 1f))],
            assembly.Members.Where(member => selected == null || selected.Contains(member.Id)).Select(member =>
                new KsProcgenMemberOrientation(member.Id, member.Id == "Seat" ? chairTurn : 0)).ToArray(), 0, room ?? Room());

    private static void AssertFailure(KsProcgenSupportedAccessPlan plan, KsProcgenSupportedAccessStatus status, string code)
    {
        Assert.That(plan.Status, Is.EqualTo(status), plan.Issue?.Code);
        Assert.That(plan.Issue?.Code, Is.EqualTo(code));
        Assert.That(plan.Relations, Is.Empty);
        Assert.That(plan.Access, Is.Empty);
        Assert.That(plan.Poses, Is.Null);
        Assert.That(plan.AccessHash, Is.Zero);
    }
}
