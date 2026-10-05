using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenAssemblyPosePlannerTests
{
    [Test]
    public void SurfaceOffsetsUseCommonFrameRatherThanRotatedParentAxes()
    {
        var assembly = Assembly();
        var report = Report(assembly);
        foreach (var turn in Enumerable.Range(0, 4))
        {
            var plan = Plan(assembly, report, turn);
            Assert.That(plan.Status, Is.EqualTo(KsProcgenAssemblySupportStatus.NeedsEngineValidation));
            var child = plan.Members.Single(pose => pose.Support.MemberId == "Child");
            Assert.That(child.Position, Is.EqualTo(new Vector2(2.25f, 3.5f)));
            Assert.That(child.QuarterTurns, Is.EqualTo(turn));
            Assert.That(child.CoordinateParentMemberId, Is.Null);
            Assert.That(child.LocalPosition, Is.EqualTo(child.Position));
            Assert.That(child.Support.ParentMemberId, Is.EqualTo("Root"));
            Assert.That(plan.EnginePlacementVerified, Is.False);
        }
    }

    [Test]
    public void NoncenteredSurfaceChoosesOriginClickAndIgnoresUnusedOffset()
    {
        var assembly = Assembly();
        var report = Report(assembly);
        report = report with { Members = report.Members.Select(member => member with
            { Capabilities = member.Capabilities with { SurfaceCentered = false, SurfaceOffset = new(float.NaN, 100f) } }).ToArray() };
        Assert.That(Plan(assembly, report, 0).Members.Last().Position, Is.EqualTo(new Vector2(2f, 3f)));
    }

    [Test]
    public void NestedSurfaceAndContainerPosesPreserveInsertionFrameAndExposure()
    {
        var assembly = Assembly() with { Members = [Member("Root"), Member("Child"), Member("Stored")],
            Relations = [Relation("Top", "Child", "Root", KsProcgenRelationKind.OnSurface),
                Relation("Slot", "Stored", "Child", KsProcgenRelationKind.InContainer)] };
        var plan = Plan(assembly, Report(assembly), 3);
        Assert.That(plan.Status, Is.EqualTo(KsProcgenAssemblySupportStatus.NeedsEngineValidation));
        Assert.That(plan.Members.Select(pose => pose.Support.MemberId), Is.EqualTo(new[] { "Root", "Child", "Stored" }));
        var stored = plan.Members.Last();
        Assert.That(stored.Position, Is.EqualTo(plan.Members[1].Position));
        Assert.That(stored.QuarterTurns, Is.EqualTo(3));
        Assert.That(stored.LocalPosition, Is.EqualTo(Vector2.Zero));
        Assert.That(stored.LocalQuarterTurns, Is.Zero);
        Assert.That(stored.CoordinateParentMemberId, Is.EqualTo("Child"));
        Assert.That(stored.Support.Exposed, Is.False);
    }

    [Test]
    public void InsertionCannotHonorIndependentWorldRotationThatDiffersFromParent()
    {
        var assembly = Assembly(kind: KsProcgenRelationKind.InContainer);
        var report = Report(assembly);
        AssertRejected(KsProcgenAssemblyPosePlanner.Plan(assembly, ["Root", "Child"], report,
            [new("Root", Vector2.Zero)], [new("Root", 1), new("Child", 2)], 0),
            "AssemblyPoseContainerRotationConflict");
    }

    [Test]
    public void AuthoredRotationRestrictionsAndRelativeOffsetsAreEnforced()
    {
        var assembly = Assembly();
        assembly = assembly with { Members = [assembly.Members[0], assembly.Members[1] with
            { RotationMode = KsProcgenMemberRotation.AssemblyRelative, LocalQuarterTurns = 1 }] };
        var report = Report(assembly);
        AssertRejected(Plan(assembly, report, 0), "AssemblyPoseRotationNotAllowed");
        var valid = KsProcgenAssemblyPosePlanner.Plan(assembly, ["Root", "Child"], report,
            [new("Root", Vector2.Zero)], [new("Root", 3), new("Child", 0)], 3);
        Assert.That(valid.Status, Is.EqualTo(KsProcgenAssemblySupportStatus.NeedsEngineValidation));
        assembly.Members[1].Entry.AllowedQuarterTurns = [1];
        AssertRejected(KsProcgenAssemblyPosePlanner.Plan(assembly, ["Root", "Child"], report,
            [new("Root", Vector2.Zero)], [new("Root", 3), new("Child", 0)], 3), "AssemblyPoseRotationNotAllowed");
    }

    [Test]
    public void InvalidInputsAndPartialSelectionsNeverProducePoses()
    {
        var assembly = Assembly();
        var report = Report(assembly);
        foreach (var roots in new IReadOnlyList<KsProcgenFloorRootPose>[]
                 { [], [new("Root", new(float.NaN, 0f))], [new("Root", Vector2.Zero), new("Root", Vector2.Zero)] })
            AssertRejected(KsProcgenAssemblyPosePlanner.Plan(assembly, ["Root", "Child"], report,
                roots, [new("Root", 0), new("Child", 0)], 0), "InvalidAssemblyPoseInput");
        AssertRejected(KsProcgenAssemblyPosePlanner.Plan(assembly, ["Root", "Child"], report,
            [new("Child", Vector2.Zero)], [new("Root", 0), new("Child", 0)], 0), "AssemblyPoseSelectionMismatch");
        AssertRejected(KsProcgenAssemblyPosePlanner.Plan(assembly, ["Root", "Child"], report,
            [new("Root", Vector2.Zero)], [new("Root", 0)], 0), "AssemblyPoseSelectionMismatch");
        AssertRejected(KsProcgenAssemblyPosePlanner.Plan(assembly, ["Root"], report,
            [new("Root", Vector2.Zero)], [new("Root", 0)], 0), "InvalidSupportSelection");
    }

    [Test]
    public void UnsupportedSurfaceCasesAndOutOfBoundsOffsetsRejectAtomically()
    {
        var assembly = Assembly();
        var report = Report(assembly);
        foreach (var offset in new[] { new Vector2(float.PositiveInfinity, 0f), new Vector2(17f, 0f) })
        {
            var changed = report with { Members = report.Members.Select(member => member with
                { Capabilities = member.Capabilities with { SurfaceOffset = offset } }).ToArray() };
            AssertRejected(Plan(assembly, changed, 0), "AssemblyPoseSurfaceOffsetOutOfRange");
        }
        AssertRejected(Plan(assembly with { Relations = [assembly.Relations[0] with { Slot = "Top" }] }, report, 0),
            "AssemblyPoseNamedSurfaceUnsupported");
        report = report with { Members = report.Members.Select(member => member.MemberId == "Child" ? member with
            { Capabilities = member.Capabilities with { InitiallyAnchored = true } } : member).ToArray() };
        Assert.That(Plan(assembly, report, 0).Status, Is.EqualTo(KsProcgenAssemblySupportStatus.NeedsEngineValidation));
        report = report with { Members = report.Members.Select(member => member.MemberId == "Child" ? member with
            { Capabilities = member.Capabilities with { HasItem = false } } : member).ToArray() };
        AssertRejected(Plan(assembly, report, 0), "AssemblyPoseSurfaceSubjectUnsupported");
        var hidden = Assembly(kind: KsProcgenRelationKind.InContainer) with
        {
            Members = [Member("Root"), Member("Child"), Member("Hidden")],
            Relations = [Relation("Inside", "Child", "Root", KsProcgenRelationKind.InContainer),
                Relation("Top", "Hidden", "Child", KsProcgenRelationKind.OnSurface)],
        };
        AssertRejected(Plan(hidden, Report(hidden), 0), "AssemblyPoseContainedSurfaceUnsupported");
    }

    [Test]
    public void ReplayHashIgnoresEnumerationAndChangesWithTransforms()
    {
        var assembly = Assembly();
        var report = Report(assembly);
        var original = Plan(assembly, report, 2);
        var reversed = KsProcgenAssemblyPosePlanner.Plan(assembly with { Members = assembly.Members.Reverse().ToArray() },
            ["Child", "Root"], report with { Members = report.Members.Reverse().ToArray() },
            [new("Root", new(2f, 3f))], [new("Child", 2), new("Root", 2)], 0);
        Assert.That(reversed.Members, Is.EqualTo(original.Members));
        Assert.That(reversed.PoseHash, Is.EqualTo(original.PoseHash));
        Assert.That(Plan(assembly, report, 1).PoseHash, Is.Not.EqualTo(original.PoseHash));
        Assert.That(KsProcgenAssemblyPosePlanner.Plan(assembly, ["Root", "Child"], report,
            [new("Root", new(3f, 3f))], [new("Root", 2), new("Child", 2)], 0).PoseHash,
            Is.Not.EqualTo(original.PoseHash));
    }

    private static KsProcgenResolvedAssemblyMember Member(string id) =>
        new(id, id, true, new() { Id = id, Entity = id }, KsProcgenMemberRotation.Independent, 0);

    private static KsProcgenResolvedAssemblyRelation Relation(string id, string subject, string target, KsProcgenRelationKind kind) =>
        new(id, subject, kind, target, KsProcgenRelationSeverity.Required, 1, 4, null,
            kind == KsProcgenRelationKind.InContainer ? "Slot" : null, KsProcgenApproachPolicy.EmptyFloor);

    private static KsProcgenResolvedAssembly Assembly(KsProcgenRelationKind kind = KsProcgenRelationKind.OnSurface) =>
        new("Fixture", "Base", "Root", [Member("Root"), Member("Child")], [Relation("Support", "Child", "Root", kind)]);

    private static KsProcgenAssemblyCapabilityReport Report(KsProcgenResolvedAssembly assembly) => new(
        assembly.Members.Select(member => new KsProcgenMemberCapabilityDeclaration(member.Id,
            new(member.Entry.Entity, true, true, true, new(0.25f, 0.5f), true, false, false, false, 0.0,
                [], [new("Slot", KsProcgenDeclaredContainerKind.Container, true, false, false, null, 0, 0, null, null)]))).ToArray(), []);

    private static KsProcgenAssemblyPosePlan Plan(KsProcgenResolvedAssembly assembly,
        KsProcgenAssemblyCapabilityReport report, int turn) => KsProcgenAssemblyPosePlanner.Plan(assembly,
        assembly.Members.Select(member => member.Id).ToArray(), report, [new("Root", new(2f, 3f))],
        assembly.Members.Select(member => new KsProcgenMemberOrientation(member.Id, turn)).ToArray(), 0);

    private static void AssertRejected(KsProcgenAssemblyPosePlan plan, string code)
    {
        Assert.That(plan.Status, Is.Not.EqualTo(KsProcgenAssemblySupportStatus.NeedsEngineValidation));
        Assert.That(plan.Issue?.Code, Is.EqualTo(code));
        Assert.That(plan.Members, Is.Empty);
        Assert.That(plan.SupportPlan, Is.Null);
        Assert.That(plan.PoseHash, Is.Zero);
    }
}
