#nullable enable
using System;
using System.Linq;
using Content.Server._KS14.Procedural;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._KS14.Procedural;
using Content.Shared.Atmos;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenOwnedAtmosphereSystem))]
public sealed class KsProcgenGasPreferenceTests : KsProcgenGameTest
{
    [TestCase(false)] [TestCase(true)]
    public async Task PreferredStabilityReportsAnOmissionWhileRequiredStabilityRemainsGuarded(bool required)
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            var request = Request(); request.Atmosphere!.StateRequirement = KsProcgenAtmosphereState.StableAtRest;
            request.Atmosphere.ObservationTicks = 5;
            if (required) request.Atmosphere.Requirement = KsProcgenAtmosphereRequirement.Required;
            var generated = Generate(request);
            try
            {
                if (required)
                {
                    Assert.That(generated.Status, Is.EqualTo(KsProcgenOwnedStageStatus.UnsupportedContent));
                    Assert.That(generated.Issue?.Code, Is.EqualTo("OwnedGasStabilityUnsupported"));
                    Assert.That(generated.Stage, Is.Null);
                    Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before)); return;
                }
                var receipt = Initialize(generated);
                Assert.That(receipt.RequiredInitialGasVerified && receipt.InitialGasVerified, Is.True);
                Assert.That(receipt.AtmosphereAtRestVerified, Is.False);
                Assert.That(receipt.Omissions.Single().Code, Is.EqualTo("PreferredGasStabilityUnsupported"));
                Assert.That(receipt.Cells.Where(cell => cell.OwnedMixture).All(cell => Math.Abs(cell.PressureKPa - 101.325f) <= 0.1f), Is.True);
                Assert.That(stages.TryInitialize(generated.Stage!).Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview));
                Assert.That(generated.Stage!.InitialAtmosphere, Is.SameAs(receipt));
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task HardObservationBoundCannotBeDowngradedByAPreferredProfile()
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet(); var request = Request();
            request.Atmosphere!.StateRequirement = KsProcgenAtmosphereState.StableAtRest; request.Atmosphere.ObservationTicks = 5;
            request.Utilities = new() { GasRestrictions = new() { MinimumObservationTicks = 5 } };
            var generated = Generate(request);
            Assert.That(generated.Status, Is.EqualTo(KsProcgenOwnedStageStatus.UnsupportedContent));
            Assert.That(generated.Issue?.Code, Is.EqualTo("OwnedGasStabilityUnsupported"));
            Assert.That(generated.Stage, Is.Null); Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task OptionalFillInAnOpenFootprintIsOmittedWhileRequiredFillRejects(bool required)
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            var request = Request(); request.Shape.Cells = new() { new(0, 0), new(1, 0), new(2, 0) };
            request.Shape.EnvelopeCells.Clear();
            if (required) request.Atmosphere!.Requirement = KsProcgenAtmosphereRequirement.Required;
            var generated = Generate(request);
            try
            {
                Assert.That(generated.Stage, Is.Not.Null, message: generated.Issue?.Code);
                var initialized = stages.TryInitialize(generated.Stage!);
                if (required)
                {
                    Assert.That(initialized.Status, Is.EqualTo(KsProcgenOwnedStageStatus.Rejected));
                    Assert.That(initialized.Issue?.Code, Is.EqualTo("OwnedGasDomainLeaks"));
                    Assert.That(generated.Stage!.Active, Is.False);
                    Assert.That(generated.Stage.InitialAtmosphere, Is.Null);
                    Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before)); return;
                }
                Assert.That(initialized.Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview), message: initialized.Issue?.Code);
                var receipt = generated.Stage!.InitialAtmosphere!;
                Assert.That(receipt.RequiredInitialGasVerified, Is.True); Assert.That(receipt.InitialGasVerified, Is.False);
                Assert.That(receipt.Omissions.Select(omission => omission.Code), Does.Contain("PreferredGasDomainLeaks"));
                Assert.That(receipt.Omissions.Select(omission => omission.Code), Does.Contain("PreferredGasInitialStateUnmet"));
                Assert.That(receipt.Cells.Where(cell => cell.OwnedMixture).All(cell => cell.PressureKPa == 0f), Is.True);
                var first = Mixture(generated.Stage, new(0, 0));
                Assert.That(stages.Verify(generated.Stage), Is.True);
                Assert.That(stages.TryInitialize(generated.Stage).Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview));
                Assert.That(Mixture(generated.Stage, new(0, 0)), Is.SameAs(first)); Assert.That(first.Pressure, Is.Zero);
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task UnrepresentableOptionalMixtureIsNotWrittenOrClaimedAsVerified(bool required)
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            var request = Request(); request.Atmosphere!.PressureKPa = float.MaxValue; request.Atmosphere.TemperatureKelvin = Atmospherics.TCMB;
            if (required) request.Atmosphere.Requirement = KsProcgenAtmosphereRequirement.Required;
            var generated = Generate(request);
            try
            {
                Assert.That(generated.Stage, Is.Not.Null, message: generated.Issue?.Code);
                var initialized = stages.TryInitialize(generated.Stage!);
                if (required)
                {
                    Assert.That(initialized.Status, Is.EqualTo(KsProcgenOwnedStageStatus.Rejected));
                    Assert.That(initialized.Issue?.Code, Is.EqualTo("AtmosphereMixtureOverflow"));
                    Assert.That(generated.Stage!.Active, Is.False);
                    Assert.That(generated.Stage.InitialAtmosphere, Is.Null);
                    Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before)); return;
                }
                Assert.That(initialized.Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview), message: initialized.Issue?.Code);
                var receipt = generated.Stage!.InitialAtmosphere!;
                Assert.That(receipt.RequiredInitialGasVerified, Is.True); Assert.That(receipt.InitialGasVerified, Is.False);
                Assert.That(receipt.Omissions.Select(omission => omission.Code), Does.Contain("PreferredGasMixtureUnrepresentable"));
                Assert.That(receipt.Omissions.Select(omission => omission.Code), Does.Contain("PreferredGasInitialStateUnmet"));
                Assert.That(receipt.Cells.Where(cell => cell.OwnedMixture).All(cell => cell.PressureKPa == 0f), Is.True);
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task SkippingAnOptionalFillCannotBypassAHardMinimumPressure()
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            var request = Request(); request.Atmosphere!.PressureKPa = float.MaxValue; request.Atmosphere.TemperatureKelvin = Atmospherics.TCMB;
            request.Utilities = new() { GasRestrictions = new() { MinimumPressureKPa = 80f } };
            var generated = Generate(request);
            try
            {
                Assert.That(generated.Stage, Is.Not.Null, message: generated.Issue?.Code);
                var initialized = stages.TryInitialize(generated.Stage!);
                Assert.That(initialized.Status, Is.EqualTo(KsProcgenOwnedStageStatus.Rejected));
                Assert.That(initialized.Issue?.Code, Is.EqualTo("OwnedGasRestrictionDrift"));
                Assert.That(generated.Stage!.InitialAtmosphere, Is.Null);
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task LaterPreferredDriftDoesNotRefillOrRewriteHistoricalEvidenceButHardBoundsStillReject()
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            var request = Request(); request.Utilities = new() { GasRestrictions = new() { MinimumPressureKPa = 80f } };
            var generated = Generate(request);
            try
            {
                var receipt = Initialize(generated); var stage = generated.Stage!;
                var observed = receipt.Cells.First(cell => cell.OwnedMixture); var mixture = Mixture(stage, observed.Cell);
                var originalOxygen = mixture.GetMoles(Gas.Oxygen); var originalNitrogen = mixture.GetMoles(Gas.Nitrogen);
                mixture.SetMoles(Gas.Oxygen, originalOxygen * 0.9f); mixture.SetMoles(Gas.Nitrogen, originalNitrogen * 0.9f);
                var changedPressure = mixture.Pressure;
                Assert.That(stages.Verify(stage), Is.True);
                Assert.That(stages.TryInitialize(stage).Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview));
                Assert.That(Mixture(stage, observed.Cell), Is.SameAs(mixture)); Assert.That(mixture.Pressure, Is.EqualTo(changedPressure));
                Assert.That(stage.InitialAtmosphere, Is.SameAs(receipt)); Assert.That(receipt.InitialGasVerified, Is.True);
                Assert.That(receipt.Omissions, Is.Empty);
                Assert.That(receipt.Cells.Single(cell => cell.Cell == observed.Cell).PressureKPa, Is.EqualTo(observed.PressureKPa));
                mixture.SetMoles(Gas.Oxygen, originalOxygen * 0.5f); mixture.SetMoles(Gas.Nitrogen, originalNitrogen * 0.5f);
                Assert.That(stages.Verify(stage), Is.False);
                var rejected = stages.TryInitialize(stage);
                Assert.That(rejected.Status, Is.EqualTo(KsProcgenOwnedStageStatus.Rejected));
                Assert.That(rejected.Issue?.Code, Is.EqualTo("OwnedGasRestrictionDrift")); Assert.That(stage.Active, Is.False);
                Assert.That(mixture.GetMoles(Gas.Oxygen), Is.EqualTo(originalOxygen * 0.5f));
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    private KsProcgenInitialAtmosphereReceipt Initialize(KsProcgenOwnedStageResult generated)
    {
        Assert.That(generated.Stage, Is.Not.Null, message: generated.Issue?.Code);
        var initialized = Pair.Server.System<KsProcgenOwnedStageSystem>().TryInitialize(generated.Stage!);
        Assert.That(initialized.Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview), message: initialized.Issue?.Code);
        return generated.Stage!.InitialAtmosphere!;
    }
    private GasMixture Mixture(KsProcgenOwnedStage stage, Vector2i cell) => Pair.Server.System<AtmosphereSystem>().GetTileMixture(stage.GridUid, stage.MapUid, cell)!;
    private KsProcgenOwnedStageResult Generate(KsProcgenRequest request) => Pair.Server.System<KsProcgenOwnedStageSystem>().TryGenerate(request,
        "KsProcgenEmptyOfficeThemeFixture", planningBudgets: new() { MinimumRoomCells = 1, PreferredMaximumRoomCells = 64 });
    private static KsProcgenRequest Request() => new()
    {
        SchemaVersion = 2, RequestId = "GasPreferences", Seed = 17, ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland,
        Shape = new() { Cells = Enumerable.Range(0, 5).SelectMany(x => Enumerable.Range(0, 5).Select(y => new Vector2i(x, y))).ToList(),
            EnvelopeCells = Enumerable.Range(-1, 7).SelectMany(x => Enumerable.Range(-1, 7).Select(y => new Vector2i(x, y)))
                .Where(cell => cell.X is -1 or 5 || cell.Y is -1 or 5).ToList() },
        Atmosphere = new() { Mode = KsProcgenAtmosphereMode.Fill, Requirement = KsProcgenAtmosphereRequirement.Preferred,
            PressureKPa = 101.325f, TemperatureKelvin = 293.15f, MoleFractions = new() { ["Oxygen"] = 0.21f, ["Nitrogen"] = 0.79f } },
    };
}
