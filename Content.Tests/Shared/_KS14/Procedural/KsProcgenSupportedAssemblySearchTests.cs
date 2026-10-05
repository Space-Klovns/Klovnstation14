using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenSupportedAssemblySearchTests
{
    [Test]
    public void TinyRightWallPlacementFindsIndependentWestFacingMachine()
    {
        var result = Search(Assembly(), Corridor(), fixedRoots: [new("Root", new(2f, 0f))]);
        Assert.That(result.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate), result.Issue?.Code);
        Assert.That(result.Orientations.Single(turn => turn.MemberId == "Device").QuarterTurns, Is.EqualTo(1));
        Assert.That(result.Access!.Access.Single().Approach, Is.EqualTo(new Vector2i(1, 0)));
        Assert.That(result.EnginePlacementVerified, Is.False);
        Assert.That(result.PlacementProbes, Is.GreaterThan(2));
    }

    [Test]
    public void SearchRevisitsFloorRootsAfterAllTheirOperatingApproachesFail()
    {
        var assembly = Assembly();
        assembly.Members.Single(member => member.Id == "Root").Entry.AllowedQuarterTurns = [0];
        assembly.Members.Single(member => member.Id == "Device").Entry.AllowedQuarterTurns = [1];
        var floor = new HashSet<Vector2i> { new(0, 0), new(1, 0), new(2, 0), new(9, 9), new(10, 9) };
        var room = Corridor() with { Floor = floor };
        // Select a seed that tries the isolated tile first. The subsequent choice must revisit Root.
        var seed = Enumerable.Range(0, 1000).First(value => floor.OrderBy(cell => Rank(value, assembly, cell)).First() == new Vector2i(10, 9));
        var result = Search(assembly, room, seed: seed);
        Assert.That(result.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate));
        Assert.That(result.FloorRoots.Single().Position, Is.EqualTo(new Vector2(1f, 0f)).Or.EqualTo(new Vector2(2f, 0f)));
        Assert.That(result.ExpandedCells, Is.GreaterThan(result.Access!.ExpandedCells));
        Assert.That(result.Access!.Access.Single().CleanPath.Cells.First(), Is.EqualTo(new Vector2i(0, 0)));
        // Force the isolated branch independently to establish why it must be abandoned.
        Assert.That(Search(assembly, room, fixedRoots: [new("Root", new(10f, 9f))]).Status,
            Is.EqualTo(KsProcgenSupportedAccessStatus.Rejected));
    }

    [Test]
    public void FloorRootsNeverOverlapOrOccupyProtectedNetworkOrExistingContent()
    {
        var assembly = Assembly() with { Members = Assembly().Members.Append(Member("Other")).ToArray() };
        var room = Corridor() with { Floor = Enumerable.Range(0, 5).Select(x => new Vector2i(x, 0)).ToHashSet(),
            Occupied = new HashSet<Vector2i> { new(4, 0) } };
        var result = Search(assembly, room);
        Assert.That(result.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate));
        Assert.That(result.FloorRoots.Select(root => root.Position).Distinct().Count(), Is.EqualTo(2));
        Assert.That(result.FloorRoots.Any(root => root.Position == Vector2.Zero || root.Position == new Vector2(4f, 0f)), Is.False);
        AssertAtomicFailure(Search(assembly, room, fixedRoots: [new("Root", new(2f, 0f)), new("Other", new(2f, 0f))]),
            KsProcgenSupportedAccessStatus.Rejected);
        AssertAtomicFailure(Search(assembly, room, fixedRoots: [new("Root", Vector2.Zero)]), KsProcgenSupportedAccessStatus.Rejected);
    }

    [Test]
    public void RelativeAndContainerTurnsAreSolvedTogetherWithoutMixingMembership()
    {
        var assembly = Assembly() with { Members = Assembly().Members.Append(Member("Stored") with
            { RotationMode = KsProcgenMemberRotation.AssemblyRelative, LocalQuarterTurns = 1 }).ToArray(),
            Relations = Assembly().Relations.Append(Relation("Inside", "Stored", "Device", KsProcgenRelationKind.InContainer)).ToArray() };
        var result = Search(assembly, Corridor(), fixedRoots: [new("Root", new(2f, 0f))]);
        Assert.That(result.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate));
        Assert.That(result.Orientations.Single(turn => turn.MemberId == "Stored").QuarterTurns, Is.EqualTo(1));
        Assert.That(result.Access!.Poses!.Members.Single(pose => pose.Support.MemberId == "Stored").LocalQuarterTurns, Is.Zero);
        var report = Report(assembly);
        AssertAtomicFailure(KsProcgenSupportedAssemblySearch.Search(assembly, ["Root", "Device"], report, Corridor(), 0),
            KsProcgenSupportedAccessStatus.InvalidInput);
        assembly.Members.Single(member => member.Id == "Stored").Entry.AllowedQuarterTurns = [3];
        AssertAtomicFailure(Search(assembly, Corridor(), fixedRoots: [new("Root", new(2f, 0f))]), KsProcgenSupportedAccessStatus.Rejected);
    }

    [Test]
    public void BudgetsAreAtomicAndPathAllowanceIsSharedAcrossFailedModels()
    {
        var assembly = Assembly();
        AssertAtomicFailure(Search(assembly, Corridor(), maximumPlacementProbes: 0), KsProcgenSupportedAccessStatus.BudgetExceeded);
        AssertAtomicFailure(Search(assembly, Corridor(), maximumExpandedCells: 0), KsProcgenSupportedAccessStatus.BudgetExceeded);
        var room = Corridor() with { Floor = new HashSet<Vector2i> { new(0, 0), new(9, 9), new(10, 9) } };
        var result = Search(assembly, room, fixedRoots: [new("Root", new(10f, 9f))], maximumExpandedCells: 2);
        AssertAtomicFailure(result, KsProcgenSupportedAccessStatus.BudgetExceeded);
        Assert.That(result.ExpandedCells, Is.EqualTo(2));
        AssertAtomicFailure(Search(assembly, room, fixedRoots: [new("Root", new(10f, 9f))], maximumExpandedCells: 100),
            KsProcgenSupportedAccessStatus.Rejected);
    }

    [Test]
    public void InvalidSpatialRelationsAndMalformedFixedRootsFailBeforeSearch()
    {
        var assembly = Assembly();
        AssertAtomicFailure(Search(assembly, Corridor(), fixedRoots: [new("Root", new(1.5f, 0f))]),
            KsProcgenSupportedAccessStatus.InvalidInput);
        AssertAtomicFailure(Search(assembly, Corridor(), fixedRoots: [new("Device", new(2f, 0f))]),
            KsProcgenSupportedAccessStatus.InvalidInput);
        AssertAtomicFailure(Search(assembly, Corridor() with { CellCenterOrigin = new(float.NaN, 0f) }),
            KsProcgenSupportedAccessStatus.InvalidInput);
        assembly = assembly with { Relations = assembly.Relations.Append(Relation("Invalid", "Device", "Root", (KsProcgenRelationKind) 255)).ToArray() };
        var unsupported = Search(assembly, Corridor());
        AssertAtomicFailure(unsupported, KsProcgenSupportedAccessStatus.InvalidInput);
        Assert.That(unsupported.PlacementProbes, Is.Zero);
    }

    [Test]
    public void OffsetSurfaceProjectionCannotClaimPreexistingOccupiedOrBlockingCells()
    {
        var assembly = Assembly();
        var report = Report(assembly);
        report = report with { Members = report.Members.Select(member => member.MemberId == "Root" ?
            member with { Capabilities = member.Capabilities with { SurfaceOffset = new(1f, 0f) } } : member).ToArray() };
        var room = Corridor() with
        {
            Floor = Enumerable.Range(0, 4).SelectMany(x => Enumerable.Range(0, 4).Select(y => new Vector2i(x, y))).ToHashSet(),
            Walls = new HashSet<Vector2i>(),
        };
        KsProcgenSupportedAssemblySearchResult Run(KsProcgenRoomAccessMask mask) =>
            KsProcgenSupportedAssemblySearch.Search(assembly, ["Root", "Device"], report, mask, 0,
                fixedFloorRoots: [new("Root", new(1f, 1f))]);
        Assert.That(Run(room).Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate));
        AssertAtomicFailure(Run(room with { Occupied = new HashSet<Vector2i> { new(2, 1) } }), KsProcgenSupportedAccessStatus.Rejected);
        AssertAtomicFailure(Run(room with { Blocking = new HashSet<Vector2i> { new(2, 1) } }), KsProcgenSupportedAccessStatus.Rejected);
    }

    [Test]
    public void ReplayCanonicalizesInputOrderAndPreservesOriginAndCandidateIdentity()
    {
        var assembly = Assembly();
        var room = Corridor();
        var result = Search(assembly, room, seed: 19);
        var replay = Search(assembly with { Members = assembly.Members.Reverse().ToArray() },
            room with { Floor = room.Floor.Reverse().ToHashSet() }, seed: 19, maximumPlacementProbes: 10000);
        Assert.That(result.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate));
        Assert.That(replay.FloorRoots, Is.EqualTo(result.FloorRoots));
        Assert.That(replay.Orientations, Is.EqualTo(result.Orientations));
        Assert.That(replay.Access!.AccessHash, Is.EqualTo(result.Access!.AccessHash));
        var shifted = Search(assembly, room with { CellCenterOrigin = new(0.5f, 0.5f) }, seed: 19);
        Assert.That(shifted.FloorRoots.Single().Position, Is.EqualTo(result.FloorRoots.Single().Position + new Vector2(0.5f, 0.5f)));
        Assert.That(shifted.Access!.AccessHash, Is.Not.EqualTo(result.Access.AccessHash));
    }

    private static KsProcgenRoomAccessMask Corridor() => new(
        new HashSet<Vector2i> { new(0, 0), new(1, 0), new(2, 0) }, new HashSet<Vector2i> { new(3, 0) },
        new HashSet<Vector2i>(), new HashSet<Vector2i>(), new HashSet<Vector2i> { new(0, 0) }, Vector2.Zero);

    private static ulong Rank(int seed, KsProcgenResolvedAssembly assembly, Vector2i cell)
    {
        var hash = KsProcgenStableHash.Create();
        hash.AddInt(seed);
        hash.AddString("furnishing-cell");
        hash.AddString("supported-assembly-search");
        hash.AddString(assembly.AssemblyId + "/" + assembly.VariantId);
        hash.AddInt(cell.X);
        hash.AddInt(cell.Y);
        return hash.Value;
    }

    private static KsProcgenResolvedAssemblyMember Member(string id) => new(id, id, true,
        new() { Id = id, Entity = id, Movement = KsProcgenMovementClass.Blocks, RequiresInteractionApproach = id == "Device" },
        KsProcgenMemberRotation.Independent, 0);

    private static KsProcgenResolvedAssemblyRelation Relation(string id, string subject, string target, KsProcgenRelationKind kind) =>
        new(id, subject, kind, target, KsProcgenRelationSeverity.Required, 1, 4, null,
            kind == KsProcgenRelationKind.InContainer ? "Slot" : null, KsProcgenApproachPolicy.EmptyFloor);

    private static KsProcgenResolvedAssembly Assembly() => new("SearchFixture", "Base", "Root", [Member("Root"), Member("Device")],
        [Relation("Top", "Device", "Root", KsProcgenRelationKind.OnSurface)]);

    private static KsProcgenAssemblyCapabilityReport Report(KsProcgenResolvedAssembly assembly) => new(
        assembly.Members.Select(member => new KsProcgenMemberCapabilityDeclaration(member.Id,
            new(member.Entry.Entity, true, true, true, Vector2.Zero, true, false, false, false, 0.0,
                [], [new("Slot", KsProcgenDeclaredContainerKind.Container, true, false, false, null, 0, 0, null, null)]))).ToArray(), []);

    private static KsProcgenSupportedAssemblySearchResult Search(KsProcgenResolvedAssembly assembly, KsProcgenRoomAccessMask room,
        int seed = 0, IReadOnlyList<KsProcgenFloorRootPose>? fixedRoots = null, int maximumPlacementProbes = 4096,
        int maximumExpandedCells = 4096) => KsProcgenSupportedAssemblySearch.Search(assembly,
        assembly.Members.Select(member => member.Id).ToArray(), Report(assembly), room, seed,
        fixedFloorRoots: fixedRoots, maximumPlacementProbes: maximumPlacementProbes, maximumExpandedCells: maximumExpandedCells);

    private static void AssertAtomicFailure(KsProcgenSupportedAssemblySearchResult result, KsProcgenSupportedAccessStatus status)
    {
        Assert.That(result.Status, Is.EqualTo(status), result.Issue?.Code);
        Assert.That(result.Access, Is.Null);
        Assert.That(result.FloorRoots, Is.Empty);
        Assert.That(result.Orientations, Is.Empty);
    }
}
