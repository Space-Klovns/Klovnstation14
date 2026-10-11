using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenAreaSubdivisionTests
{
    [Test]
    public void SnapshotsDetachOrderedSizesAndHashThemWithoutChangingBlobIdentity()
    {
        var sizes = new List<int> { 8, 64 };
        var profile = Profile() with { RoomSubdivision = new(4, 4, sizes) };
        var first = Normalize(profile);
        Assert.That(first.Status, Is.EqualTo(KsProcgenStatus.Success));
        var blob = first.Blobs.Single();
        sizes.Reverse();
        Assert.That(blob.Profile.RoomSubdivision!.AlternativeMaximumRoomCells, Is.EqualTo(new[] { 8, 64 }));
        Assert.Throws<NotSupportedException>(() => ((IList<int>) blob.Profile.RoomSubdivision.AlternativeMaximumRoomCells)[0] = 99);
        var changed = Normalize(profile);
        Assert.That(changed.SnapshotHash, Is.Not.EqualTo(first.SnapshotHash));
        Assert.That(changed.Blobs.Single().Id, Is.EqualTo(blob.Id));
        sizes.Reverse();
        Assert.That(Normalize(profile).SnapshotHash, Is.EqualTo(first.SnapshotHash));
        Assert.That(Normalize(profile with { RoomSubdivision = new(3, 4, sizes) }).SnapshotHash, Is.Not.EqualTo(first.SnapshotHash));
        Assert.That(Normalize(profile with { RoomSubdivision = new(4, 5, sizes) }).SnapshotHash, Is.Not.EqualTo(first.SnapshotHash));
        Assert.That(Normalize(Profile()).SnapshotHash, Is.Not.EqualTo(first.SnapshotHash));
    }

    [Test]
    public void AuthoredSizesOverridePreferencesWhileEveryCallerWorkAllowanceSurvives()
    {
        var caller = new KsProcgenOwnedPlanBudgets
        {
            MinimumRoomCells = 1, PreferredMaximumRoomCells = 128, AlternativeMaximumRoomCells = [256],
            MaximumLayoutProbes = 2, MaximumConfigurationProbes = 1, MaximumReportedGraphExpansions = 123,
            MaximumOrderProbes = 3, MaximumPartitionMerges = 0, MaximumPartitionSeamProbes = 456,
            MaximumFurnishingProbes = 7, MaximumLightingFixtures = 8, MaximumContentRoomCells = 9,
        };
        var blob = Normalize(Profile() with { RoomSubdivision = new(4, 4, new[] { 8, 64 }) }).Blobs.Single();
        var adapted = blob.CreateOwnedBudgets(budgets: caller);
        Assert.That(adapted.MinimumRoomCells, Is.EqualTo(4));
        Assert.That(adapted.PreferredMaximumRoomCells, Is.EqualTo(4));
        Assert.That(adapted.AlternativeMaximumRoomCells, Is.EqualTo(new[] { 8, 64 }));
        Assert.That(adapted with
        {
            MinimumRoomCells = caller.MinimumRoomCells,
            PreferredMaximumRoomCells = caller.PreferredMaximumRoomCells,
            AlternativeMaximumRoomCells = caller.AlternativeMaximumRoomCells,
        }, Is.EqualTo(caller));
        Assert.That(caller.PreferredMaximumRoomCells, Is.EqualTo(128));
        Assert.That(Normalize(Profile()).Blobs.Single().CreateOwnedBudgets(budgets: caller), Is.SameAs(caller));
        Assert.That(blob.CreateOwnedBudgets().MaximumLayoutProbes, Is.EqualTo(64));
    }

    [Test]
    public void SerializedDefinitionProducesAnImmutableValue()
    {
        var spec = new KsProcgenRoomSubdivisionSpec
        {
            PreferredMaximumRoomCells = 4, AlternativeMaximumRoomCells = new List<int> { 8, 64 }, UseSizeGoals = true,
        };
        Assert.That(KsProcgenRoomSubdivision.TrySnapshot(spec, out var snapshot), Is.True);
        spec.MinimumRoomCells = 8;
        spec.UseSizeGoals = false;
        spec.AlternativeMaximumRoomCells.Clear();
        Assert.That(snapshot!.MinimumRoomCells, Is.EqualTo(4));
        Assert.That(snapshot.AlternativeMaximumRoomCells, Is.EqualTo(new[] { 8, 64 }));
        Assert.That(snapshot.UseSizeGoals, Is.True);
    }

    [Test]
    public void GoalStrategyChangesInputIdentityAndOverridesCallerPreference()
    {
        var profile = Profile() with { RoomSubdivision = new(4, 24, new[] { 64 }) };
        var legacy = Normalize(profile);
        var enabled = Normalize(profile with { RoomSubdivision = profile.RoomSubdivision with { UseSizeGoals = true } });
        Assert.That(enabled.SnapshotHash, Is.Not.EqualTo(legacy.SnapshotHash));
        Assert.That(enabled.Blobs.Single().Id, Is.EqualTo(legacy.Blobs.Single().Id));
        Assert.That(enabled.Blobs.Single().CreateOwnedBudgets().UseSizeGoals, Is.True);
        Assert.That(legacy.Blobs.Single().CreateOwnedBudgets(budgets: new() { UseSizeGoals = true }).UseSizeGoals, Is.False);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(6)]
    [TestCase(7)]
    public void InvalidSubdivisionDefinitionsAndProfilesFailAtomically(int variant)
    {
        var spec = new KsProcgenRoomSubdivisionSpec();
        switch (variant)
        {
            case 0: spec.MinimumRoomCells = 0; break;
            case 1: spec.PreferredMaximumRoomCells = 3; break;
            case 2: spec.PreferredMaximumRoomCells = 4_097; break;
            case 3: spec.AlternativeMaximumRoomCells = null!; break;
            case 4: spec.AlternativeMaximumRoomCells = new List<int> { 8, 8 }; break;
            case 5: spec.AlternativeMaximumRoomCells.Add(24); break;
            case 6: spec.AlternativeMaximumRoomCells.Add(3); break;
            default: spec.AlternativeMaximumRoomCells = Enumerable.Range(25, 64).ToList(); break;
        }
        Assert.That(KsProcgenRoomSubdivision.TrySnapshot(spec, out var snapshot), Is.False);
        Assert.That(snapshot, Is.Null);
        var result = Normalize(Profile() with
        {
            RoomSubdivision = new(spec.MinimumRoomCells, spec.PreferredMaximumRoomCells, spec.AlternativeMaximumRoomCells),
        });
        Assert.That(result.Issue?.Code, Is.EqualTo("InvalidAreaSubdivisionProfile"));
        Assert.That(result.Blobs, Is.Empty);
        Assert.That(result.SnapshotHash, Is.Zero);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void UnsupportedSubdivisionModesAreRejectedBeforePublishingBlobs(bool hybrid)
    {
        var profile = Profile() with { RoomSubdivision = new(4, 24, Array.Empty<int>()) };
        profile = hybrid ? profile with { Mode = KsProcgenMode.Hybrid }
            : profile with { GeometryMode = KsProcgenGeometryMode.InteriorFill };
        var result = Normalize(profile);
        Assert.That(result.Issue?.Code, Is.EqualTo("InvalidAreaSubdivisionProfile"));
        Assert.That(result.Blobs, Is.Empty);
        Assert.That(result.SnapshotHash, Is.Zero);
    }

    private static KsProcgenAreaProfile Profile() => new("Office", KsProcgenMode.Procedural,
        KsProcgenGeometryMode.Footprint, KsProcgenConnectivityPolicy.SingleNetwork, "Theme", 256, 100);

    private static KsProcgenAreaNormalization Normalize(KsProcgenAreaProfile profile) =>
        KsProcgenAreaNormalizer.Normalize("Host", new[] { new KsProcgenAreaMarker(new Vector2i(0, 0), "Default", "Office") },
            new[] { profile });
}
