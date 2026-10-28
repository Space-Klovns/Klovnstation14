#nullable enable
using System.Linq;
using Content.Server._KS14.Procedural;
using Content.Shared._KS14.Procedural;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

public sealed partial class KsProcgenRoomMapCaptureTests
{
    [TestCase("L", 0)] [TestCase("Four", 0)] [TestCase("L", 1)] [TestCase("Four", 1)]
    public async Task NativeSourcePoseEligibilityAutomaticallySelectsAnEntireCompatibleAlternative(string compatible, int familyTurns)
    {
        var yaml = CopySource().Replace("- proto: Table", "- proto: ''").Replace("rot: 90", "rot: 0")
            .Replace("noRot: false", "noRot: true");
        await AssertOnServer(() => WithSource(yaml, (capture, project, bindings, root) =>
        {
            var spec = LayoutProjectSpec(project, root.Bytes); var family = spec.Layouts!.Families["Choice"];
            family.AllowedQuarterTurns = new() { familyTurns };
            foreach (var member in family.Options.Single(option => option.Id == compatible).Members)
                member.QuarterTurns = (4 - familyTurns) % 4;
            project = Project(spec); var source = Capture(capture, project, bindings);
            Assert.That(capture.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.Code);
            root.Available = false;
            WithRoomCopyTransaction((gridUid, allocations) =>
            {
                // Prefer the incompatible option to prove source eligibility changes the selection.
                Assert.That(Pair.Server.System<KsProcgenLayoutInstantiationSystem>().TryInstantiate(project, "Area", source, extraction!,
                    gridUid, allocations, 32, out var result, out issue,
                    budgets: new() { Packing = new() { PreferredPrefabCoveragePercent = compatible == "L" ? 100 : 75 } }),
                    Is.True, message: issue?.ToString());
                Assert.That(result!.Packing.Placements, Has.Count.EqualTo(1));
                Assert.That(result.Packing.Placements[0].OptionId, Is.EqualTo(compatible));
                Assert.That(result.Packing.Placements[0].QuarterTurns, Is.EqualTo(familyTurns));
                Assert.That(result.Rooms, Has.Count.EqualTo(compatible == "L" ? 3 : 4));
                Assert.That(result.Rooms.Select(room => room.OptionId).Distinct(), Is.EqualTo(new[] { compatible }));
                Assert.That(result.Eligibility.Rejected, Has.Count.EqualTo(1));
                Assert.That(result.Eligibility.Rejected[0].Orientation.OptionId, Is.Not.EqualTo(compatible));
                Assert.That(result.Eligibility.Rejected[0].Reason, Is.EqualTo("RoomInstantiationRotationUnsupported"));
                foreach (var room in result.Rooms)
                {
                    var transform = SEntMan.GetComponent<TransformComponent>(room.Instance.Members[10]);
                    Assert.That(transform.NoLocalRotation, Is.True);
                    Assert.That(transform.LocalRotation.Theta, Is.Zero.Within(0.000001));
                    Assert.That(SEntMan.GetComponent<KsProcgenMapReferenceProbeComponent>(room.Instance.Members[10]).Target,
                        Is.EqualTo(room.Instance.Members[2]));
                }
                Assert.That(result.GeneratedCells.Count, Is.EqualTo(compatible == "L" ? 1 : 0));
            }, layFloor: false);
        }));
    }

    [TestCase("exact")] [TestCase("orientations")] [TestCase("members")]
    public async Task SourceEligibilityMetersUnselectedAlternativesAndRejectsBeforeWritingWhenItsSharedBudgetRunsOut(string budget)
    {
        await AssertOnServer(() => WithSource(CopySource().Replace("- proto: Table", "- proto: ''"), (capture, project, bindings, root) =>
        {
            var spec = LayoutProjectSpec(project, root.Bytes); spec.Layouts!.Families["Choice"].AllowedQuarterTurns = new() { 0 };
            project = Project(spec); var source = Capture(capture, project, bindings);
            Assert.That(capture.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.Code);
            WithRoomCopyTransaction((gridUid, allocations) =>
            {
                var accepted = Pair.Server.System<KsProcgenLayoutInstantiationSystem>().TryInstantiate(project, "Area", source, extraction!,
                    gridUid, allocations, 32, out var result, out issue, budgets: new()
                    {
                        Packing = new() { PreferredPrefabCoveragePercent = 75 },
                        MaximumEligibilityOrientationProbes = budget == "orientations" ? 1 : 2,
                        MaximumEligibilityMemberProbes = budget == "members" ? 13 : 14,
                    });
                Assert.That(accepted, Is.EqualTo(budget == "exact"), message: issue?.ToString());
                if (accepted)
                {
                    Assert.That(result!.Eligibility.OrientationProbes, Is.EqualTo(2));
                    Assert.That(result.Eligibility.MemberProbes, Is.EqualTo(14));
                }
                else
                {
                    Assert.That(result, Is.Null);
                    Assert.That(issue?.Code, Is.EqualTo(budget == "orientations" ? "LayoutEligibilityOrientationBudget" : "LayoutEligibilityMemberBudget"));
                    Assert.That(allocations.Count, Is.EqualTo(2));
                    Assert.That(SEntMan.GetComponent<MapGridComponent>(gridUid).ChunkCount, Is.Zero);
                }
            }, layFloor: false);
        }));
    }

