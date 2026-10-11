#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenAreaGoalsTests
{
    [Test]
    public void AuthoredGoalsAndCreatedRequestsCannotMutateEarlierSnapshots()
    {
        var spec = Goals();
        Assert.That(KsProcgenAreaGoals.TrySnapshot(spec, out var goals), Is.True);
        var blob = Normalize(goals!).Blobs.Single();
        spec.SizeMix[0].MinCells = 99;
        spec.SizeMix.Clear();
        spec.FallbackPolicy.AllowMergedPartition = true;
        spec.WindowGoal.MinimumCount = 8;
        var first = blob.CreateRequest(23);
        Assert.That(first.SizeMix.Select(goal => goal.Id), Is.EqualTo(new[] { "Medium", "Small" }));
        Assert.That(first.SizeMix.Single(goal => goal.Id == "Small").MinCells, Is.EqualTo(4));
        Assert.That(first.FallbackPolicy.AllowMergedPartition, Is.False);
        Assert.That(first.WindowGoal.MinimumCount, Is.EqualTo(1));
        first.SizeMix[0].MaxCells = 999;
        first.SizeMix.Clear();
        first.FallbackPolicy.AllowSparseLighting = true;
        first.WindowGoal.ExteriorWindowFraction = 1;
        var second = blob.CreateRequest(23);
        Assert.That(second.SizeMix.Count, Is.EqualTo(2));
        Assert.That(second.FallbackPolicy.AllowSparseLighting, Is.False);
        Assert.That(second.WindowGoal.ExteriorWindowFraction, Is.EqualTo(0.5f));
        Assert.That(second.WindowGoal.HardFraction, Is.True);
        Assert.That(second.WindowGoal.ToleranceCells, Is.EqualTo(1));
        Assert.That(second.WindowGoal.MinimumCount, Is.EqualTo(1));
        Assert.That(second.WindowGoal.MaximumCount, Is.EqualTo(2));
        Assert.That(blob.Profile.GenerationGoals!.SizeMix.Count, Is.EqualTo(2));
        Assert.Throws<NotSupportedException>(() => ((IList<KsProcgenAreaSizeGoal>) blob.Profile.GenerationGoals.SizeMix).Clear());
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void FallbackPermissionsAreTransportedIndependently(int permitted)
    {
        var spec = Goals();
        spec.FallbackPolicy.AllowMergedPartition = permitted == 0;
        spec.FallbackPolicy.AllowRoomSizeShortfall = permitted == 1;
        spec.FallbackPolicy.AllowSparseFurnishing = permitted == 2;
        spec.FallbackPolicy.AllowSparseLighting = permitted == 3;
        Assert.That(KsProcgenAreaGoals.TrySnapshot(spec, out var goals), Is.True);
        var policy = Normalize(goals!).Blobs.Single().CreateRequest(1).FallbackPolicy;
        Assert.That(new[] { policy.AllowMergedPartition, policy.AllowRoomSizeShortfall,
                policy.AllowSparseFurnishing, policy.AllowSparseLighting },
            Is.EqualTo(Enumerable.Range(0, 4).Select(index => index == permitted)));
    }

    [Test]
    public void SnapshotHashIncludesAllPolicyAndWindowFieldsAndCanonicalSizeGoals()
    {
        Assert.That(KsProcgenAreaGoals.TrySnapshot(Goals(), out var goals), Is.True);
        var original = Normalize(goals!);
        var id = original.Blobs.Single().Id;
        var reordered = goals! with { SizeMix = goals!.SizeMix.Reverse().ToArray() };
        Assert.That(Normalize(reordered).SnapshotHash, Is.EqualTo(original.SnapshotHash));
        var changes = new List<KsProcgenAreaGoalsProfile>
        {
            goals with { SizeMix = goals.SizeMix.Select(goal => goal.Id == "Small" ? goal with { TargetCount = 2 } : goal).ToArray() },
            goals with { SizeMix = goals.SizeMix.Select(goal => goal.Id == "Small" ? goal with { MinCells = 3 } : goal).ToArray() },
            goals with { SizeMix = goals.SizeMix.Select(goal => goal.Id == "Small" ? goal with { MaxCells = 9 } : goal).ToArray() },
            goals with { SizeMix = goals.SizeMix.Select(goal => goal.Id == "Small" ? goal with { Id = "Smaller" } : goal).ToArray() },
            goals with { FallbackPolicy = goals.FallbackPolicy with { AllowMergedPartition = true } },
            goals with { FallbackPolicy = goals.FallbackPolicy with { AllowRoomSizeShortfall = true } },
            goals with { FallbackPolicy = goals.FallbackPolicy with { AllowSparseFurnishing = true } },
            goals with { FallbackPolicy = goals.FallbackPolicy with { AllowSparseLighting = true } },
            goals with { WindowGoal = goals.WindowGoal with { ExteriorWindowFraction = 0.25f } },
            goals with { WindowGoal = goals.WindowGoal with { HardFraction = false } },
            goals with { WindowGoal = goals.WindowGoal with { ToleranceCells = 2 } },
            goals with { WindowGoal = goals.WindowGoal with { MinimumCount = 0 } },
            goals with { WindowGoal = goals.WindowGoal with { MaximumCount = null } },
            goals with { WindowGoal = goals.WindowGoal with { MaximumCount = 3 } },
        };
        foreach (var changed in changes)
        {
            var snapshot = Normalize(changed);
            Assert.That(snapshot.Status, Is.EqualTo(KsProcgenStatus.Success), snapshot.Issue?.Code);
            Assert.That(snapshot.SnapshotHash, Is.Not.EqualTo(original.SnapshotHash));
            Assert.That(snapshot.Blobs.Single().Id, Is.EqualTo(id));
        }
    }

    [Test]
    public void ValueProfileListsAreFrozenAndOmissionRetainsFreshRequestDefaults()
    {
        Assert.That(KsProcgenAreaGoals.TrySnapshot(Goals(), out var goals), Is.True);
        var list = goals!.SizeMix.ToList();
        var snapshot = Normalize(goals with { SizeMix = list });
        list.Clear();
        Assert.That(snapshot.Blobs.Single().CreateRequest(1).SizeMix.Count, Is.EqualTo(2));
        var absent = Normalize(null).Blobs.Single();
        var first = absent.CreateRequest(1);
        var second = absent.CreateRequest(1);
        Assert.That(first.SizeMix, Is.Empty);
        Assert.That(first.FallbackPolicy.AllowMergedPartition && first.FallbackPolicy.AllowRoomSizeShortfall &&
            first.FallbackPolicy.AllowSparseFurnishing && first.FallbackPolicy.AllowSparseLighting, Is.True);
        Assert.That(first.WindowGoal.ExteriorWindowFraction, Is.EqualTo(0.25f));
        Assert.That(first.WindowGoal.MaximumCount, Is.Null);
        Assert.That(first.FallbackPolicy, Is.Not.SameAs(second.FallbackPolicy));
        Assert.That(first.WindowGoal, Is.Not.SameAs(second.WindowGoal));
        Assert.That(KsProcgenAreaGoals.TrySnapshot(new(), out var defaults), Is.True);
        Assert.That(Normalize(defaults!).SnapshotHash, Is.Not.EqualTo(Normalize(null).SnapshotHash));
    }

    [Test]
    public void OmittedAndExplicitZeroWindowMaximumHaveDistinctIdentityAndRequests()
    {
        Assert.That(KsProcgenAreaGoals.TrySnapshot(new(), out var defaults), Is.True);
        var unlimited = Normalize(defaults!);
        var zero = Normalize(defaults! with { WindowGoal = defaults!.WindowGoal with { MaximumCount = 0 } });
        Assert.That(zero.Status, Is.EqualTo(KsProcgenStatus.Success), zero.Issue?.Code);
        Assert.That(zero.SnapshotHash, Is.Not.EqualTo(unlimited.SnapshotHash));
        Assert.That(zero.Blobs.Single().Id, Is.EqualTo(unlimited.Blobs.Single().Id));
        Assert.That(zero.Blobs.Single().CreateRequest(1).WindowGoal.MaximumCount, Is.EqualTo(0));
        Assert.That(unlimited.Blobs.Single().CreateRequest(1).WindowGoal.MaximumCount, Is.Null);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(6)]
    [TestCase(7)]
    [TestCase(8)]
    [TestCase(9)]
    [TestCase(10)]
    [TestCase(11)]
    [TestCase(12)]
    public void MalformedAuthoredGoalsHaveNoPartialProfile(int variant)
    {
        var spec = Goals();
        switch (variant)
        {
            case 0: spec.SizeMix = null!; break;
            case 1: spec.FallbackPolicy = null!; break;
            case 2: spec.WindowGoal = null!; break;
            case 3: spec.SizeMix.Add(null!); break;
            case 4: spec.SizeMix.Add(spec.SizeMix[0]); break;
            case 5: spec.SizeMix[0].Id = new string('x', 129); break;
            case 6: spec.SizeMix[0].MinCells = 0; break;
            case 7: spec.SizeMix[0].MaxCells = 20; break;
            case 8: spec.SizeMix[0].TargetCount = 4_096; break;
            case 9: spec.SizeMix = Enumerable.Range(1, 65).Select(index => new KsProcgenRoomSizeGoal
                { Id = $"Size{index}", MinCells = index, MaxCells = index }).ToList(); break;
            case 10: spec.WindowGoal.ExteriorWindowFraction = float.NaN; break;
            case 11: spec.WindowGoal.MaximumCount = 0; break;
            default: spec.WindowGoal.ToleranceCells = -1; break;
        }
        Assert.That(KsProcgenAreaGoals.TrySnapshot(spec, out var profile), Is.False);
        Assert.That(profile, Is.Null);
    }

    [Test]
    public void ForgedValueProfilesAreRejectedWithoutBlobArtifacts()
    {
        Assert.That(KsProcgenAreaGoals.TrySnapshot(Goals(), out var goals), Is.True);
        foreach (var invalid in new[]
        {
            goals! with { SizeMix = null! },
            goals! with { FallbackPolicy = null! },
            goals! with { WindowGoal = null! },
            goals! with { WindowGoal = goals!.WindowGoal with { ExteriorWindowFraction = float.PositiveInfinity } },
            goals! with { SizeMix = new[] { new KsProcgenAreaSizeGoal("Bad", 0, 8, 1) } },
        })
        {
            var result = Normalize(invalid);
            Assert.That(result.Issue?.Code, Is.EqualTo("InvalidAreaGenerationGoals"));
            Assert.That(result.Blobs, Is.Empty);
            Assert.That(result.SnapshotHash, Is.Zero);
        }
    }

    private static KsProcgenAreaGoalsSpec Goals() => new()
    {
        SizeMix = new List<KsProcgenRoomSizeGoal>
        {
            new() { Id = "Small", MinCells = 4, MaxCells = 8, TargetCount = 1 },
            new() { Id = "Medium", MinCells = 12, MaxCells = 24, TargetCount = 1 },
        },
        FallbackPolicy = new() { AllowMergedPartition = false, AllowRoomSizeShortfall = false,
            AllowSparseFurnishing = false, AllowSparseLighting = false },
        WindowGoal = new() { ExteriorWindowFraction = 0.5f, HardFraction = true, ToleranceCells = 1, MinimumCount = 1, MaximumCount = 2 },
    };

    private static KsProcgenAreaNormalization Normalize(KsProcgenAreaGoalsProfile? goals) => KsProcgenAreaNormalizer.Normalize(
        "Host", new[] { new KsProcgenAreaMarker(new Vector2i(0, 0), "Default", "Office") },
        new[] { new KsProcgenAreaProfile("Office", KsProcgenMode.Procedural, KsProcgenGeometryMode.Footprint,
            KsProcgenConnectivityPolicy.SingleNetwork, "Theme", 256, 100) { GenerationGoals = goals } });
}
