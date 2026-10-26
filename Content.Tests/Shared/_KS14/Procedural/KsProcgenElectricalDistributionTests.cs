#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenElectricalDistributionTests
{
    [Test]
    public void ExplicitUpgradeAddsNewDefaultsWithoutReinterpretingHistoricalSnapshots()
    {
        var legacy = new KsProcgenElectricalSpec { Mode = KsProcgenElectricalMode.AutoDistribute };
        var old = Snapshot(legacy);
        Assert.That(old.SchemaVersion, Is.EqualTo(1));
        Assert.That(old.Distribution, Is.Null);
        Assert.That(KsProcgenElectricalProfiles.TryUpgrade(legacy, out var upgraded, out var issue), Is.True, issue?.Code);
        var current = Snapshot(upgraded!);
        Assert.That(current.SchemaVersion, Is.EqualTo(KsProcgenElectricalProfiles.CurrentSchemaVersion));
        Assert.That(current.Distribution!.ClosetInteriorSizes, Is.EqualTo(new[] { new Vector2i(1, 2), new(2, 2) }));
        Assert.That(current.Distribution.ClosetTheme, Is.EqualTo("KsProcgenElectricalCloset"));
        Assert.That(current.Distribution.MediumVoltageCirculationBackbone, Is.True);
        Assert.That(current.Distribution.MediumVoltageRoomEntryBranches, Is.True);
        Assert.That(current.Distribution.FloorServiceFraction, Is.EqualTo(1f));
        Assert.That(current.Distribution.FloorServiceDomain, Is.EqualTo(KsProcgenElectricalFloorDomain.UsableRoomFloor));
        Assert.That(current.Distribution.LowVoltageSquares, Is.EqualTo(KsProcgenElectricalSquarePolicy.RequireNecessityCertificate));
        Assert.That(current.SemanticHash, Is.Not.EqualTo(old.SemanticHash));
        Assert.That(legacy.SchemaVersion, Is.EqualTo(1));
        Assert.That(legacy.Distribution, Is.Null);
        Assert.That(Snapshot(legacy).SemanticHash, Is.EqualTo(old.SemanticHash));
        Assert.That(KsProcgenElectricalProfiles.TryUpgrade(upgraded!, out var twice, out issue), Is.True, issue?.Code);
        Assert.That(Snapshot(twice!).SemanticHash, Is.EqualTo(current.SemanticHash));
        upgraded!.Distribution!.ClosetInteriorSizes.Clear();
        Assert.That(current.Distribution.ClosetInteriorSizes.Count, Is.EqualTo(2));
    }

    [Test]
    public void EveryDistributionFieldChangesFrozenRequestAndDraftIdentity()
    {
        var original = Snapshot(Spec());
        var request = Request(original.CopySpec());
        Assert.That(KsProcgenRequestSnapshot.TryCapture(request, out var originalRequest, out var issue), Is.True, issue?.Code);
        var originalDraft = Draft(original.CopySpec());
        Action<KsProcgenElectricalDistributionSpec>[] changes =
        [
            spec => spec.ClosetInteriorSizes.RemoveAt(0),
            spec => spec.ClosetTheme = "OtherElectricalCloset",
            spec => spec.MediumVoltageCirculationBackbone = false,
            spec => spec.MediumVoltageRoomEntryBranches = false,
            spec => spec.FloorServiceFraction = 0.75f,
            spec => spec.FloorServiceDomain = KsProcgenElectricalFloorDomain.AllRoomFloor,
            spec => spec.LowVoltageSquares = KsProcgenElectricalSquarePolicy.ForbidNew,
        ];
        foreach (var change in changes)
        {
            var spec = Spec(); change(spec.Distribution!);
            Assert.That(Snapshot(spec).SemanticHash, Is.Not.EqualTo(original.SemanticHash));
            Assert.That(KsProcgenRequestSnapshot.TryCapture(Request(spec), out var changed, out issue), Is.True, issue?.Code);
            Assert.That(changed!.DeclarationHash, Is.Not.EqualTo(originalRequest!.DeclarationHash));
            Assert.That(Draft(spec).ContentHash, Is.Not.EqualTo(originalDraft.ContentHash));
        }
    }

    [Test]
    public void NestedCopiesAndPaintedBlobRequestsCannotMutateRetainedIntent()
    {
        var spec = Spec(); var profile = Snapshot(spec); var hash = profile.SemanticHash;
        var draft = Draft(spec);
        var area = KsProcgenAreaNormalizer.Normalize("Host", [new(new Vector2i(0, 0), "Main", "Room")],
            [new("Room", KsProcgenMode.Procedural, KsProcgenGeometryMode.Footprint,
                KsProcgenConnectivityPolicy.PerIsland, null, 64, 100) { Electrical = profile }]);
        Assert.That(area.Issue, Is.Null);
        var request = area.Blobs.Single().CreateRequest(17);
        Assert.That(KsProcgenRequestSnapshot.TryCapture(request, out var frozen, out var issue), Is.True, issue?.Code);
        var copy = frozen!.CopyRequest();
        spec.Distribution!.ClosetInteriorSizes.Clear();
        spec.Distribution.ClosetTheme = "Changed";
        copy.Electrical!.Distribution!.FloorServiceFraction = 0f;
        copy.Electrical.Distribution.ClosetInteriorSizes.Clear();
        draft.CopySpec().Electrical["Power"].Distribution!.ClosetInteriorSizes.Clear();
        request.Electrical!.Distribution!.ClosetTheme = "Changed";
        Assert.That(profile.SemanticHash, Is.EqualTo(hash));
        Assert.That(frozen.Electrical!.SemanticHash, Is.EqualTo(hash));
        Assert.That(draft.Electrical.Single().Profile.Distribution!.ClosetInteriorSizes.Count, Is.EqualTo(2));
        Assert.That(area.Blobs.Single().CreateRequest(17).Electrical!.Distribution!.ClosetTheme, Is.EqualTo("KsProcgenElectricalCloset"));
        Assert.Throws<NotSupportedException>(() => ((IList<Vector2i>) profile.Distribution!.ClosetInteriorSizes).Clear());
    }

    [Test]
    public void RotatedSizeSetsAndExplicitDefaultsHaveCanonicalIdentity()
    {
        var explicitDefaults = Spec(); var expected = Snapshot(explicitDefaults).SemanticHash;
        explicitDefaults.Distribution!.ClosetInteriorSizes = new() { new(2, 2), new(2, 1) };
        Assert.That(Snapshot(explicitDefaults).SemanticHash, Is.EqualTo(expected));
        explicitDefaults.Distribution = null;
        var implicitDefaults = Snapshot(explicitDefaults);
        Assert.That(implicitDefaults.SemanticHash, Is.EqualTo(expected));
        Assert.That(implicitDefaults.CopySpec().Distribution, Is.Not.Null);
        explicitDefaults.Distribution = new() { FloorServiceFraction = -0f };
        var zero = Snapshot(explicitDefaults);
        explicitDefaults.Distribution.FloorServiceFraction = 0f;
        Assert.That(Snapshot(explicitDefaults).SemanticHash, Is.EqualTo(zero.SemanticHash));
    }

    [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)]
    [TestCase(6)] [TestCase(7)] [TestCase(8)] [TestCase(9)] [TestCase(10)] [TestCase(11)]
    [TestCase(12)] [TestCase(13)] [TestCase(14)] [TestCase(15)]
    public void InvalidOrUnversionedIntentCannotSurviveAnyImportPath(int defect)
    {
        var spec = Spec(); var distribution = spec.Distribution!;
        switch (defect)
        {
            case 0: spec.SchemaVersion = 1; break;
            case 1: spec.SchemaVersion = 3; break;
            case 2: distribution.ClosetInteriorSizes = null!; break;
            case 3: distribution.ClosetInteriorSizes.Clear(); break;
            case 4: distribution.ClosetInteriorSizes = new() { new(1, 2), new(2, 1) }; break;
            case 5: distribution.ClosetInteriorSizes.Add(new(1, 2)); break;
            case 6: distribution.ClosetInteriorSizes = new() { new(1, 3) }; break;
            case 7: distribution.ClosetInteriorSizes = new() { new(int.MaxValue, 2) }; break;
            case 8: distribution.ClosetTheme = ""; break;
            case 9: distribution.ClosetTheme = null!; break;
            case 10: distribution.FloorServiceFraction = float.NaN; break;
            case 11: distribution.FloorServiceFraction = float.PositiveInfinity; break;
            case 12: distribution.FloorServiceFraction = -0.01f; break;
            case 13: distribution.FloorServiceFraction = 1.01f; break;
            case 14: distribution.FloorServiceDomain = (KsProcgenElectricalFloorDomain) 255; break;
            case 15: distribution.LowVoltageSquares = (KsProcgenElectricalSquarePolicy) 255; break;
        }
        Assert.That(KsProcgenElectricalProfiles.TrySnapshot(spec, out var profile, out var issue), Is.False);
        Assert.That(profile, Is.Null); Assert.That(issue, Is.Not.Null);
        Assert.That(KsProcgenElectricalProfiles.TryUpgrade(spec, out var upgraded, out issue), Is.False);
        Assert.That(upgraded, Is.Null);
        Assert.That(KsProcgenRequestSnapshot.TryCapture(Request(spec), out var request, out issue), Is.False);
        Assert.That(request, Is.Null);
        Assert.That(KsProcgenUtilityDraft.TrySnapshot(new() { DocumentId = "Utilities", Electrical = new() { ["Power"] = spec } },
            _ => true, _ => true, out var draft, out issue), Is.False);
        Assert.That(draft, Is.Null);
    }

    [Test]
    public void NewIntentCannotFallThroughToTheLegacySharedHomePlanner()
    {
        var spec = Spec(); spec.Mode = KsProcgenElectricalMode.AutoDistribute;
        spec.SupplyRequirement = KsProcgenElectricalSupply.WiredOnly;
        KsProcgenElectricalPrototypeFact[] facts =
        [
            new("CableHV", true, false, false, false, 0, null),
            new("CableMV", true, false, false, false, 0, null),
            new("CableApcExtension", true, false, false, false, 0, null),
            new("SubstationBasic", true, false, false, false, 0, null),
            new("APCBasic", true, false, false, false, 0, null, WallMounted: true),
        ];
        Assert.That(KsProcgenElectricalPlanningContext.TryCreate(Snapshot(spec), facts, "facts", 3, out var context, out var issue), Is.False);
        Assert.That(context, Is.Null);
        Assert.That(issue?.Code, Is.EqualTo("ElectricalUtilityRoomContextRequired"));
        spec.SchemaVersion = 1; spec.Distribution = null;
        Assert.That(KsProcgenElectricalPlanningContext.TryCreate(Snapshot(spec), facts, "facts", 3, out context, out issue), Is.True, issue?.Code);
    }

    private static KsProcgenElectricalSpec Spec() => new() { SchemaVersion = 2, Distribution = new() };
    private static KsProcgenRequest Request(KsProcgenElectricalSpec spec) => new()
    { RequestId = "Power", Electrical = spec, Shape = new() { Cells = new() { new(0, 0) } } };
    private static KsProcgenElectricalProfile Snapshot(KsProcgenElectricalSpec spec)
    {
        Assert.That(KsProcgenElectricalProfiles.TrySnapshot(spec, out var profile, out var issue), Is.True, issue?.Code);
        return profile!;
    }
    private static KsProcgenUtilityDraft Draft(KsProcgenElectricalSpec spec)
    {
        Assert.That(KsProcgenUtilityDraft.TrySnapshot(new() { DocumentId = "Utilities", Electrical = new() { ["Power"] = spec } },
            _ => true, _ => true, out var draft, out var issue), Is.True, issue?.Code);
        return draft!;
    }
}
