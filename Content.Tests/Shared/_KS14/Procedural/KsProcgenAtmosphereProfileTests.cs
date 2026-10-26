#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using Content.Shared.Atmos;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenAtmosphereProfileTests
{
    [Test]
    public void SnapshotCanonicalizesGasNamesAndDetachesAuthoringData()
    {
        var spec = Air();
        var profile = Snapshot(spec);
        var hash = profile.SemanticHash;
        Assert.That(profile.MoleFractions.Select(fraction => fraction.GasId), Is.EqualTo(new[] { "Nitrogen", "Oxygen" }));
        spec.MoleFractions["Oxygen"] = 1f;
        spec.MoleFractions.Clear();
        spec.PressureKPa = 500f;
        Assert.That(profile.SemanticHash, Is.EqualTo(hash));
        Assert.That(profile.PressureKPa, Is.EqualTo(101.325f));
        var copy = profile.CopySpec();
        Assert.That(Snapshot(copy).SemanticHash, Is.EqualTo(hash));
        copy.MoleFractions.Clear();
        Assert.That(profile.MoleFractions.Count, Is.EqualTo(2));
        Assert.Throws<NotSupportedException>(() => ((IList<KsProcgenGasFraction>) profile.MoleFractions).Clear());
        var reordered = Air();
        reordered.MoleFractions = new() { ["Nitrogen"] = 0.79f, ["Oxygen"] = 0.21f, ["Plasma"] = 0f };
        Assert.That(Snapshot(reordered).SemanticHash, Is.EqualTo(hash));
    }

    [TestCase("oxygen")]
    [TestCase("0")]
    [TestCase("Oxygen, Nitrogen")]
    [TestCase("NotAGas")]
    [TestCase("")]
    public void InvalidGasIdentifiersNeverBecomeMoleArrayIndices(string gasId)
    {
        var spec = Air();
        spec.MoleFractions = new() { [gasId] = 1f };
        Assert.That(KsProcgenAtmosphereProfiles.TrySnapshot(spec, out var profile, out var issue), Is.False);
        Assert.That(issue?.Code, Is.EqualTo("InvalidAtmosphereGas"));
        Assert.That(profile, Is.Null);
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
    [TestCase(13)]
    [TestCase(14)]
    [TestCase(15)]
    [TestCase(16)]
    [TestCase(17)]
    public void MalformedProfilesHaveNoSnapshotOrNormalizedGeometry(int defect)
    {
        var spec = Air();
        switch (defect)
        {
            case 0: spec.SchemaVersion = 2; break;
            case 1: spec.Mode = (KsProcgenAtmosphereMode) 255; break;
            case 2: spec.MoleFractions = null!; break;
            case 3: spec.PressureKPa = float.NaN; break;
            case 4: spec.PressureKPa = float.PositiveInfinity; break;
            case 5: spec.PressureKPa = 0f; break;
            case 6: spec.TemperatureKelvin = null; break;
            case 7: spec.TemperatureKelvin = Atmospherics.TCMB - 1f; break;
            case 8: spec.TemperatureKelvin = Atmospherics.Tmax + 1f; break;
            case 9: spec.MoleFractions["Oxygen"] = -0.1f; break;
            case 10: spec.MoleFractions["Oxygen"] = float.NaN; break;
            case 11: spec.MoleFractions["Oxygen"] = 0.3f; break;
            case 12: spec.PressureToleranceKPa = -1f; break;
            case 13: spec.FractionTolerance = 1.1f; break;
            case 14: spec.StateRequirement = KsProcgenAtmosphereState.StableAtRest; break;
            case 15: spec.ObservationTicks = 1; break;
            case 16: spec.ObservationTicks = KsProcgenAtmosphereProfiles.MaximumObservationTicks + 1; break;
            case 17: spec.Requirement = (KsProcgenAtmosphereRequirement) 255; break;
        }
        Assert.That(KsProcgenAtmosphereProfiles.TrySnapshot(spec, out var profile, out var issue), Is.False);
        Assert.That(profile, Is.Null);
        Assert.That(issue, Is.Not.Null);
        var request = new KsProcgenRequest { RequestId = "Gas", Shape = new() { Cells = [new(0, 0)] }, Atmosphere = spec };
        Assert.That(KsProcgenGeometry.TryNormalize(request, out var shape, out _), Is.False);
        Assert.That(shape, Is.Null);
    }

    [Test]
    public void RoundingToleranceIsNarrowAndEveryNamedGasIsValidatedEvenAtZero()
    {
        var spec = Air();
        spec.MoleFractions["Nitrogen"] = 0.7900005f;
        var profile = Snapshot(spec);
        Assert.That(profile.MoleFractions.Sum(fraction => fraction.Fraction), Is.EqualTo(1f).Within(0.000001f));
        spec.MoleFractions["Nitrogen"] = 0.79001f;
        Assert.That(KsProcgenAtmosphereProfiles.TrySnapshot(spec, out _, out var issue), Is.False);
        Assert.That(issue?.Code, Is.EqualTo("AtmosphereFractionsNotNormalized"));
        spec = Air();
        spec.MoleFractions["Unknown"] = 0f;
        Assert.That(KsProcgenAtmosphereProfiles.TrySnapshot(spec, out _, out issue), Is.False);
        Assert.That(issue?.Code, Is.EqualTo("InvalidAtmosphereGas"));
        spec = Air();
        Assert.That(KsProcgenAtmosphereProfiles.TrySnapshot(spec, out _, out issue,
            gasExists: gasId => gasId != "Oxygen"), Is.False);
        Assert.That(issue?.Code, Is.EqualTo("InvalidAtmosphereGas"));
    }

    [Test]
    public void EveryPolicyAndMeasuredTargetParticipatesInIdentity()
    {
        var original = Snapshot(Air()).SemanticHash;
        var changes = new Action<KsProcgenAtmosphereSpec>[]
        {
            spec => spec.PressureKPa = 110f,
            spec => spec.TemperatureKelvin = 300f,
            spec => spec.Requirement = KsProcgenAtmosphereRequirement.Preferred,
            spec => { spec.StateRequirement = KsProcgenAtmosphereState.StableAtRest; spec.ObservationTicks = 10; },
            spec => spec.PressureToleranceKPa = 0.2f,
            spec => spec.TemperatureToleranceKelvin = 0.2f,
            spec => spec.FractionTolerance = 0.01f,
            spec => { spec.MoleFractions["Oxygen"] = 0.2f; spec.MoleFractions["Nitrogen"] = 0.8f; },
        };
        foreach (var change in changes)
        {
            var spec = Air();
            change(spec);
            Assert.That(Snapshot(spec).SemanticHash, Is.Not.EqualTo(original));
        }
        Assert.That(KsProcgenAtmosphereProfiles.TrySnapshot(Air(), out var named, out _, sourceProfileId: "Air"), Is.True);
        Assert.That(named!.SemanticHash, Is.Not.EqualTo(original));
    }

    [Test]
    public void CanonicalMixturesKeepIdentityAcrossRepeatedEditableRoundtrips()
    {
        var random = new KsProcgenRandom(42);
        for (var sample = 0; sample < 512; sample++)
        {
            var counts = Enumerable.Range(0, 3).Select(_ => random.NextInt(10000) + 1).ToArray();
            var total = (float) counts.Sum();
            var spec = Air();
            spec.MoleFractions = new()
            {
                ["Oxygen"] = (float) counts[0] / total,
                ["Nitrogen"] = (float) counts[1] / total,
                ["CarbonDioxide"] = (float) counts[2] / total,
            };
            var initial = Snapshot(spec);
            var current = initial;
            for (var roundtrip = 0; roundtrip < 4; roundtrip++)
                current = Snapshot(current.CopySpec());
            Assert.That(current.SemanticHash, Is.EqualTo(initial.SemanticHash), $"Mixture {sample} drifted while reopening its draft.");
        }
    }

    [Test]
    public void PerTileMixturesUseActualVolumeAndEnginePressureArithmetic()
    {
        var profile = Snapshot(Air());
        Assert.That(KsProcgenAtmosphereMixturePlanner.TryPlan(profile, 2500f, out var standard, out var issue), Is.True, issue?.Code);
        Assert.That(KsProcgenAtmosphereMixturePlanner.TryPlan(profile, 5000f, out var doubleVolume, out issue), Is.True, issue?.Code);
        Assert.That(standard!.CalculatedPressureKPa, Is.EqualTo(101.325f).Within(0.001f));
        Assert.That(doubleVolume!.CalculatedPressureKPa, Is.EqualTo(standard.CalculatedPressureKPa).Within(0.001f));
        foreach (var gas in standard.Gases)
            Assert.That(doubleVolume.Gases.Single(amount => amount.GasId == gas.GasId).Moles, Is.EqualTo(gas.Moles * 2f));
        var mixture = new GasMixture(volume: 2500f) { Temperature = standard.TemperatureKelvin };
        foreach (var gas in standard.Gases)
            mixture.SetMoles(Enum.Parse<Gas>(gas.GasId), gas.Moles);
        Assert.That(mixture.Pressure, Is.EqualTo(standard.CalculatedPressureKPa));
        Assert.That(mixture.TotalMoles, Is.EqualTo(Atmospherics.MolesCellStandard).Within(0.001f));
        Assert.That(standard.InitialGasVerified || standard.AtmosphereAtRestVerified, Is.False);
    }

    [Test]
    public void PreserveAndVacuumAreDifferentAndCannotCarryHiddenReplacementGas()
    {
        var preserve = Snapshot(new());
        Assert.That(KsProcgenAtmosphereMixturePlanner.TryPlan(preserve, 2500f, out var mixture, out var issue), Is.False);
        Assert.That(issue?.Code, Is.EqualTo("PreservedAtmosphereNotWritable"));
        Assert.That(mixture, Is.Null);
        var vacuum = Snapshot(new() { Mode = KsProcgenAtmosphereMode.Vacuum, PressureKPa = 0f, TemperatureKelvin = 293.15f });
        Assert.That(KsProcgenAtmosphereMixturePlanner.TryPlan(vacuum, 2500f, out mixture, out issue), Is.True, issue?.Code);
        Assert.That(mixture!.Gases, Is.Empty);
        Assert.That(mixture.CalculatedPressureKPa, Is.Zero);
        Assert.That(vacuum.SemanticHash, Is.Not.EqualTo(preserve.SemanticHash));
        Assert.That(KsProcgenAtmosphereProfiles.TrySnapshot(new() { PressureKPa = 0f }, out _, out _), Is.False);
        Assert.That(KsProcgenAtmosphereProfiles.TrySnapshot(new()
        {
            Mode = KsProcgenAtmosphereMode.Vacuum, PressureKPa = 0f, TemperatureKelvin = 293.15f,
            MoleFractions = new() { ["Oxygen"] = 0f },
        }, out _, out _), Is.False);
    }

    [TestCase(0f)]
    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void InvalidVolumesCannotReturnPartialMixtures(float volume)
    {
        Assert.That(KsProcgenAtmosphereMixturePlanner.TryPlan(Snapshot(Air()), volume, out var mixture, out var issue), Is.False);
        Assert.That(issue?.Code, Is.EqualTo("InvalidAtmosphereVolume"));
        Assert.That(mixture, Is.Null);
    }

    [Test]
    public void EngineOverflowAndUnderflowCannotPassAsValidPressure()
    {
        var huge = Air();
        huge.PressureKPa = float.MaxValue;
        Assert.That(KsProcgenAtmosphereMixturePlanner.TryPlan(Snapshot(huge), float.MaxValue, out var mixture, out _), Is.False);
        Assert.That(mixture, Is.Null);
        var tiny = Air();
        tiny.PressureKPa = float.Epsilon;
        Assert.That(KsProcgenAtmosphereMixturePlanner.TryPlan(Snapshot(tiny), float.Epsilon, out mixture, out _), Is.False);
        Assert.That(mixture, Is.Null);
        Assert.That(KsProcgenAtmosphereMixturePlanner.TryPlan(Snapshot(Air()), float.MaxValue, out mixture, out _), Is.False);
        Assert.That(mixture, Is.Null);
    }

    [Test]
    public void BlobRequestsRetainFrozenAtmosphereAndSnapshotIdentityIncludesGasData()
    {
        var spec = Air();
        var profile = Snapshot(spec);
        var result = Area(profile);
        var blob = result.Blobs.Single();
        spec.MoleFractions.Clear();
        var request = blob.CreateRequest(42);
        Assert.That(request.Atmosphere, Is.Not.Null);
        Assert.That(request.Atmosphere!.MoleFractions.Count, Is.EqualTo(2));
        request.Atmosphere.MoleFractions.Clear();
        Assert.That(blob.CreateRequest(42).Atmosphere!.MoleFractions.Count, Is.EqualTo(2));
        Assert.That(Area(null).SnapshotHash, Is.Not.EqualTo(result.SnapshotHash));
        var nitrogen = Air();
        nitrogen.MoleFractions = new() { ["Nitrogen"] = 1f };
        Assert.That(Area(Snapshot(nitrogen)).SnapshotHash, Is.Not.EqualTo(result.SnapshotHash));
        Assert.That(Area(null).Blobs.Single().Id, Is.EqualTo(blob.Id));
    }

    private static KsProcgenAreaNormalization Area(KsProcgenAtmosphereProfile? profile) => KsProcgenAreaNormalizer.Normalize(
        "Host", [new(new Vector2i(0, 0), "Main", "Room")],
        [new("Room", KsProcgenMode.Procedural, KsProcgenGeometryMode.Footprint,
            KsProcgenConnectivityPolicy.PerIsland, null, 64, 100) { Atmosphere = profile }]);

    private static KsProcgenAtmosphereSpec Air() => new()
    {
        Mode = KsProcgenAtmosphereMode.Fill, TemperatureKelvin = 293.15f, PressureKPa = 101.325f,
        MoleFractions = new() { ["Oxygen"] = 0.21f, ["Nitrogen"] = 0.79f },
    };

    private static KsProcgenAtmosphereProfile Snapshot(KsProcgenAtmosphereSpec spec)
    {
        Assert.That(KsProcgenAtmosphereProfiles.TrySnapshot(spec, out var profile, out var issue), Is.True, issue?.Code);
        return profile!;
    }
}
