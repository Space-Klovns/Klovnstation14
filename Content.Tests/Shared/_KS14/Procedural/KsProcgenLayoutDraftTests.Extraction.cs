#nullable enable
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

public sealed partial class KsProcgenLayoutDraftTests
{
    [Test]
    public void SourceExtractionIsDetachedAndAffectsIdentityWithoutMovingLayoutGeometry()
    {
        var spec = Spec(); var room = spec.Rooms["Big"];
        room.SourceKey = "atlas"; room.ContentFingerprint = "expected";
        var original = Capture(spec); room.SourceGridUid = 123; room.SourceOrigin = new(50, -20);
        var extracted = Capture(spec);
        Assert.That(extracted.DeclarationHash, Is.Not.EqualTo(original.DeclarationHash));
        Assert.That(extracted.RoomSources[0].SourceGridUid, Is.EqualTo(123));
        Assert.That(extracted.RoomSources[0].SourceOrigin, Is.EqualTo(new Vector2i(50, -20)));
        Assert.That(extracted.CopySpec().Rooms["Big"].Cells, Is.EqualTo(original.CopySpec().Rooms["Big"].Cells));
        room.SourceGridUid = 456; room.SourceOrigin = new(60, 10);
        Assert.That(Capture(spec).DeclarationHash, Is.Not.EqualTo(extracted.DeclarationHash));
        var copy = extracted.CopySpec(); copy.Rooms["Big"].SourceOrigin = default;
        Assert.That(extracted.CopySpec().Rooms["Big"].SourceOrigin, Is.EqualTo(new Vector2i(50, -20)));
        Assert.That(Capture(extracted.CopySpec()).DeclarationHash, Is.EqualTo(extracted.DeclarationHash));
    }

    [Test]
    public void LegacyLayoutDocumentsMigrateToTheSameCanonicalVersionTwoData()
    {
        var legacy = Spec(); legacy.SchemaVersion = 1;
        var current = Spec();
        Assert.That(Capture(legacy).DeclarationHash, Is.EqualTo(Capture(current).DeclarationHash));
        Assert.That(Capture(legacy).SchemaVersion, Is.EqualTo(2));
        Assert.That(Capture(legacy).CopySpec().SchemaVersion, Is.EqualTo(2));
    }

    [TestCase("zero-grid")] [TestCase("negative-grid")] [TestCase("origin-range")]
    [TestCase("translated-range")] [TestCase("without-source")] [TestCase("legacy-grid")] [TestCase("legacy-origin")]
    public void InvalidSourceExtractionsAreRejectedBeforeAnyLibraryIsProduced(string kind)
    {
        var spec = Spec(); var room = spec.Rooms["Big"];
        room.SourceKey = "atlas"; room.ContentFingerprint = "expected";
        switch (kind)
        {
            case "zero-grid": room.SourceGridUid = 0; break;
            case "negative-grid": room.SourceGridUid = -1; break;
            case "origin-range": room.SourceOrigin = new(int.MaxValue, 0); break;
            case "translated-range": room.SourceOrigin = new(1_000_000, 0); break;
            case "without-source": room.SourceKey = null; room.ContentFingerprint = null; room.SourceGridUid = 1; break;
            case "legacy-grid": spec.SchemaVersion = 1; room.SourceGridUid = 1; break;
            case "legacy-origin": spec.SchemaVersion = 1; room.SourceOrigin = new(1, 0); break;
        }
        Assert.That(KsProcgenLayoutDraft.TrySnapshot(spec, out var draft, out var issue), Is.False);
        Assert.That(draft, Is.Null); Assert.That(issue, Is.Not.Null);
    }
}
