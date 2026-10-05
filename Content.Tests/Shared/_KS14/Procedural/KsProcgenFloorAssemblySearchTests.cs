using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenFloorAssemblySearchTests
{
    [Test]
    public void EarlierMemberPositionIsRevisitedWhenItOccupiesALaterRequiredLanding()
    {
        var members = new List<KsProcgenAssemblyMember>
        {
            Member("A"), Member("B"), Member("C"),
        };
        members[1].Footprint = [new(0, 0), new(1, 0)];
        var search = Search(members,
            [new() { Id = "Beside", Subject = "C", Target = "A", Kind = KsProcgenRelationKind.AdjacentTo }],
            Rectangle(6, 1));
        Assert.That(search.ExploreAnchor(new(1, 0)), Is.True);
        Assert.That(search.Exhausted, Is.False);
        Assert.That(search.Accepted.Single(entity => entity.EntryId == "C").Cell, Is.EqualTo(new Vector2i(2, 0)));
        Assert.That(search.Accepted.Single(entity => entity.EntryId == "B").Cell, Is.EqualTo(new Vector2i(3, 0)));
        Assert.That(search.Backtracks, Is.GreaterThan(0));
        Assert.That(search.Accepted.SelectMany(entity => entity.OccupiedCells).Distinct().Count(), Is.EqualTo(4));
    }

    [Test]
    public void EarlierIndependentRotationCanBeRevisitedForARequiredCorner()
    {
        var members = new List<KsProcgenAssemblyMember> { Member("A"), Member("B") };
        members[0].Footprint = [new(0, 0), new(1, 0)];
        members[0].AllowedQuarterTurns = [0, 1];
        var search = Search(members,
            [new() { Id = "Corner", Subject = "B", Kind = KsProcgenRelationKind.AtCorner }],
            Rectangle(3, 3), walls: new HashSet<Vector2i> { new(2, 0), new(3, 1) });
        Assert.That(search.ExploreAnchor(new(1, 1)), Is.True);
        Assert.That(search.Exhausted, Is.False);
        Assert.That(search.Accepted.Single(entity => entity.EntryId == "A").QuarterTurns, Is.EqualTo(1));
        Assert.That(search.Accepted.Single(entity => entity.EntryId == "B").Cell, Is.EqualTo(new Vector2i(2, 1)));
        Assert.That(search.Backtracks, Is.GreaterThan(0));
    }

    [Test]
    public void OptionalMemberCanBeOmittedAfterItsLegalFootprintBreaksARequiredPath()
    {
        var members = new List<KsProcgenAssemblyMember> { Member("A"), Member("B"), Member("C") };
        members[0].Movement = KsProcgenMovementClass.Clear;
        members[1].Movement = KsProcgenMovementClass.Clear;
        members[2].MinimumCount = 0;
        var search = Search(members,
            [new() { Id = "Near", Subject = "A", Target = "B", Kind = KsProcgenRelationKind.Near,
                MinimumDistance = 2, MaximumDistance = 2 }], Rectangle(4, 1));
        Assert.That(search.ExploreAnchor(new(1, 0)), Is.True);
        Assert.That(search.Exhausted, Is.False);
        Assert.That(search.Accepted.Select(entity => entity.EntryId), Is.EqualTo(new[] { "A", "B" }));
        Assert.That(search.Accepted.Single(entity => entity.EntryId == "B").Cell, Is.EqualTo(new Vector2i(3, 0)));
        Assert.That(search.Witnesses.Single().PathDistance, Is.EqualTo(2));
        Assert.That(search.Backtracks, Is.GreaterThan(0));
    }

    [Test]
    public void MultiCellMachineCanUseAnotherFrontEdgeApproach()
    {
        var members = new List<KsProcgenAssemblyMember> { Member("A"), Member("B") };
        members[0].Role = KsProcgenEntityRole.Equipment;
        members[0].RequiresInteractionApproach = true;
        members[0].Footprint = [new(0, 0), new(1, 0)];
        members[1].Role = KsProcgenEntityRole.Storage;
        members[1].Movement = KsProcgenMovementClass.Clear;
        var search = Search(members,
            [new() { Id = "Near", Subject = "A", Target = "B", Kind = KsProcgenRelationKind.Near,
                MinimumDistance = 0, MaximumDistance = 1 },
                new() { Id = "Corner", Subject = "B", Kind = KsProcgenRelationKind.AtCorner }],
            Rectangle(4, 3), walls: new HashSet<Vector2i> { new(4, 0), new(3, -1) });
        Assert.That(search.ExploreAnchor(new(1, 1)), Is.True);
        Assert.That(search.Exhausted, Is.False);
        Assert.That(search.Accepted.Single(entity => entity.EntryId == "A").InteractionApproach,
            Is.EqualTo(new Vector2i(2, 0)));
        Assert.That(search.Witnesses.Single(witness => witness.RelationId == "Near").PathDistance, Is.EqualTo(1));
    }

    [Test]
    public void EarlierChairApproachCanBeRevisitedWhenTheAssociatedMachineOccupiesIt()
    {
        var members = new List<KsProcgenAssemblyMember> { Member("A"), Member("B") };
        members[0].Role = KsProcgenEntityRole.Seat;
        members[0].Movement = KsProcgenMovementClass.Clear;
        members[1].Role = KsProcgenEntityRole.Equipment;
        members[1].RequiresInteractionApproach = true;
        members[1].AllowedQuarterTurns = [2];
        members[1].LocalQuarterTurns = 2;
        var search = Search(members,
            [new() { Id = "Facing", Subject = "A", Target = "B", Kind = KsProcgenRelationKind.FacingTarget },
                new() { Id = "Seat", Subject = "B", Target = "A", Kind = KsProcgenRelationKind.UsesSeat }],
            Rectangle(3, 3));
        Assert.That(search.ExploreAnchor(new(1, 1)), Is.True);
        Assert.That(search.Exhausted, Is.False);
        Assert.That(search.Accepted.Single(entity => entity.EntryId == "B").Cell, Is.EqualTo(new Vector2i(1, 0)));
        Assert.That(search.Accepted.Single(entity => entity.EntryId == "A").InteractionApproach,
            Is.EqualTo(new Vector2i(0, 1)));
        Assert.That(search.Backtracks, Is.GreaterThan(0));
    }

    [Test]
    public void MachineStillPrefersWallBackingAndFacesCleanFloor()
    {
        var members = new List<KsProcgenAssemblyMember> { Member("A") };
        members[0].Role = KsProcgenEntityRole.Equipment;
        members[0].RequiresInteractionApproach = true;
        members[0].AllowedQuarterTurns = [0, 1, 2, 3];
        var search = Search(members, [], Rectangle(3, 3),
            walls: new HashSet<Vector2i> { new(3, 1) });
        Assert.That(search.ExploreAnchor(new(2, 1)), Is.True);
        Assert.That(search.Accepted.Single().QuarterTurns, Is.EqualTo(1));
        Assert.That(search.Accepted.Single().InteractionApproach, Is.EqualTo(new Vector2i(1, 1)));
    }

    [Test]
    public void ContinuationCanRejectACompletePoseAndResumeAtAnotherAnchor()
    {
        var calls = 0;
        var search = Search([Member("A")], [], Rectangle(4, 1), continuation: (_, _, anchor, probes) =>
        {
            calls++;
            return new KsProcgenAssemblyContinuationResult(anchor == new Vector2i(2, 0), probes, false);
        });
        Assert.That(search.ExploreAnchor(new(1, 0)), Is.False);
        Assert.That(search.Accepted, Is.Empty);
        Assert.That(search.ExploreAnchor(new(2, 0)), Is.True);
        Assert.That(search.Accepted.Single().Cell, Is.EqualTo(new Vector2i(2, 0)));
        Assert.That(calls, Is.EqualTo(2));
    }

    [Test]
    public void ContinuationSpentProbesStopTheParentWithoutAPartialCore()
    {
        var search = Search([Member("A")], [], Rectangle(4, 1), maximumProbes: 3,
            continuation: (_, _, _, _) => new KsProcgenAssemblyContinuationResult(false, 3, true));
        Assert.That(search.ExploreAnchor(new(1, 0)), Is.True);
        Assert.That(search.Exhausted, Is.True);
        Assert.That(search.Probes, Is.EqualTo(3));
        Assert.That(search.Accepted, Is.Empty);
    }

    [Test]
    public void RejectedContinuationWorkRemainsChargedWhenTryingTheNextAnchor()
    {
        var search = Search([Member("A")], [], Rectangle(4, 1), maximumProbes: 4,
            continuation: (_, _, anchor, probes) => anchor == new Vector2i(1, 0)
                ? new KsProcgenAssemblyContinuationResult(false, 3, false)
                : new KsProcgenAssemblyContinuationResult(true, probes, false));
        Assert.That(search.ExploreAnchor(new(1, 0)), Is.False);
        Assert.That(search.Probes, Is.EqualTo(3));
        Assert.That(search.ExploreAnchor(new(2, 0)), Is.True);
        Assert.That(search.Probes, Is.EqualTo(4));
        Assert.That(search.Accepted.Single().Cell, Is.EqualTo(new Vector2i(2, 0)));
    }

    [Test]
    public void BacktrackingBudgetFailureDoesNotReturnAPartialCore()
    {
        var search = Search([Member("A"), Member("B")], [], Rectangle(3, 1), maximumProbes: 1);
        Assert.That(search.ExploreAnchor(new(1, 0)), Is.True);
        Assert.That(search.Exhausted, Is.True);
        Assert.That(search.Probes, Is.EqualTo(1));
        Assert.That(search.Accepted, Is.Empty);
        Assert.That(search.Witnesses, Is.Empty);
    }

    [Test]
    public void ExhaustedAnchorCanBeRetriedWithoutLeakingEarlierClaims()
    {
        var members = new List<KsProcgenAssemblyMember> { Member("A"), Member("B") };
        members[1].Footprint = [new(0, 0), new(1, 0)];
        var search = Search(members, [], Rectangle(4, 1));
        Assert.That(search.ExploreAnchor(new(2, 0)), Is.False);
        Assert.That(search.Exhausted, Is.False);
        Assert.That(search.Accepted, Is.Empty);
        Assert.That(search.ExploreAnchor(new(1, 0)), Is.True);
        Assert.That(search.Accepted.Single(entity => entity.EntryId == "B").OccupiedCells,
            Is.EqualTo(new[] { new Vector2i(2, 0), new Vector2i(3, 0) }));
    }

    private static KsProcgenFloorAssemblySearch Search(List<KsProcgenAssemblyMember> members,
        List<KsProcgenAssemblyRelation> relations, HashSet<Vector2i> floor,
        HashSet<Vector2i>? walls = null, int maximumProbes = 4096,
        KsProcgenAssemblyContinuation? continuation = null)
    {
        Assert.That(KsProcgenAssemblyCompiler.TryCompileVariant("Fixture",
            new KsProcgenAssemblyVariant { Id = "Base", AnchorMember = "A", Members = members, Relations = relations },
            new Dictionary<string, string>(), _ => true, out var assembly, out var issue), Is.True, issue?.Message);
        return new KsProcgenFloorAssemblySearch(assembly!, "Pack", "Core", 0, floor,
            new HashSet<Vector2i> { new(0, 0) }, walls ?? new HashSet<Vector2i>(),
            new HashSet<Vector2i>(), new HashSet<Vector2i>(), 17, "Room", 0, maximumProbes, 0,
            new KsProcgenRelationPathBudget(), continuation: continuation);
    }

    private static KsProcgenAssemblyMember Member(string id) => new()
    {
        Id = id, Entity = id, Role = KsProcgenEntityRole.PrimaryFurniture,
        AllowedQuarterTurns = [0], RotationMode = KsProcgenMemberRotation.Independent,
    };

    private static HashSet<Vector2i> Rectangle(int width, int height) => Enumerable.Range(0, width)
        .SelectMany(x => Enumerable.Range(0, height).Select(y => new Vector2i(x, y))).ToHashSet();
}