    [TestCase(KsProcgenMode.Prefabs)] [TestCase(KsProcgenMode.Hybrid)]
    public async Task AllIncompatibleNativeAlternativesNeverAllocateTheirMembersAndKeepResidualsExplicit(KsProcgenMode mode)
    {
        var yaml = CopySource().Replace("- proto: Table", "- proto: ''").Replace("anchored: false", "anchored: true");
        await AssertOnServer(() => WithSource(yaml, (capture, project, bindings, root) =>
        {
            var spec = LayoutProjectSpec(project, root.Bytes); spec.Layouts!.Families["Choice"].AllowedQuarterTurns = new() { 0 };
            spec.Areas["Area"].Request.Mode = mode;
            project = Project(spec); var source = Capture(capture, project, bindings);
            Assert.That(capture.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.Code);
            WithRoomCopyTransaction((gridUid, allocations) =>
            {
                var accepted = Pair.Server.System<KsProcgenLayoutInstantiationSystem>().TryInstantiate(project, "Area", source, extraction!,
                    gridUid, allocations, 32, out var result, out issue);
                Assert.That(accepted, Is.EqualTo(mode == KsProcgenMode.Hybrid), message: issue?.Code);
                Assert.That(allocations.Count, Is.EqualTo(2));
                Assert.That(SEntMan.GetComponent<MapGridComponent>(gridUid).ChunkCount, Is.Zero);
                if (accepted)
                {
                    Assert.That(result!.Rooms, Is.Empty); Assert.That(result.GeneratedCells, Has.Count.EqualTo(4));
                    Assert.That(result.Eligibility.Rejected, Has.Count.EqualTo(2));
                    Assert.That(result.Eligibility.Rejected.All(item => item.Reason == "RoomInstantiationAnchoredPoseUnsupported"), Is.True);
                }
                else { Assert.That(result, Is.Null); Assert.That(issue?.Code, Is.EqualTo("NoGeometricCover")); }
            }, layFloor: false);
        }));
    }

    [Test]
    public async Task ADeclaredGenerateOnlyAlternativeNeedsNoFabricatedSourceRoomBinding()
    {
        await AssertOnServer(() => WithSource(CopySource().Replace("- proto: Table", "- proto: ''"), (capture, project, bindings, root) =>
        {
            var spec = LayoutProjectSpec(project, root.Bytes); var family = spec.Layouts!.Families["Choice"];
            var option = family.Options[0]; option.Id = "GenerateOnly";
            option.GenerateCells = option.ReservationCells.ToList(); option.Members.Clear();
            family.Options = new() { option }; family.AllowedQuarterTurns = new() { 0 };
            spec.Areas["Area"].Request.Mode = KsProcgenMode.Prefabs;
            project = Project(spec); var source = Capture(capture, project, bindings);
            Assert.That(capture.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.Code);
            WithRoomCopyTransaction((gridUid, allocations) =>
            {
                Assert.That(Pair.Server.System<KsProcgenLayoutInstantiationSystem>().TryInstantiate(project, "Area", source, extraction!,
                    gridUid, allocations, 32, out var result, out issue,
                    budgets: new() { MaximumEligibilityMemberProbes = 0 }), Is.True, message: issue?.ToString());
                Assert.That(result!.Packing.Placements.Single().OptionId, Is.EqualTo("GenerateOnly"));
                Assert.That(result.Rooms, Is.Empty); Assert.That(result.GeneratedCells, Has.Count.EqualTo(4));
                Assert.That(result.Eligibility.MemberProbes, Is.Zero); Assert.That(allocations.Count, Is.EqualTo(2));
                Assert.That(SEntMan.GetComponent<MapGridComponent>(gridUid).ChunkCount, Is.Zero);
            }, layFloor: false);
        }));
    }
}
