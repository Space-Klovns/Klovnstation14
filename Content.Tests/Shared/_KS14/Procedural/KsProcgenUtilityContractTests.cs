#nullable enable
using System;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenUtilityContractTests
{
    [Test]
    public void CapturePinsNestedDeclarationsAndCanonicalizesSets()
    {
        var request = Spec();
        var snapshot = Capture(request);
        var hash = snapshot.DeclarationHash;
        request.Utilities!.Consumers[0].NominalWatts = 900f;
        request.Utilities.GasAssignments[0].Profile!.TemperatureKelvin = 400f;
        request.Utilities.CableWriteCells!.Clear();
        var copy = snapshot.CopyRequest();
        Assert.That(copy.Utilities!.Consumers[0].NominalWatts, Is.EqualTo(50f));
        Assert.That(copy.Utilities.GasAssignments[0].Profile!.TemperatureKelvin, Is.EqualTo(293.15f));
        Assert.That(copy.Utilities.CableWriteCells!.Count, Is.EqualTo(2));
        copy.Utilities.Consumers.Clear();
        copy.Utilities.GasRestrictions.AllowedGases!.Clear();
        Assert.That(Capture(snapshot.CopyRequest()).DeclarationHash, Is.EqualTo(hash));
        Assert.That(snapshot.Utilities!.RequiresTargetBinding, Is.True);
        Assert.That(snapshot.SchemaVersion, Is.EqualTo(2));

        var reordered = snapshot.CopyRequest();
        reordered.Utilities!.CableWriteCells!.Reverse();
        reordered.Utilities.CableWriteCells.Add(new(0, 0));
        reordered.Utilities.GasRestrictions.AllowedGases!.Reverse();
        reordered.Utilities.Consumers.Reverse();
        Assert.That(Capture(reordered).DeclarationHash, Is.EqualTo(hash));
    }

    [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)]
    [TestCase(5)] [TestCase(6)] [TestCase(7)] [TestCase(8)] [TestCase(9)]
    [TestCase(10)] [TestCase(11)] [TestCase(12)] [TestCase(13)] [TestCase(14)]
    [TestCase(15)] [TestCase(16)] [TestCase(17)] [TestCase(18)] [TestCase(19)]
    [TestCase(20)] [TestCase(21)] [TestCase(22)] [TestCase(23)] [TestCase(24)]
    [TestCase(25)] [TestCase(26)] [TestCase(27)] [TestCase(28)] [TestCase(29)]
    public void InvalidDeclarationsAreRejectedBeforeCapture(int change)
    {
        var request = Spec();
        var utility = request.Utilities!;
        var consumer = utility.Consumers[0];
        switch (change)
        {
            case 0: request.SchemaVersion = 1; break;
            case 1: utility.SchemaVersion = 2; break;
            case 2: utility.GasAssignments.Add(utility.GasAssignments[0]); break;
            case 3: utility.GasAssignments[0].Scope = (KsProcgenUtilityScope) 250; break;
            case 4: utility.GasAssignments[0].ScopeId = "ForeignArea"; break;
            case 5: utility.GasAssignments[0].Profile = null; break;
            case 6: utility.Consumers.Add(consumer); break;
            case 7: consumer.NominalWatts = float.NaN; break;
            case 8: consumer.PeakWatts = -1f; break;
            case 9: consumer.PeakWatts = 1f; break;
            case 10: consumer.MinimumMatches = 4097; break;
            case 11: consumer.SelectorId = "Unexpected"; break;
            case 12: consumer.Scope = KsProcgenUtilityScope.Request; consumer.ScopeId = "Unexpected"; break;
            case 13: consumer.Scope = KsProcgenUtilityScope.Room; consumer.ScopeId = null; break;
            case 14: consumer.Selector = KsProcgenConsumerSelector.Member; consumer.SelectorId = "has spaces"; break;
            case 15: consumer.CircuitId = ""; break;
            case 16: consumer.Supply = (KsProcgenElectricalSupply) 250; break;
            case 17: utility.GasWriteCells = new() { new(30, 0) }; break;
            case 18: utility.CableWriteCells = new() { new(3, 0) }; break;
            case 19: utility.DeviceWriteCells = new() { new(4, 0) }; break;
            case 20: utility.GasRestrictions.AllowedModes = new(); break;
            case 21: utility.GasRestrictions.AllowedModes = new() { KsProcgenAtmosphereMode.Fill, KsProcgenAtmosphereMode.Fill }; break;
            case 22: utility.GasRestrictions.AllowedGases = new() { "UnknownGas" }; break;
            case 23: utility.GasRestrictions.MaximumPressureKPa = -1f; break;
            case 24: utility.GasRestrictions.MinimumTemperatureKelvin = 0f; break;
            case 25: utility.GasRestrictions.MinimumObservationTicks = 0; break;
            case 26: utility.GasRestrictions.MinimumPressureKPa = 200f; utility.GasRestrictions.MaximumPressureKPa = 100f; break;
            case 27: utility.GasRestrictions.MinimumTemperatureKelvin = 400f; utility.GasRestrictions.MaximumTemperatureKelvin = 300f; break;
            case 28: utility.GasAssignments = null!; break;
            case 29: utility.ElectricalRestrictions = null!; break;
        }
        Assert.That(KsProcgenRequestSnapshot.TryCapture(request, out var snapshot, out var issue), Is.False);
        Assert.That(snapshot, Is.Null);
        Assert.That(issue, Is.Not.Null);
        Assert.That(KsProcgenGeometry.TryNormalize(request, out var shape, out _), Is.False);
        Assert.That(shape, Is.Null);
    }

    [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)]
    [TestCase(5)] [TestCase(6)] [TestCase(7)] [TestCase(8)] [TestCase(9)]
    [TestCase(10)] [TestCase(11)] [TestCase(12)] [TestCase(13)] [TestCase(14)]
    public void RetainedRequirementsParticipateInIdentity(int change)
    {
        var request = Spec(); var hash = Capture(request).DeclarationHash; var utility = request.Utilities!;
        switch (change)
        {
            case 0: utility.GasAssignments[0].Profile!.PressureToleranceKPa = 2f; break;
            case 1: utility.Consumers[0].NominalWatts = null; break;
            case 2: utility.Consumers[0].PeakWatts = 110f; break;
            case 3: utility.Consumers[0].MinimumMatches = 0; break;
            case 4: utility.Consumers[0].Requirement = KsProcgenUtilityRequirement.Preferred; break;
            case 5: utility.Consumers[0].Supply = KsProcgenElectricalSupply.WiredOnly; break;
            case 6: utility.Consumers[0].Scope = KsProcgenUtilityScope.Room; utility.Consumers[0].ScopeId = "Room/0"; break;
            case 7: utility.CableWriteCells = null; break;
            case 8: utility.CableWriteCells = new(); break;
            case 9: utility.GasWriteCells = new(); break;
            case 10: utility.DeviceWriteCells = new(); break;
            case 11: utility.GasRestrictions.AllowedModes = new() { KsProcgenAtmosphereMode.Fill }; break;
            case 12: utility.GasRestrictions.MaximumPressureKPa = 120f; break;
            case 13: utility.GasRestrictions.MinimumObservationTicks = 60; break;
            case 14: utility.ElectricalRestrictions.RequireOperationalSupply = true; break;
        }
        Assert.That(Capture(request).DeclarationHash, Is.Not.EqualTo(hash));
    }

    [Test]
    public void EqualSpecificityConflictsFailWhileIdenticalAliasesAndMoreSpecificDeclarationsSurvive()
    {
        var request = Spec();
        var first = request.Utilities!.GasAssignments[0];
        request.Utilities.GasAssignments.Add(new() { Id = "Alias", Scope = first.Scope, ProfileId = "OtherName", Profile = Fill() });
        Assert.That(KsProcgenRequestSnapshot.TryCapture(request, out _, out var issue), Is.True, issue?.Code);
        request.Utilities.GasAssignments[1].Profile!.PressureKPa = 200f;
        Assert.That(KsProcgenRequestSnapshot.TryCapture(request, out var rejected, out issue), Is.False);
        Assert.That(rejected, Is.Null);
        Assert.That(issue!.Code, Is.EqualTo("ConflictingScopedGasAssignments"));
        request.Utilities.GasAssignments[1].Scope = KsProcgenUtilityScope.Room;
        request.Utilities.GasAssignments[1].ScopeId = "Room/1";
        Assert.That(Capture(request).Utilities!.GasAssignments.Count, Is.EqualTo(2));
    }

    [Test]
    public void RequestDefaultsAndExplicitCircuitsAreCheckedWithoutClaimingBinding()
    {
        var request = Spec();
        request.Atmosphere = Fill();
        request.Utilities!.GasAssignments[0].Scope = KsProcgenUtilityScope.Request;
        Assert.That(KsProcgenRequestSnapshot.TryCapture(request, out _, out var issue), Is.True, issue?.Code);
        request.Atmosphere.PressureKPa = 200f;
        Assert.That(KsProcgenRequestSnapshot.TryCapture(request, out _, out issue), Is.False);
        Assert.That(issue!.Code, Is.EqualTo("ConflictingRequestGasAssignments"));
        request.Atmosphere = null;
        request.Electrical = new();
        request.Utilities.Consumers[0].CircuitId = "Other";
        Assert.That(KsProcgenRequestSnapshot.TryCapture(request, out _, out issue), Is.False);
        Assert.That(issue!.Code, Is.EqualTo("UnknownConsumerCircuit"));
        request.Electrical = null;
        Assert.That(Capture(request).Utilities!.RequiresTargetBinding, Is.True);
    }

    [Test]
    public void UnresolvedNamesFailHeadlessAndLegacyHashIsUnaffectedByAbsentContracts()
    {
        var request = Spec();
        request.Utilities!.GasAssignments[0].ProfileId = "NamedOnly";
        request.Utilities.GasAssignments[0].Profile = null;
        Assert.That(KsProcgenRequestSnapshot.TryCapture(request, out _, out var issue), Is.False);
        Assert.That(issue!.Code, Is.EqualTo("UnresolvedScopedGasReference"));
        request.Utilities = null; request.SchemaVersion = 1;
        var legacy = Capture(request);
        request.SchemaVersion = 2;
        Assert.That(Capture(request).DeclarationHash, Is.Not.EqualTo(legacy.DeclarationHash));
        var absentHash = Capture(request).DeclarationHash;
        request.Utilities = new();
        var empty = Capture(request);
        Assert.That(empty.Utilities!.HasRequirements, Is.False);
        Assert.That(empty.DeclarationHash, Is.Not.EqualTo(absentHash));
    }

    private static KsProcgenRequestSnapshot Capture(KsProcgenRequest request)
    {
        Assert.That(KsProcgenRequestSnapshot.TryCapture(request, out var snapshot, out var issue), Is.True, issue?.Code);
        return snapshot!;
    }

    [TestCase(0)] [TestCase(1)] [TestCase(2)]
    public void OversizedDeclarationCollectionsFailBeforeNormalizationOutput(int collection)
    {
        var request = Spec();
        if (collection == 0)
            request.Utilities!.GasAssignments = Enumerable.Range(0, 129).Select(index => new KsProcgenGasAssignmentSpec
                { Id = $"Gas{index}", Profile = Fill() }).ToList();
        else if (collection == 1)
            request.Utilities!.Consumers = Enumerable.Range(0, 1025).Select(index => new KsProcgenElectricalConsumerSpec { Id = $"Consumer{index}" }).ToList();
        else
        {
            request.Limits.MaxCells = 8;
            request.Utilities!.CableWriteCells = Enumerable.Repeat(new Vector2i(0, 0), 9).ToList();
        }
        Assert.That(KsProcgenGeometry.TryNormalize(request, out var shape, out var issue), Is.False);
        Assert.That(shape, Is.Null);
        Assert.That(issue!.Code, Is.EqualTo(collection == 2 ? "UtilityOwnershipOutsideWritable" : "InvalidUtilityContract"));
    }

    [Test]
    public void ConstantOwnershipCannotBeReclaimedByUtilityMasks()
    {
        var request = Spec();
        request.ConstantRegions.Add(new()
        {
            Id = "Authored", SourceId = "Fixture", ContentFingerprint = "v1", Origin = new(1, 0), LocalCells = new() { new(0, 0) },
        });
        Assert.That(KsProcgenRequestSnapshot.TryCapture(request, out _, out var issue), Is.False);
        Assert.That(issue!.Code, Is.EqualTo("UtilityOwnershipOutsideWritable"));
        request.Utilities!.CableWriteCells = new() { new(0, 0) };
        Assert.That(KsProcgenRequestSnapshot.TryCapture(request, out _, out issue), Is.True, issue?.Code);
    }

    [Test]
    public void CanonicalScopeDefaultsAndDeclarationOrderDoNotChangeIntent()
    {
        var request = Spec();
        request.Utilities!.Consumers.Add(new() { Id = "Other", Scope = KsProcgenUtilityScope.Room, ScopeId = "Room/0", Selector = KsProcgenConsumerSelector.Member, SelectorId = "Desk/Computer" });
        request.Utilities.GasAssignments.Add(new() { Id = "RoomAir", Scope = KsProcgenUtilityScope.Room, ScopeId = "Room/0", Profile = Fill() });
        var hash = Capture(request).DeclarationHash;
        request.Utilities.Consumers.Reverse(); request.Utilities.GasAssignments.Reverse();
        foreach (var consumer in request.Utilities.Consumers.Where(consumer => consumer.Scope == KsProcgenUtilityScope.Area)) consumer.ScopeId = request.RequestId;
        foreach (var gas in request.Utilities.GasAssignments.Where(gas => gas.Scope == KsProcgenUtilityScope.Area)) gas.ScopeId = request.RequestId;
        Assert.That(Capture(request).DeclarationHash, Is.EqualTo(hash));
        var copy = Capture(request).CopyRequest();
        Assert.That(copy.Utilities!.Consumers.Single(consumer => consumer.Id == "Other").NominalWatts, Is.Null);
        request.Utilities.Consumers.Single(consumer => consumer.Id == "Other").NominalWatts = 0f;
        Assert.That(Capture(request).DeclarationHash, Is.Not.EqualTo(hash));
    }

    private static KsProcgenAtmosphereSpec Fill() => new()
    {
        Mode = KsProcgenAtmosphereMode.Fill, TemperatureKelvin = 293.15f, PressureKPa = 101.325f,
        MoleFractions = new() { ["Oxygen"] = 0.21f, ["Nitrogen"] = 0.79f },
    };

    private static KsProcgenRequest Spec() => new()
    {
        SchemaVersion = 2, RequestId = "Utilities",
        Shape = new() { Cells = new() { new(0, 0), new(1, 0), new(2, 0), new(3, 0) }, PreservedCells = new() { new(3, 0) }, VoidCells = new() { new(4, 0) } },
        Utilities = new()
        {
            GasAssignments = new() { new() { Id = "Air", Scope = KsProcgenUtilityScope.Area, Profile = Fill() } },
            Consumers = new() { new() { Id = "Devices", Scope = KsProcgenUtilityScope.Area, NominalWatts = 50f, PeakWatts = 100f } },
            CableWriteCells = new() { new(1, 0), new(0, 0) },
            GasRestrictions = new() { AllowedGases = new() { "Oxygen", "Nitrogen" } },
        },
    };
}
