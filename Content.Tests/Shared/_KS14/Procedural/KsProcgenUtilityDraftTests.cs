#nullable enable
using System;
using System.Collections.Generic;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenUtilityDraftTests
{
    [Test]
    public void RevisionsRetainIndependentImmutableAssetsAndEditableCopies()
    {
        var spec = Spec();
        var original = Snapshot(spec);
        var hash = original.ContentHash;
        spec.Atmosphere["Air"].PressureKPa = 200f;
        spec.Electrical["Power"].Interfaces[0].AvailableWatts = 900f;
        Assert.That(original.TryAtmosphere("Air", out var air), Is.True);
        Assert.That(air!.PressureKPa, Is.EqualTo(101.325f));
        Assert.That(original.TryElectrical("Power", out var power), Is.True);
        Assert.That(power!.Interfaces[0].AvailableWatts, Is.EqualTo(500f));
        var edit = original.CopySpec();
        edit.Atmosphere["Air"].PressureKPa = 150f;
        Assert.That(original.TryRevise(edit, _ => true, _ => true, out var revised, out var issue), Is.True, issue?.Code);
        Assert.That(revised!.Revision, Is.EqualTo(4));
        Assert.That(edit.Revision, Is.EqualTo(3));
        Assert.That(revised.ContentHash, Is.Not.EqualTo(hash));
        edit.Atmosphere.Clear();
        edit.Electrical["Power"].Interfaces.Clear();
        Assert.That(revised.Atmosphere.Count, Is.EqualTo(1));
        Assert.That(revised.Electrical[0].Profile.Interfaces.Count, Is.EqualTo(1));
        Assert.That(original.ContentHash, Is.EqualTo(hash));
        Assert.Throws<NotSupportedException>(() => ((IList<KsProcgenDraftAtmosphere>) original.Atmosphere).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<KsProcgenDraftElectrical>) original.Electrical).Clear());
    }

    [Test]
    public void ContentIdentityIgnoresDocumentRevisionAndDictionaryInsertionOrder()
    {
        var spec = Spec();
        spec.Atmosphere.Add("Preserve", new());
        spec.Electrical.Add("Disabled", new());
        var original = Snapshot(spec);
        spec.DocumentId = "AnotherDocument";
        spec.Revision = 50;
        spec.Atmosphere = new() { ["Preserve"] = spec.Atmosphere["Preserve"], ["Air"] = spec.Atmosphere["Air"] };
        spec.Electrical = new() { ["Disabled"] = spec.Electrical["Disabled"], ["Power"] = spec.Electrical["Power"] };
        Assert.That(Snapshot(spec).ContentHash, Is.EqualTo(original.ContentHash));
        var roundtrip = Snapshot(original.CopySpec());
        Assert.That(roundtrip.ContentHash, Is.EqualTo(original.ContentHash));
        var renamed = original.CopySpec();
        renamed.Atmosphere["Renamed"] = renamed.Atmosphere["Air"];
        renamed.Atmosphere.Remove("Air");
        Assert.That(Snapshot(renamed).ContentHash, Is.Not.EqualTo(original.ContentHash));
    }

    [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)]
    [TestCase(5)] [TestCase(6)] [TestCase(7)] [TestCase(8)] [TestCase(9)]
    public void InvalidImportNeverReturnsPartialCatalog(int defect)
    {
        var spec = Spec();
        switch (defect)
        {
            case 0: spec.SchemaVersion = 2; break;
            case 1: spec.DocumentId = ""; break;
            case 2: spec.Revision = -1; break;
            case 3: spec.Atmosphere = null!; break;
            case 4: spec.Electrical = null!; break;
            case 5: spec.Atmosphere["Air"].SchemaVersion = 2; break;
            case 6: spec.Electrical["Power"].DemandMargin = float.NaN; break;
            case 7: spec.Atmosphere[""] = new(); break;
            case 8: spec.Electrical["Null"] = null!; break;
            case 9:
                for (var index = 0; index < KsProcgenUtilityDraft.MaximumAssets; index++)
                    spec.Atmosphere.Add($"Asset{index}", new());
                break;
        }
        Assert.That(KsProcgenUtilityDraft.TrySnapshot(spec, _ => true, _ => true, out var draft, out var issue), Is.False);
        Assert.That(draft, Is.Null);
        Assert.That(issue, Is.Not.Null);
    }

    [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
    public void ChangingEitherUtilityKindChangesContentIdentity(int change)
    {
        var spec = Spec();
        var original = Snapshot(spec);
        switch (change)
        {
            case 0: spec.Atmosphere["Air"].PressureKPa = 150f; break;
            case 1: spec.Electrical["Power"].Interfaces[0].AvailableWatts = 600f; break;
            case 2: spec.Electrical["Power"].Mode = KsProcgenElectricalMode.ValidateExisting; break;
            case 3:
                spec.Electrical["Renamed"] = spec.Electrical["Power"];
                spec.Electrical.Remove("Power");
                break;
        }
        Assert.That(Snapshot(spec).ContentHash, Is.Not.EqualTo(original.ContentHash));
    }

    [Test]
    public void AssetCapAppliesAcrossBothKindsAndRequiresReceivingContentDependencies()
    {
        var spec = Spec();
        Assert.That(KsProcgenUtilityDraft.TrySnapshot(spec, _ => false, _ => true, out var rejected, out var issue), Is.False);
        Assert.That(rejected, Is.Null);
        Assert.That(issue?.Code, Is.EqualTo("InvalidAtmosphereGas"));
        Assert.That(KsProcgenUtilityDraft.TrySnapshot(spec, _ => true, _ => false, out rejected, out issue), Is.False);
        Assert.That(rejected, Is.Null);
        Assert.That(issue?.Code, Is.EqualTo("InvalidElectricalPalette"));
        for (var index = 2; index < KsProcgenUtilityDraft.MaximumAssets; index++)
            spec.Atmosphere.Add($"Asset{index}", new());
        Assert.That(Snapshot(spec).Atmosphere.Count + Snapshot(spec).Electrical.Count, Is.EqualTo(KsProcgenUtilityDraft.MaximumAssets));
        spec.Electrical.Add("Extra", new());
        Assert.That(KsProcgenUtilityDraft.TrySnapshot(spec, _ => true, _ => true, out rejected, out issue), Is.False);
        Assert.That(issue?.Code, Is.EqualTo("InvalidUtilityDraft"));
    }

    [Test]
    public void StaleForeignInvalidAndExhaustedRevisionsCannotReplaceCurrentDocument()
    {
        var original = Snapshot(Spec());
        var edit = original.CopySpec();
        Assert.That(original.TryRevise(edit, _ => true, _ => true, out var revised, out _), Is.True);
        Assert.That(revised!.TryRevise(edit, _ => true, _ => true, out var rejected, out var issue), Is.False);
        Assert.That(rejected, Is.Null);
        Assert.That(issue?.Code, Is.EqualTo("UtilityDraftRevisionConflict"));
        edit.DocumentId = "Other";
        Assert.That(original.TryRevise(edit, _ => true, _ => true, out rejected, out issue), Is.False);
        Assert.That(issue?.Code, Is.EqualTo("UtilityDraftRevisionConflict"));
        edit = original.CopySpec();
        edit.Electrical["Power"].Interfaces[0].AvailableWatts = -1f;
        Assert.That(original.TryRevise(edit, _ => true, _ => true, out rejected, out issue), Is.False);
        Assert.That(issue?.Code, Is.EqualTo("InvalidElectricalInterface"));
        Assert.That(original.Revision, Is.EqualTo(3));
        edit = original.CopySpec();
        edit.Revision = int.MaxValue;
        var exhausted = Snapshot(edit);
        Assert.That(exhausted.TryRevise(edit, _ => true, _ => true, out rejected, out issue), Is.False);
        Assert.That(issue?.Code, Is.EqualTo("UtilityDraftRevisionConflict"));
    }

    private static KsProcgenUtilityDraft Snapshot(KsProcgenUtilityDraftSpec spec)
    {
        Assert.That(KsProcgenUtilityDraft.TrySnapshot(spec, _ => true, _ => true, out var draft, out var issue), Is.True, issue?.Code);
        return draft!;
    }

    private static KsProcgenUtilityDraftSpec Spec() => new()
    {
        DocumentId = "InteriorDraft", Revision = 3,
        Atmosphere = new()
        {
            ["Air"] = new()
            {
                Mode = KsProcgenAtmosphereMode.Fill, PressureKPa = 101.325f, TemperatureKelvin = 293.15f,
                MoleFractions = new() { ["Oxygen"] = 0.21f, ["Nitrogen"] = 0.79f },
            },
        },
        Electrical = new()
        {
            ["Power"] = new()
            {
                Mode = KsProcgenElectricalMode.AutoDistribute,
                Interfaces = new() { new() { Id = "Input", Cell = new(0, 0), NetworkKey = "StationHV", AvailableWatts = 500f } },
            },
        },
    };
}
