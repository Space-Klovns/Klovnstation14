using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenAssemblySupportPlannerTests
{
    [Test]
    public void NestedLayersOrderParentsFirstAndPropagateContainment()
    {
        var assembly = Assembly([Member("ZParent"), Member("BDevice"), Member("AItem"), Member("CChild")],
            [Relation("Surface", "BDevice", "ZParent", KsProcgenRelationKind.OnSurface),
                Relation("Stored", "AItem", "BDevice", KsProcgenRelationKind.InContainer),
                Relation("Nested", "CChild", "AItem", KsProcgenRelationKind.OnSurface)]);
        var report = Report(assembly);
        var plan = Plan(assembly, report);
        Assert.That(plan.Status, Is.EqualTo(KsProcgenAssemblySupportStatus.NeedsEngineValidation));
        Assert.That(plan.Members.Select(member => member.MemberId), Is.EqualTo(new[] { "ZParent", "BDevice", "AItem", "CChild" }));
        Assert.That(plan.Members.Select(member => member.Depth), Is.EqualTo(new[] { 0, 1, 2, 3 }));
        Assert.That(plan.Members.Select(member => member.Layer), Is.EqualTo(new[]
            { KsProcgenPlacementLayer.Floor, KsProcgenPlacementLayer.Surface, KsProcgenPlacementLayer.Container, KsProcgenPlacementLayer.Surface }));
        Assert.That(plan.Members.Select(member => member.Exposed), Is.EqualTo(new[] { true, true, false, false }));
        Assert.That(plan.Members.All(member => member.FloorRootMemberId == "ZParent"), Is.True);
        Assert.That(plan.EnginePlacementVerified, Is.False);
        Assert.That(plan.SupportRelations.Count, Is.EqualTo(3));
        Assert.That(plan.SupportRelations.All(witness => witness.Inspection.State == KsProcgenSupportInspectionState.Unverified), Is.True);
    }

    [Test]
    public void KnownSlotConflictRejectsAtomicallyButOptionalOmissionReleasesClaim()
    {
        var assembly = Assembly([Member("ZParent"), Member("A"), Member("B", required: false)],
            [Relation("First", "A", "ZParent", KsProcgenRelationKind.InContainer),
                Relation("Second", "B", "ZParent", KsProcgenRelationKind.InContainer)]);
        var report = Report(assembly, slotCapacity: true);
        AssertRejected(Plan(assembly, report), "AssemblySupportCapacityConflict");
        var selected = KsProcgenAssemblySupportPlanner.Plan(assembly, ["ZParent", "A"], report);
        Assert.That(selected.Status, Is.EqualTo(KsProcgenAssemblySupportStatus.NeedsEngineValidation));
        Assert.That(selected.Reservations.Single().ClaimCount, Is.EqualTo(1));
        Assert.That(selected.Reservations.Single().DeclaredCapacity, Is.EqualTo(1));
        Assert.That(selected.SupportRelations.Single().SubjectMemberId, Is.EqualTo("A"));
        AssertRejected(Plan(assembly, report), "AssemblySupportCapacityConflict");
    }

    [Test]
    public void SurfaceTrackerReservationsIncludeExistingClaimsButDoNotInferNamedSlots()
    {
        var assembly = Assembly([Member("ZParent"), Member("A"), Member("B")],
            [Relation("First", "A", "ZParent", KsProcgenRelationKind.OnSurface),
                Relation("Second", "B", "ZParent", KsProcgenRelationKind.OnSurface)]);
        var report = Report(assembly);
        report = report with { Members = report.Members.Select(member => member.MemberId == "ZParent" ?
            member with { Capabilities = member.Capabilities with { SurfaceTracker = new(3, 2, null) } } : member).ToArray() };
        AssertRejected(Plan(assembly, report), "AssemblySupportCapacityConflict");
        report = report with { Members = report.Members.Select(member => member.MemberId == "ZParent" ?
            member with { Capabilities = member.Capabilities with { SurfaceTracker = new(4, 2, null) } } : member).ToArray() };
        var fitting = Plan(assembly, report);
        Assert.That(fitting.Status, Is.EqualTo(KsProcgenAssemblySupportStatus.NeedsEngineValidation));
        Assert.That(fitting.Reservations.Single().DeclaredCapacity, Is.EqualTo(4));
        Assert.That(fitting.Reservations.Single().ClaimCount, Is.EqualTo(2));
        Assert.That(fitting.EnginePlacementVerified, Is.False);
        var named = assembly with { Relations = assembly.Relations.Select(relation => relation with { Slot = "Top" }).ToArray() };
        Assert.That(Plan(named, report).Reservations.Single().DeclaredCapacity, Is.Null);
    }

    [Test]
    public void UnknownCapacityNeverBecomesOneAndSurfaceAndContainerClaimsAreSeparate()
    {
        var assembly = Assembly([Member("ZParent"), Member("A"), Member("B"), Member("C")],
            [Relation("First", "A", "ZParent", KsProcgenRelationKind.InContainer),
                Relation("Second", "B", "ZParent", KsProcgenRelationKind.InContainer),
                Relation("Top", "C", "ZParent", KsProcgenRelationKind.OnSurface) with { Slot = "Slot" }]);
        var plan = Plan(assembly, Report(assembly));
        Assert.That(plan.Status, Is.EqualTo(KsProcgenAssemblySupportStatus.NeedsEngineValidation));
        Assert.That(plan.Reservations.Count, Is.EqualTo(2));
        Assert.That(plan.Reservations.Single(claim => claim.Layer == KsProcgenPlacementLayer.Container).ClaimCount, Is.EqualTo(2));
        Assert.That(plan.Reservations.All(claim => claim.DeclaredCapacity == null), Is.True);
        Assert.That(plan.SupportRelations.Single(witness => witness.Kind == KsProcgenRelationKind.OnSurface)
            .Inspection.Reason, Is.EqualTo("NamedSurfaceSlotUnverified"));
    }

    [Test]
    public void SelectedOptionalChildRequiresItsOptionalParent()
    {
        var assembly = Assembly([Member("ZParent"), Member("A", required: false), Member("B", required: false)],
            [Relation("Top", "B", "A", KsProcgenRelationKind.OnSurface)]);
        AssertRejected(KsProcgenAssemblySupportPlanner.Plan(assembly, ["ZParent", "B"], Report(assembly)),
            "MissingSelectedSupportParent");
        var omitted = KsProcgenAssemblySupportPlanner.Plan(assembly, ["ZParent"], Report(assembly));
        Assert.That(omitted.Status, Is.EqualTo(KsProcgenAssemblySupportStatus.NeedsEngineValidation));
        Assert.That(omitted.Reservations, Is.Empty);
        Assert.That(omitted.SupportRelations, Is.Empty);
    }

    [Test]
    public void SupportCyclesAndMultipleParentsRejectWithoutPartialRecords()
    {
        var assembly = Assembly([Member("ZParent"), Member("A")],
            [Relation("Top", "A", "ZParent", KsProcgenRelationKind.OnSurface),
                Relation("Cycle", "ZParent", "A", KsProcgenRelationKind.OnSurface)]);
        AssertRejected(Plan(assembly, Report(assembly)), "AssemblySupportCycle");
        assembly = assembly with { Relations = [assembly.Relations[0],
            Relation("Other", "A", "ZParent", KsProcgenRelationKind.InContainer)] };
        AssertRejected(Plan(assembly, Report(assembly)), "InvalidSupportDependency");
    }

    [Test]
    public void KnownDeclarationRejectionsCannotBeOverriddenByOldWitnesses()
    {
        var assembly = Assembly([Member("ZParent"), Member("A")],
            [Relation("Stored", "A", "ZParent", KsProcgenRelationKind.InContainer)]);
        var report = Report(assembly, slotCapacity: true);
        report = report with { SupportRelations = [new KsProcgenAssemblyCapabilityWitness("Stored", "A", "ZParent",
            KsProcgenRelationKind.InContainer, KsProcgenSupportCapabilityInspector.Inspect(
                report.Members[1].Capabilities, report.Members[0].Capabilities,
                KsProcgenRelationKind.InContainer, slotId: "Slot"))] };
        var parent = report.Members.Single(member => member.MemberId == "ZParent");
        report = report with { Members = [parent with { Capabilities = parent.Capabilities with
            { Containers = [parent.Capabilities.Containers[0] with { Locked = true }] } }, report.Members[1]] };
        AssertRejected(Plan(assembly, report), "ItemSlotInitiallyLocked");
    }

    [Test]
    public void StructureHashAndOrderIgnoreEnumerationButTrackSelectionAndDeclarations()
    {
        var assembly = Assembly([Member("ZParent"), Member("A"), Member("B", required: false)],
            [Relation("Top", "A", "ZParent", KsProcgenRelationKind.OnSurface),
                Relation("Stored", "B", "ZParent", KsProcgenRelationKind.InContainer)]);
        var report = Report(assembly);
        var original = Plan(assembly, report);
        var reordered = KsProcgenAssemblySupportPlanner.Plan(assembly with
            { Members = assembly.Members.Reverse().ToArray(), Relations = assembly.Relations.Reverse().ToArray() },
            ["B", "A", "ZParent"], report with { Members = report.Members.Reverse().ToArray() });
        Assert.That(reordered.Members, Is.EqualTo(original.Members));
        Assert.That(reordered.Reservations, Is.EqualTo(original.Reservations));
        Assert.That(reordered.SupportRelations, Is.EqualTo(original.SupportRelations));
        Assert.That(reordered.StructureHash, Is.EqualTo(original.StructureHash));
        Assert.That(KsProcgenAssemblySupportPlanner.Plan(assembly, ["ZParent", "A"], report).StructureHash,
            Is.Not.EqualTo(original.StructureHash));
        var changed = report with { Members = report.Members.Select(member => member with
            { Capabilities = member.Capabilities with { SurfaceOffset = new Vector2(0.25f, 0f) } }).ToArray() };
        Assert.That(Plan(assembly, changed).StructureHash, Is.Not.EqualTo(original.StructureHash));
    }

    [Test]
    public void InvalidSelectionsReportsAndBudgetsFailWithoutClaims()
    {
        var assembly = Assembly([Member("ZParent"), Member("A")], []);
        var report = Report(assembly);
        foreach (var selection in new[] { new[] { "ZParent" }, new[] { "ZParent", "A", "Unknown" }, new[] { "ZParent", "A", "A" } })
            AssertRejected(KsProcgenAssemblySupportPlanner.Plan(assembly, selection, report), "InvalidSupportSelection");
        AssertRejected(Plan(assembly, report with { Members = [report.Members[0], report.Members[0]] }), "InvalidSupportAssembly");
        AssertRejected(Plan(assembly, report with { Members = [report.Members[0], report.Members[1] with
            { Capabilities = report.Members[1].Capabilities with { PrototypeId = "Other" } }] }), "SupportCapabilityMismatch");
        AssertRejected(KsProcgenAssemblySupportPlanner.Plan(assembly, Enumerable.Repeat("A", 65).ToArray(), report), "InvalidSupportAssembly");
    }

    private static KsProcgenResolvedAssemblyMember Member(string id, bool required = true) =>
        new(id, id, required, new KsProcgenEntityEntry { Id = id, Entity = id }, KsProcgenMemberRotation.Independent, 0);

    private static KsProcgenResolvedAssemblyRelation Relation(string id, string subject, string target, KsProcgenRelationKind kind) =>
        new(id, subject, kind, target, KsProcgenRelationSeverity.Required, 1, 4, null,
            kind == KsProcgenRelationKind.InContainer ? "Slot" : null, KsProcgenApproachPolicy.EmptyFloor);

    private static KsProcgenResolvedAssembly Assembly(IReadOnlyList<KsProcgenResolvedAssemblyMember> members,
        IReadOnlyList<KsProcgenResolvedAssemblyRelation> relations) => new("Fixture", "Base", "ZParent", members, relations);

    private static KsProcgenAssemblyCapabilityReport Report(KsProcgenResolvedAssembly assembly, bool slotCapacity = false) => new(
        assembly.Members.Select(member => new KsProcgenMemberCapabilityDeclaration(member.Id,
            new KsProcgenPrototypeCapabilities(member.Entry.Entity, true, true, false, Vector2.Zero, false,
                false, false, false, 0.0, [], [new KsProcgenContainerDeclaration("Slot",
                    slotCapacity ? KsProcgenDeclaredContainerKind.Slot : KsProcgenDeclaredContainerKind.Container,
                    true, slotCapacity, false, null, 0, 0, null, null)]))).ToArray(), []);

    private static KsProcgenAssemblySupportPlan Plan(KsProcgenResolvedAssembly assembly, KsProcgenAssemblyCapabilityReport report) =>
        KsProcgenAssemblySupportPlanner.Plan(assembly, assembly.Members.Select(member => member.Id).ToArray(), report);

    private static void AssertRejected(KsProcgenAssemblySupportPlan plan, string code)
    {
        Assert.That(plan.Status, Is.Not.EqualTo(KsProcgenAssemblySupportStatus.NeedsEngineValidation));
        Assert.That(plan.Issue?.Code, Is.EqualTo(code));
        Assert.That(plan.Members, Is.Empty);
        Assert.That(plan.Reservations, Is.Empty);
        Assert.That(plan.SupportRelations, Is.Empty);
        Assert.That(plan.StructureHash, Is.Zero);
    }
}
