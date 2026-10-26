#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenElectricalProfileTests
{
    [Test]
    public void NestedAuthoringDataAndEditableCopiesCannotChangeAnEarlierSnapshot()
    {
        var spec = Spec();
        var profile = Snapshot(spec);
        var hash = profile.SemanticHash;
        spec.Substations[0].Weight = 50f;
        spec.Interfaces[0].AvailableWatts = 0f;
        spec.CircuitIds.Clear();
        Assert.That(profile.SemanticHash, Is.EqualTo(hash));
        var copy = profile.CopySpec();
        Assert.That(Snapshot(copy).SemanticHash, Is.EqualTo(hash));
        copy.Substations[0].Prototype = "Other";
        copy.Apcs.Clear();
        copy.Interfaces[0].NetworkKey = "Other";
        Assert.That(profile.Substations[0].Prototype, Is.EqualTo("SubstationBasic"));
        Assert.That(profile.Interfaces[0].NetworkKey, Is.EqualTo("StationHV"));
        Assert.That(profile.Apcs.Count, Is.EqualTo(1));
        Assert.Throws<NotSupportedException>(() => ((IList<KsProcgenElectricalDevice>) profile.Substations).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<KsProcgenElectricalInterface>) profile.Interfaces).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<string>) profile.CircuitIds).Clear());
    }

    [Test]
    public void SetOrderingAndNegativeZeroDoNotChangeSemanticIdentity()
    {
        var spec = Spec();
        spec.Substations.Add(new() { Prototype = "SubstationWallBasic", Weight = 0f });
        spec.Interfaces.Add(new() { Id = "Other", NetworkKey = "Spare", Cell = new(4, 0), AvailableWatts = 0f });
        spec.CircuitPolicy = KsProcgenElectricalCircuitPolicy.Independent;
        spec.CircuitIds.Add("Spare");
        var original = Snapshot(spec);
        spec.Substations.Reverse(); spec.Interfaces.Reverse(); spec.CircuitIds.Reverse();
        spec.Substations.Single(choice => choice.Weight == 0f).Weight = -0f;
        spec.Interfaces.Single(port => port.Id == "Other").AvailableWatts = -0f;
        Assert.That(Snapshot(spec).SemanticHash, Is.EqualTo(original.SemanticHash));
    }

    [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)]
    [TestCase(6)] [TestCase(7)] [TestCase(8)] [TestCase(9)] [TestCase(10)] [TestCase(11)]
    [TestCase(12)] [TestCase(13)] [TestCase(14)] [TestCase(15)] [TestCase(16)] [TestCase(17)]
    [TestCase(18)] [TestCase(19)] [TestCase(20)] [TestCase(21)] [TestCase(22)] [TestCase(23)]
    public void InvalidDeclarationsLeaveNoProfileOrNormalizedShape(int defect)
    {
        var spec = Spec();
        switch (defect)
        {
            case 0: spec.SchemaVersion = 3; break;
            case 1: spec.Mode = (KsProcgenElectricalMode) 255; break;
            case 2: spec.HighVoltageCable = ""; break;
            case 3: spec.Substations = null!; break;
            case 4: spec.Substations[0].Weight = float.NaN; break;
            case 5: spec.Substations[0].Weight = 0f; break;
            case 6: spec.Apcs.Add(spec.Apcs[0]); break;
            case 7: spec.CircuitIds.Clear(); break;
            case 8: spec.CircuitIds.Add("Main"); break;
            case 9: spec.CircuitIds.Add("Spare"); break;
            case 10: spec.DemandMargin = float.PositiveInfinity; break;
            case 11: spec.DemandMargin = -0.1f; break;
            case 12: spec.StartupTicks = -1; break;
            case 13: spec.ObservationTicks = 0; break;
            case 14: spec.InitialChargeFraction = 1.1f; break;
            case 15: spec.Interfaces = null!; break;
            case 16: spec.Interfaces.Add(spec.Interfaces[0]); break;
            case 17: spec.Interfaces[0].Voltage = (KsProcgenElectricalVoltage) 255; break;
            case 18: spec.Interfaces[0].Cell = new(int.MinValue, 0); break;
            case 19: spec.Interfaces[0].AvailableWatts = float.NaN; break;
            case 20: spec.Interfaces[0].NetworkKey = ""; break;
            case 21: spec.Interfaces[0].CircuitId = "Unknown"; break;
            case 22: spec.Interfaces[0].Connection = (KsProcgenElectricalConnection) 255; break;
            case 23: spec.Interfaces.Add(null!); break;
        }
        Assert.That(KsProcgenElectricalProfiles.TrySnapshot(spec, out var profile, out var issue), Is.False);
        Assert.That(profile, Is.Null);
        Assert.That(issue, Is.Not.Null);
        var request = new KsProcgenRequest { RequestId = "Power", Electrical = spec, Shape = new() { Cells = [new(0, 0)] } };
        Assert.That(KsProcgenGeometry.TryNormalize(request, out var shape, out _), Is.False);
        Assert.That(shape, Is.Null);
    }

    [Test]
    public void EveryPolicyAndInterfaceFieldAffectsIdentity()
    {
        var original = Snapshot(Spec()).SemanticHash;
        Action<KsProcgenElectricalSpec>[] changes =
        [
            spec => spec.Mode = KsProcgenElectricalMode.ValidateExisting,
            spec => spec.HighVoltageCable = "OtherHV",
            spec => spec.MediumVoltageCable = "OtherMV",
            spec => spec.LowVoltageCable = "OtherLV",
            spec => spec.Substations[0].Weight = 2f,
            spec => spec.Apcs[0].Prototype = "OtherAPC",
            spec => spec.SupplyRequirement = KsProcgenElectricalSupply.WiredOnly,
            spec => spec.DemandMargin = 0.3f,
            spec => spec.StartupTicks = 10,
            spec => spec.ObservationTicks = 100,
            spec => spec.AllowBatteryOnly = true,
            spec => spec.InitialChargeFraction = 0f,
            spec => spec.ReuseExistingCables = false,
            spec => spec.CircuitPolicy = KsProcgenElectricalCircuitPolicy.Independent,
            spec => spec.Interfaces[0].Id = "Other",
            spec => spec.Interfaces[0].Cell = new(1, 0),
            spec => spec.Interfaces[0].Voltage = KsProcgenElectricalVoltage.Medium,
            spec => spec.Interfaces[0].NetworkKey = "OtherNet",
            spec => spec.Interfaces[0].AvailableWatts = 50f,
            spec => spec.Interfaces[0].Enabled = false,
            spec => spec.Interfaces[0].Connection = KsProcgenElectricalConnection.JoinAtCell,
        ];
        foreach (var change in changes)
        {
            var spec = Spec(); change(spec);
            Assert.That(Snapshot(spec).SemanticHash, Is.Not.EqualTo(original));
        }
    }

    [Test]
    public void PaletteAndInterfaceBoundsAreCheckedBeforeExpansion()
    {
        var spec = Spec();
        spec.Substations = Enumerable.Range(0, 65).Select(index => new KsProcgenElectricalDeviceSpec { Prototype = $"Substation{index}" }).ToList();
        Assert.That(KsProcgenElectricalProfiles.TrySnapshot(spec, out _, out _), Is.False);
        spec = Spec();
        spec.Interfaces = Enumerable.Range(0, 1025).Select(index => new KsProcgenElectricalInterfaceSpec { Id = $"Input{index}", NetworkKey = "Net" }).ToList();
        Assert.That(KsProcgenElectricalProfiles.TrySnapshot(spec, out _, out _), Is.False);
        Assert.That(KsProcgenElectricalProfiles.TrySnapshot(Spec(), out _, out var issue, entityExists: id => id != "CableHV"), Is.False);
        Assert.That(issue?.Code, Is.EqualTo("InvalidElectricalPalette"));
    }

    [Test]
    public void DefaultCoverageIsAnInclusiveCircleOfTwentyNineCenteredTiles()
    {
        var cells = Enumerable.Range(-4, 9).SelectMany(x => Enumerable.Range(-4, 9).Select(y => new Vector2(x, y)))
            .Where(position => KsProcgenElectricalReach.WithinRange(Vector2.Zero, position, 3, 3)).ToArray();
        Assert.That(cells.Length, Is.EqualTo(29));
        Assert.That(cells, Does.Contain(new Vector2(3f, 0f)));
        Assert.That(cells, Does.Contain(new Vector2(2f, 2f)));
        Assert.That(cells, Does.Not.Contain(new Vector2(3f, 1f)));
        Assert.That(KsProcgenElectricalReach.WithinRange(Vector2.Zero, new(2f, 2f), 2, 3), Is.False);
        Assert.That(KsProcgenElectricalReach.WithinRange(new(0.5f, 0.5f), new(3.5f, 0.5f), 4, 3), Is.True);
        Assert.That(KsProcgenElectricalReach.WithinRange(Vector2.Zero, new(3.0001f, 0f), 3, 3), Is.False);
        Assert.That(KsProcgenElectricalReach.WithinRange(Vector2.Zero, Vector2.Zero, 0, 3), Is.True);
        Assert.That(KsProcgenElectricalReach.WithinRange(new(float.NaN, 0f), Vector2.Zero, 3, 3), Is.False);
        Assert.That(KsProcgenElectricalReach.WithinRange(Vector2.Zero, Vector2.Zero, -1, 3), Is.False);
    }

    [Test]
    public void PaintedBlobTransportKeepsElectricalSettingsWithoutMutableAliases()
    {
        var profile = Snapshot(Spec());
        var snapshot = Area(profile);
        var blob = snapshot.Blobs.Single();
        var request = blob.CreateRequest(1);
        Assert.That(request.Electrical, Is.Not.Null);
        request.Electrical!.Interfaces.Clear();
        Assert.That(blob.CreateRequest(1).Electrical!.Interfaces.Count, Is.EqualTo(1));
        Assert.That(Area(null).SnapshotHash, Is.Not.EqualTo(snapshot.SnapshotHash));
        Assert.That(Area(null).Blobs.Single().Id, Is.EqualTo(blob.Id));
        Assert.That(blob.Profile.Electrical!.SemanticHash, Is.EqualTo(profile.SemanticHash));
    }

    private static KsProcgenAreaNormalization Area(KsProcgenElectricalProfile? profile) => KsProcgenAreaNormalizer.Normalize(
        "Host", [new(new Vector2i(0, 0), "Main", "Room")],
        [new("Room", KsProcgenMode.Procedural, KsProcgenGeometryMode.Footprint,
            KsProcgenConnectivityPolicy.PerIsland, null, 64, 100) { Electrical = profile }]);

    private static KsProcgenElectricalSpec Spec() => new()
    {
        Mode = KsProcgenElectricalMode.AutoDistribute,
        Interfaces = [new() { Id = "Input", NetworkKey = "StationHV", AvailableWatts = 10000f }],
    };

    private static KsProcgenElectricalProfile Snapshot(KsProcgenElectricalSpec spec)
    {
        Assert.That(KsProcgenElectricalProfiles.TrySnapshot(spec, out var profile, out var issue), Is.True, issue?.Code);
        return profile!;
    }
}
