using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenEntranceNormalizerTests
{
    [Test]
    public void WideEntranceBindsItsEntireDirectedInsideSpanAndRetainsReadOnlyExteriorGeometry()
    {
        var result = Normalize([new(0, 0), new(0, 1), new(1, 0)],
            [Entrance("Service", [new(-1, 1), new(-1, 0)])]);
        Assert.That(result.Status, Is.EqualTo(KsProcgenStatus.Success), result.Issue?.Code);
        var entrance = result.Entrances.Single();
        Assert.That(entrance.InsideApproach, Is.EqualTo(new Vector2i[] { new(0, 0), new(0, 1) }));
        Assert.That(entrance.OutsideApproach, Is.EqualTo(new Vector2i[] { new(-2, 0), new(-2, 1) }));
        Assert.That(result.Blobs.Single().Cells, Does.Not.Contain(new Vector2i(-1, 0)));
        Assert.That(result.Blobs.Single().Entrances, Is.EqualTo(result.Entrances));
        var request = result.Blobs.Single().CreateRequest(4);
        Assert.That(KsProcgenGeometry.TryNormalize(request, out var shape, out var issue), Is.True, issue?.Code);
        Assert.That(shape!.Entrances.Single().PortId, Is.EqualTo("Service"));
        Assert.That(KsProcgenPackingPlanner.Plan(request, []).Issue?.Code, Is.EqualTo("UnsupportedEntranceAwarePacking"),
            "The old all-connected routing planner must not silently discard entrances.");
    }

    [Test]
    public void RepeatedLabelsAcrossBlobsAreValidButDuplicateLabelsWithinOneBlobFailAtomically()
    {
        var result = Normalize([new(0, 0), new(0, 3)],
            [Entrance("1", [new(-1, 0)]), Entrance("1", [new(-1, 3)])]);
        Assert.That(result.Entrances.Count, Is.EqualTo(2));
        Assert.That(result.Entrances.Select(entrance => entrance.BlobId).Distinct().Count(), Is.EqualTo(2));
        AssertFailure(Normalize([new(0, 0), new(0, 1)],
            [Entrance("1", [new(-1, 0)]), Entrance("1", [new(-1, 1)])]), "DuplicateEntrancePort");
    }

    [Test]
    public void EveryInsideCellMustResolveEvenWithAnExplicitOwner()
    {
        var marker = Entrance("Wide", [new(-1, 0), new(-1, 1)], blobId: "Fixed");
        AssertFailure(Normalize([new(0, 0)], [marker], blobId: "Fixed"), "OrphanEntranceMarker");
        AssertFailure(Normalize([new(0, 0)], [Entrance("1", [new(-1, 0)], blobId: "Wrong")], blobId: "Fixed"), "EntranceOwnerMismatch");
        var removed = KsProcgenAreaNormalizer.Normalize("Host", [new(new(0, 0), "Default", "Office", BlobId: "Fixed")], [Profile()],
            keepVoid: new HashSet<Vector2i> { new(0, 0) }, entrances: [Entrance("1", [new(-1, 0)])]);
        AssertFailure(removed, "OrphanEntranceMarker");
    }

    [Test]
    public void ASpanAcrossDistinctDomainsCannotPickTheFirstOwner()
    {
        var blobs = new[] { Blob("One", [new(0, 0)]), Blob("Two", [new(0, 1)]) };
        var result = KsProcgenEntranceNormalizer.Normalize(blobs, [Entrance("Wide", [new(-1, 0), new(-1, 1)])]);
        Assert.That(result.Issue?.Code, Is.EqualTo("AmbiguousEntranceOwner"));
        Assert.That(result.Entrances, Is.Empty);
    }

    [Test]
    public void SpansMustBeContiguousPerpendicularAndFreeOfDuplicateThresholds()
    {
        var cells = new Vector2i[] { new(0, 0), new(0, 1), new(0, 2) };
        AssertFailure(Normalize(cells, [Entrance("Gap", [new(-1, 0), new(-1, 2)])]), "InvalidEntranceSpan");
        AssertFailure(Normalize(cells, [Entrance("AlongNormal", [new(-1, 0), new(0, 0)])]), "InvalidEntranceSpan");
        AssertFailure(Normalize(cells, [Entrance("Duplicate", [new(-1, 0), new(-1, 0)])]), "InvalidEntranceSpan");
        AssertFailure(Normalize(cells, [Entrance("One", [new(-1, 0)]), Entrance("Two", [new(-1, 0)])]), "OverlappingEntranceThresholds");
    }

    [Test]
    public void InteriorPaintCannotPretendToHaveAnExteriorApproach()
    {
        AssertFailure(Normalize([new(-1, 0), new(0, 0), new(1, 0)],
            [Entrance("Interior", [new(0, 0)])]), "EntranceNotAtBoundary");
        var boundary = Normalize([new(0, 0), new(1, 0)], [Entrance("Boundary", [new(0, 0)])]);
        Assert.That(boundary.Status, Is.EqualTo(KsProcgenStatus.Success), boundary.Issue?.Code);
        Assert.That(boundary.Entrances.Single().OutsideApproach, Is.EqualTo(new Vector2i[] { new(-1, 0) }));
    }

    [Test]
    public void ReplayIgnoresMarkerAndSpanOrderingButIncludesPortPermissions()
    {
        var thresholds = new List<Vector2i> { new(-1, 0), new(-1, 1) };
        var markers = new[] { Entrance("Wide", thresholds), Entrance("Other", [new(-1, 3)]) };
        var cells = new Vector2i[] { new(0, 0), new(0, 1), new(0, 3) };
        var original = Normalize(cells, markers);
        var replay = Normalize(cells.Reverse().ToArray(), markers.Reverse().Select(marker => marker with
            { ThresholdCells = marker.ThresholdCells.Reverse().ToArray() }).ToArray());
        Assert.That(replay.SnapshotHash, Is.EqualTo(original.SnapshotHash));
        Assert.That(replay.Entrances.Select(entrance => entrance.PortId), Is.EqualTo(original.Entrances.Select(entrance => entrance.PortId)));
        var changed = Normalize(cells, markers.Select(marker => marker with { OptionalSealable = true }).ToArray());
        Assert.That(changed.SnapshotHash, Is.Not.EqualTo(original.SnapshotHash));
        Assert.That(changed.Blobs.Select(blob => blob.Id), Is.EqualTo(original.Blobs.Select(blob => blob.Id)));
        thresholds.Clear();
        Assert.That(original.Entrances.Single(entrance => entrance.PortId == "Wide").ThresholdCells.Count, Is.EqualTo(2));
    }

    [Test]
    public void EntranceBudgetsAndMalformedDirectionsReturnNoPartialSnapshot()
    {
        var cells = new Vector2i[] { new(0, 0) };
        var markers = new[] { Entrance("1", [new(-1, 0)]) };
        AssertFailure(Normalize(cells, markers, maximumEntrances: 0), "EntranceMarkerBudget");
        AssertFailure(Normalize(cells, markers, maximumEntrances: -1), "InvalidEntranceInput");
        AssertFailure(Normalize(cells, [markers[0] with { InwardNormal = new(1, 1) }]), "InvalidEntranceMarker");
        AssertFailure(Normalize(cells, [markers[0] with { InwardNormal = new(int.MinValue, 0) }]), "InvalidEntranceMarker");
        AssertFailure(Normalize(cells, [markers[0] with { ThresholdCells = [] }]), "InvalidEntranceMarker");
        AssertFailure(Normalize(cells, [markers[0] with { ThresholdCells = Enumerable.Range(0, 65).Select(y => new Vector2i(-1, y)).ToArray() }]), "InvalidEntranceMarker");
        AssertFailure(Normalize(cells, [markers[0] with { PortId = " " }]), "InvalidEntranceMarker");
        AssertFailure(Normalize(cells, [markers[0] with { Channel = "Missing" }]), "OrphanEntranceMarker");
    }

    [Test]
    public void CoordinateLimitsApplyToBothApproachesBeforeIntegerConversion()
    {
        var result = Normalize([new(999999, 0)],
            [Entrance("Edge", [new(1000000, 0)]) with { InwardNormal = new(-1, 0) }]);
        AssertFailure(result, "InvalidEntranceApproach");
    }

    private static KsProcgenAreaProfile Profile() => new("Office", KsProcgenMode.Procedural,
        KsProcgenGeometryMode.Footprint, KsProcgenConnectivityPolicy.SingleNetwork, null, 65536, 1000000);

    private static KsProcgenAreaBlob Blob(string id, IReadOnlyList<Vector2i> cells) => new(id, "Default", Profile(), cells);

    private static KsProcgenEntranceMarker Entrance(string portId, IReadOnlyList<Vector2i> thresholds, string? blobId = null) =>
        new(portId, "Default", thresholds, new(1, 0), BlobId: blobId);

    private static KsProcgenAreaNormalization Normalize(IReadOnlyList<Vector2i> cells,
        IReadOnlyList<KsProcgenEntranceMarker> entrances, string? blobId = null, int maximumEntrances = 1024) =>
        KsProcgenAreaNormalizer.Normalize("Host", cells.Select(cell => new KsProcgenAreaMarker(cell, "Default", "Office", BlobId: blobId)).ToArray(),
            [Profile()], entrances: entrances, maximumEntrances: maximumEntrances);

    private static void AssertFailure(KsProcgenAreaNormalization result, string code)
    {
        Assert.That(result.Status, Is.AnyOf(KsProcgenStatus.InvalidInput, KsProcgenStatus.BudgetExceeded));
        Assert.That(result.Issue?.Code, Is.EqualTo(code));
        Assert.That(result.Issue?.Message, Is.Not.Empty);
        Assert.That(result.Blobs, Is.Empty);
        Assert.That(result.Entrances, Is.Empty);
        Assert.That(result.SnapshotHash, Is.Zero);
    }
}
