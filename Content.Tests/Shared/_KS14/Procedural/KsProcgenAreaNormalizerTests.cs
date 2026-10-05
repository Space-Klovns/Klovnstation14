using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenAreaNormalizerTests
{
    [Test]
    public void CardinalPaintFormsExactBlobsAndDoesNotFillHolesOrDiagonalContact()
    {
        var cells = Enumerable.Range(0, 3).SelectMany(x => Enumerable.Range(0, 3).Select(y => new Vector2i(x, y)))
            .Where(cell => cell != new Vector2i(1, 1)).Append(new(3, 3)).ToArray();
        var result = Normalize(cells.Select(cell => Marker(cell)).ToArray());
        Assert.That(result.Status, Is.EqualTo(KsProcgenStatus.Success));
        Assert.That(result.Blobs.Select(blob => blob.Cells.Count).OrderBy(count => count), Is.EqualTo(new[] { 1, 8 }));
        Assert.That(result.Blobs.SelectMany(blob => blob.Cells), Does.Not.Contain(new Vector2i(1, 1)));
        foreach (var blob in result.Blobs)
        {
            Assert.That(KsProcgenGeometry.TryNormalize(blob.CreateRequest(4), out var shape, out var issue), Is.True, issue?.Code);
            Assert.That(shape!.TargetCells, Is.EqualTo(blob.Cells));
        }
    }

    [Test]
    public void VoidSubtractionRemovesABridgeBeforeComponentsAreFound()
    {
        var markers = Enumerable.Range(0, 3).Select(x => Marker(new(x, 0))).ToArray();
        Assert.That(Normalize(markers).Blobs.Count, Is.EqualTo(1));
        var split = Normalize(markers, keepVoid: new HashSet<Vector2i> { new(1, 0) });
        Assert.That(split.Blobs.Count, Is.EqualTo(2));
        Assert.That(split.Blobs.All(blob => blob.Cells.Count == 1), Is.True);
        Assert.That(Normalize(markers, keepVoid: markers.Select(marker => marker.Cell).ToHashSet()).Status, Is.EqualTo(KsProcgenStatus.NoOp));
    }

    [Test]
    public void ProfilesCannotSilentlySplitConnectedPaintAndConflictingDuplicatesFail()
    {
        var profiles = new[] { Profile(), Profile("Other") };
        AssertFailure(Normalize([Marker(new(0, 0)), Marker(new(1, 0), profile: "Other")], profiles: profiles), "ConflictingAreaComponent");
        AssertFailure(Normalize([Marker(new(0, 0)), Marker(new(0, 0), profile: "Other")], profiles: profiles), "ConflictingAreaPaint");
        var duplicate = Normalize([Marker(new(0, 0)), Marker(new(0, 0))]);
        Assert.That(duplicate.Blobs.Single().Cells.Count, Is.EqualTo(1));
        Assert.That(duplicate.SnapshotHash, Is.EqualTo(Normalize([Marker(new(0, 0))]).SnapshotHash));
    }

    [Test]
    public void ChannelsStaySeparateAndOverlappingClaimsFail()
    {
        var separate = Normalize([Marker(new(0, 0)), Marker(new(1, 0), channel: "Other")]);
        Assert.That(separate.Blobs.Count, Is.EqualTo(2));
        AssertFailure(Normalize([Marker(new(0, 0)), Marker(new(0, 0), channel: "Other")]), "OverlappingAreaChannels");
    }

    [Test]
    public void ExplicitIdsAreComponentWideAndCannotBeReusedAcrossDisconnectedComponents()
    {
        var result = Normalize([Marker(new(0, 0), blobId: "Fixed"), Marker(new(1, 0))]);
        Assert.That(result.Blobs.Single().Id, Is.EqualTo("Fixed"));
        Assert.That(Normalize([Marker(new(0, 0), blobId: "Fixed")]).Blobs.Single().Id, Is.EqualTo("Fixed"));
        AssertFailure(Normalize([Marker(new(0, 0), blobId: "One"), Marker(new(1, 0), blobId: "Two")]), "ConflictingAreaComponent");
        AssertFailure(Normalize([Marker(new(0, 0), blobId: "Fixed"), Marker(new(2, 0), blobId: "Fixed")]), "DuplicateAreaBlobId");
    }

    [Test]
    public void ReplayIsCanonicalAndProfileChangesAffectGeometryIdentityButNotDerivedIds()
    {
        var markers = new[] { Marker(new(-2, 3)), Marker(new(-1, 3)), Marker(new(9, 9)) };
        var original = Normalize(markers);
        var replay = Normalize(markers.Reverse().ToArray());
        Assert.That(replay.SnapshotHash, Is.EqualTo(original.SnapshotHash));
        Assert.That(replay.Blobs.Select(blob => blob.Id), Is.EqualTo(original.Blobs.Select(blob => blob.Id)));
        var changed = Normalize(markers, profiles: [Profile() with { Theme = "DifferentTheme" }]);
        Assert.That(changed.SnapshotHash, Is.Not.EqualTo(original.SnapshotHash));
        Assert.That(changed.Blobs.Select(blob => blob.Id), Is.EqualTo(original.Blobs.Select(blob => blob.Id)));
        var request = original.Blobs[0].CreateRequest(10);
        request.Shape.Cells.Clear();
        Assert.That(original.Blobs[0].Cells, Is.Not.Empty);
    }

    [Test]
    public void InvalidInputsAndBudgetsAreAtomicIncludingLateComponentFailure()
    {
        AssertFailure(Normalize([Marker(new(0, 0), profile: "Missing")]), "InvalidAreaMarker");
        AssertFailure(Normalize([Marker(new(int.MaxValue, 0))]), "InvalidAreaMarker");
        AssertFailure(Normalize([Marker(new(0, 0), channel: " ")]), "InvalidAreaMarker");
        var markers = new[] { Marker(new(0, 0)), Marker(new(2, 0)) };
        var exhausted = Normalize(markers, maximumBlobs: 1);
        AssertFailure(exhausted, "AreaBlobBudget");
        Assert.That(exhausted.Status, Is.EqualTo(KsProcgenStatus.BudgetExceeded));
        AssertFailure(Normalize(markers, maximumMarkers: 1), "AreaMarkerBudget");
        AssertFailure(Normalize([Marker(new(0, 0)), Marker(new(1, 0))], profiles: [Profile() with { MaxCells = 1 }]), "AreaProfileCellBudget");
    }

    private static KsProcgenAreaMarker Marker(Vector2i cell, string channel = "Default", string profile = "Profile", string? blobId = null) =>
        new(cell, channel, profile, BlobId: blobId);

    private static KsProcgenAreaProfile Profile(string id = "Profile") => new(id, KsProcgenMode.Procedural,
        KsProcgenGeometryMode.Footprint, KsProcgenConnectivityPolicy.SingleNetwork, "Theme", 65536, 1000000);

    private static KsProcgenAreaNormalization Normalize(KsProcgenAreaMarker[] markers, KsProcgenAreaProfile[]? profiles = null,
        IReadOnlySet<Vector2i>? keepVoid = null, int maximumMarkers = 65536, int maximumBlobs = 4096) =>
        KsProcgenAreaNormalizer.Normalize("StableGrid", markers, profiles ?? [Profile()], keepVoid: keepVoid,
            maximumMarkers: maximumMarkers, maximumBlobs: maximumBlobs);

    private static void AssertFailure(KsProcgenAreaNormalization result, string code)
    {
        Assert.That(result.Status, Is.AnyOf(KsProcgenStatus.InvalidInput, KsProcgenStatus.BudgetExceeded));
        Assert.That(result.Issue?.Code, Is.EqualTo(code));
        Assert.That(result.Blobs, Is.Empty);
        Assert.That(result.SnapshotHash, Is.Zero);
    }
}
