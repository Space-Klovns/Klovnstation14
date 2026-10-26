#nullable enable
using System;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenRequestSnapshotTests
{
    [Test]
    public void EquivalentMasksAndSetOrderProduceIdenticalCanonicalInput()
    {
        var request = Spec();
        var original = Capture(request);
        request.Shape.AddRectangles.Clear();
        request.Shape.Cells = Enumerable.Range(0, 3).SelectMany(y => Enumerable.Range(0, 4).Select(x => new Vector2i(x, y))).Reverse().ToList();
        request.RootCells.Add(request.RootCells[0]);
        request.ConstantRegions[0].LocalCells.Reverse();
        request.SizeMix.Reverse();
        Assert.That(Capture(request).DeclarationHash, Is.EqualTo(original.DeclarationHash));
        var canonical = original.CopyRequest();
        Assert.That(canonical.Shape.AddRectangles, Is.Empty);
        Assert.That(canonical.Shape.TextMask, Is.Null);
        Assert.That(canonical.Shape.Cells.Count, Is.EqualTo(12));
        Assert.That(canonical.Shape.PreservedCells, Is.EqualTo(new[] { new Vector2i(3, 2) }));
        Assert.That(KsProcgenGeometry.TryNormalize(canonical, out var normalized, out var issue), Is.True, issue?.Code);
        Assert.That(normalized!.PreservedCells.Count, Is.EqualTo(3));
        Assert.That(Capture(canonical).DeclarationHash, Is.EqualTo(original.DeclarationHash));
    }

    [Test]
    public void OriginalAndReturnedNestedObjectsCannotChangeRetainedInput()
    {
        var request = Spec();
        var snapshot = Capture(request);
        request.Shape.AddRectangles[0].Max = new(1, 1);
        request.RootCells.Clear();
        request.ConstantRegions[0].Ports[0].Id = "Changed";
        request.EntranceDomain!.Configurations[0].Groups[0].Ports.Clear();
        request.WindowGoal.ExteriorWindowFraction = 0.9f;
        request.FallbackPolicy.AllowSparseLighting = false;
        request.Atmosphere!.PressureKPa = 200f;
        request.Electrical!.Interfaces[0].AvailableWatts = 0f;
        var edit = snapshot.CopyRequest();
        Assert.That(edit.WindowGoal.ExteriorWindowFraction, Is.EqualTo(0.25f));
        Assert.That(edit.Shape.Cells.Count, Is.EqualTo(12));
        Assert.That(edit.ConstantRegions[0].Ports[0].Id, Is.EqualTo("Door"));
        edit.Shape.Cells.Clear();
        edit.Limits.MaxCells = 1;
        edit.ConstantRegions[0].LocalCells.Clear();
        edit.EntranceDomain!.Entrances[0].ThresholdCells.Clear();
        edit.EntranceDomain.Configurations[0].Groups[0].RootCells.Clear();
        edit.SizeMix[0].TargetCount = 999;
        edit.WindowGoal.MinimumCount = 40;
        edit.FallbackPolicy.AllowSparseLighting = false;
        edit.Atmosphere!.MoleFractions.Clear();
        edit.Electrical!.Interfaces.Clear();
        var retained = snapshot.CopyRequest();
        Assert.That(Capture(retained).DeclarationHash, Is.EqualTo(snapshot.DeclarationHash));
        Assert.That(retained.WindowGoal.MinimumCount, Is.Zero);
        Assert.That(retained.FallbackPolicy.AllowSparseLighting, Is.True);
        Assert.That(snapshot.Atmosphere!.PressureKPa, Is.EqualTo(101.325f));
        Assert.That(snapshot.Electrical!.Interfaces.Single().AvailableWatts, Is.EqualTo(500f));
    }

    [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)]
    [TestCase(6)] [TestCase(7)] [TestCase(8)] [TestCase(9)] [TestCase(10)] [TestCase(11)]
    [TestCase(12)] [TestCase(13)] [TestCase(14)] [TestCase(15)] [TestCase(16)] [TestCase(17)]
    [TestCase(18)] [TestCase(19)] [TestCase(20)] [TestCase(21)] [TestCase(22)] [TestCase(23)]
    [TestCase(24)] [TestCase(25)] [TestCase(26)] [TestCase(27)] [TestCase(28)] [TestCase(29)]
    [TestCase(30)] [TestCase(31)] [TestCase(32)] [TestCase(33)]
    public void BehaviorAndOwnershipChangesInvalidateIdentity(int change)
    {
        var request = Spec();
        var original = Capture(request);
        switch (change)
        {
            case 0: request.RequestId = "Other"; break;
            case 1: request.Seed++; break;
            case 2: request.Mode = KsProcgenMode.Hybrid; break;
            case 3: request.GeometryMode = KsProcgenGeometryMode.InteriorFill; break;
            case 4: request.ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland; break;
            case 5: request.Limits.MaxCells--; break;
            case 6: request.Limits.MaxAbsoluteCoordinate--; break;
            case 7: request.Shape.EnvelopeCells.Add(new(5, 5)); break;
            case 8: request.Shape.VoidCells.Add(new(5, 5)); break;
            case 9: request.Shape.PreservedCells.Add(new(2, 2)); break;
            case 10: request.RootCells.Add(new(2, 1)); break;
            case 11: request.ConstantRegions[0].ContentFingerprint = "OtherContent"; break;
            case 12: request.ConstantRegions[0].Ports[0].Id = "OtherDoor"; break;
            case 13: request.EntranceDomain!.Channel = "OtherChannel"; break;
            case 14: request.EntranceDomain!.Entrances[0].OptionalSealable = true; break;
            case 15: request.EntranceDomain!.Configurations[0].Weight = 2f; break;
            case 16: request.SizeMix[0].TargetCount++; break;
            case 17: request.WindowGoal.ExteriorWindowFraction = 0.5f; break;
            case 18: request.WindowGoal.MaximumCount = 0; break;
            case 19: request.FallbackPolicy.AllowMergedPartition = false; break;
            case 20: request.FallbackPolicy.AllowRoomSizeShortfall = false; break;
            case 21: request.FallbackPolicy.AllowSparseFurnishing = false; break;
            case 22: request.Atmosphere!.PressureKPa = 200f; break;
            case 23: request.Electrical!.Interfaces[0].AvailableWatts = 100f; break;
            case 24: request.FallbackPolicy.AllowSparseLighting = false; break;
            case 25: request.WindowGoal.HardFraction = true; break;
            case 26: request.WindowGoal.ToleranceCells = 2; break;
            case 27: request.WindowGoal.MinimumCount = 1; break;
            case 28: request.SizeMix[1].MaxCells = 13; break;
            case 29: request.EntranceDomain!.Configurations[0].Mode = KsProcgenEntranceConnectionMode.RequiredConnections; break;
            case 30: request.EntranceDomain!.Configurations[0].Groups[0].RootCells[0] = new(1, 1); break;
            case 31: request.AtmosphereProfile = "NamedInlineOverride"; break;
            case 32: request.ElectricalProfile = "NamedInlineOverride"; break;
            case 33: request.ConstantRegions[0].SourceId = "DifferentLandmarkSource"; break;
        }
        Assert.That(Capture(request).DeclarationHash, Is.Not.EqualTo(original.DeclarationHash));
    }

    [Test]
    public void UnknownSchemasAndUnresolvedUtilityNamesReturnNoSnapshot()
    {
        var request = Spec();
        request.SchemaVersion = 3;
        Assert.That(KsProcgenGeometry.TryNormalize(request, out var shape, out var issue), Is.False);
        Assert.That(shape, Is.Null);
        Assert.That(issue?.Code, Is.EqualTo("UnsupportedRequestSchema"));
        Assert.That(KsProcgenRequestSnapshot.TryCapture(request, out var snapshot, out issue), Is.False);
        Assert.That(snapshot, Is.Null);
        request.SchemaVersion = 1;
        request.Atmosphere = null;
        request.AtmosphereProfile = "NamedGas";
        Assert.That(KsProcgenRequestSnapshot.TryCapture(request, out snapshot, out issue), Is.False);
        Assert.That(snapshot, Is.Null);
        Assert.That(issue?.Code, Is.EqualTo("UnresolvedUtilityReference"));
        request.AtmosphereProfile = null;
        request.Electrical = null;
        request.ElectricalProfile = "NamedPower";
        Assert.That(KsProcgenRequestSnapshot.TryCapture(request, out snapshot, out issue), Is.False);
        Assert.That(issue?.Code, Is.EqualTo("UnresolvedUtilityReference"));
    }

    [Test]
    public void LegacyEmptyAndTinyRequestsKeepOmittedUtilitiesAndVersionOneDefaults()
    {
        foreach (var count in new[] { 0, 1, 3 })
        {
            var request = new KsProcgenRequest { RequestId = "Tiny", Shape = new() { Cells = Enumerable.Range(0, count).Select(x => new Vector2i(x, 0)).ToList() } };
            var snapshot = Capture(request);
            Assert.That(snapshot.SchemaVersion, Is.EqualTo(1));
            Assert.That(snapshot.CopyRequest().Shape.Cells.Count, Is.EqualTo(count));
            Assert.That(snapshot.Atmosphere, Is.Null);
            Assert.That(snapshot.Electrical, Is.Null);
            Assert.That(Capture(snapshot.CopyRequest()).DeclarationHash, Is.EqualTo(snapshot.DeclarationHash));
        }
    }

    [Test]
    public void ImplicitEntranceDefaultIsExpandedAndRemainsStableOnRecapture()
    {
        var request = Spec();
        request.EntranceDomain!.Configurations.Clear();
        var snapshot = Capture(request);
        Assert.That(snapshot.CopyRequest().EntranceDomain!.Configurations.Count, Is.EqualTo(1));
        Assert.That(Capture(snapshot.CopyRequest()).DeclarationHash, Is.EqualTo(snapshot.DeclarationHash));
        request.WindowGoal.ExteriorWindowFraction = -0f;
        var zero = Capture(request);
        request.WindowGoal.ExteriorWindowFraction = 0f;
        Assert.That(Capture(request).DeclarationHash, Is.EqualTo(zero.DeclarationHash));
    }

    private static KsProcgenRequestSnapshot Capture(KsProcgenRequest request)
    {
        Assert.That(KsProcgenRequestSnapshot.TryCapture(request, out var snapshot, out var issue), Is.True, issue?.Code);
        return snapshot!;
    }

    private static KsProcgenRequest Spec() => new()
    {
        RequestId = "Snapshot", Seed = 10,
        Shape = new()
        {
            AddRectangles = new() { new() { Min = new(0, 0), Max = new(4, 3) } },
            PreservedCells = new() { new(3, 2) },
        },
        RootCells = new() { new(0, 1) },
        ConstantRegions = new()
        {
            new()
            {
                Id = "Landmark", SourceId = "LandmarkRoom", ContentFingerprint = "LandmarkVersion1",
                LocalCells = new() { new(0, 0), new(1, 0) },
                Ports = new() { new() { Id = "Door", Threshold = new(1, 0), OutwardNormal = new(1, 0) } },
            },
        },
        EntranceDomain = new()
        {
            Entrances = new() { new() { PortId = "West", ThresholdCells = new() { new(-1, 1) }, InwardNormal = new(1, 0) } },
            Configurations = new()
            {
                new() { Id = "Connected", Groups = new() { new() { Id = "Network", Ports = new() { "West" }, RootCells = new() { new(0, 1) } } } },
            },
        },
        SizeMix = new()
        {
            new() { Id = "Small", MinCells = 1, MaxCells = 3, TargetCount = 2 },
            new() { Id = "Medium", MinCells = 4, MaxCells = 12, TargetCount = 1 },
        },
        Atmosphere = new()
        {
            Mode = KsProcgenAtmosphereMode.Fill, PressureKPa = 101.325f, TemperatureKelvin = 293.15f,
            MoleFractions = new() { ["Oxygen"] = 0.21f, ["Nitrogen"] = 0.79f },
        },
        Electrical = new()
        {
            Mode = KsProcgenElectricalMode.AutoDistribute,
            Interfaces = new() { new() { Id = "Source", Cell = new(0, 1), NetworkKey = "StationHV", AvailableWatts = 500f } },
        },
    };
}
