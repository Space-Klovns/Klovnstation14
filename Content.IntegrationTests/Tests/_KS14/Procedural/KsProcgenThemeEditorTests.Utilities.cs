#nullable enable
using System;
using System.Linq;
using Content.Server._KS14.Procedural;
using Content.Server._KS14.Procedural.Editor;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

public sealed partial class KsProcgenThemeEditorTests
{
    [TestCase("KsProcgenStandardAir", false)]
    [TestCase("KsProcgenNitrogenRoom", true)]
    [TestCase("KsProcgenVacuum", false)]
    public async Task SavedUtilityChoicesInitializeRealGasAndWiring(string atmosphereProfile, bool wireRoom)
    {
        await AssertOnServer(() =>
        {
            var library = Server.System<KsProcgenThemeLibrarySystem>();
            var stages = Server.System<KsProcgenOwnedStageSystem>();
            var before = SEntMan.GetEntities().ToHashSet();
            string? savedKey = null;
            try
            {
                var values = Values();
                values.WallPack = "KsProcgenOfficeWalls";
                values.AtmosphereProfile = atmosphereProfile;
                values.ElectricalProfile = wireRoom ? "KsProcgenClosetElectrical" : string.Empty;
                Assert.That(library.TrySave(Guid.NewGuid(), 1, values, out savedKey, out var issue), Is.True, issue?.Code);
                // Source mutation must not affect either the saved choice or the captured catalog.
                values.AtmosphereProfile = "MissingGas";
                values.ElectricalProfile = "MissingPower";
                Assert.That(library.TryCaptureCatalog(savedKey!, out var catalog, out issue), Is.True, issue?.Code);
                Assert.That(catalog!.Theme.Atmosphere!.SourceProfileId, Is.EqualTo(atmosphereProfile));
                Assert.That(catalog.Theme.Electrical?.SourceProfileId, Is.EqualTo(wireRoom ? "KsProcgenClosetElectrical" : null));
                var generated = stages.TryGenerate(new()
                {
                    SchemaVersion = 2, RequestId = "SavedUtilityTheme", Seed = 17,
                    ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland,
                    WindowGoal = new() { ExteriorWindowFraction = 0f },
                    Shape = new()
                    {
                        Cells = Enumerable.Range(0, 6).SelectMany(x => Enumerable.Range(0, 6).Select(y => new Vector2i(x, y))).ToList(),
                        EnvelopeCells = Enumerable.Range(-1, 8).SelectMany(x => Enumerable.Range(-1, 8).Select(y => new Vector2i(x, y)))
                            .Where(cell => cell.X is -1 or 6 || cell.Y is -1 or 6).ToList(),
                    },
                }, "Theme", content: catalog,
                    planningBudgets: new() { MinimumRoomCells = 1, PreferredMaximumRoomCells = 64, MaximumFurnishingProbes = 32768 });
                Assert.That(generated.Stage, Is.Not.Null, generated.Issue?.Code + "/" + generated.Proposal?.Issue?.Code);
                var initialized = stages.TryInitialize(generated.Stage!);
                Assert.That(initialized.Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview), initialized.Issue?.Code);
                var gas = generated.Stage!.InitialAtmosphere!;
                Assert.That(gas.InitialGasVerified, Is.True);
                Assert.That(gas.AtmosphereAtRestVerified, Is.False);
                var filled = gas.Cells.Where(cell => cell.OwnedMixture).ToArray();
                Assert.That(filled, Is.Not.Empty);
                Assert.That(filled.All(cell => Math.Abs(cell.PressureKPa - (atmosphereProfile == "KsProcgenVacuum" ? 0f : 101.325f)) <= 0.1f), Is.True);
                foreach (var cell in filled)
                {
                    Assert.That(cell.TemperatureKelvin, Is.EqualTo(293.15f).Within(0.1f));
                    var totalMoles = cell.Gases.Sum(gasAmount => gasAmount.Moles);
                    if (atmosphereProfile == "KsProcgenVacuum") Assert.That(totalMoles, Is.Zero);
                    else
                    {
                        Assert.That(totalMoles, Is.GreaterThan(0f));
                        var oxygen = cell.Gases.Where(gasAmount => gasAmount.GasId == "Oxygen").Sum(gasAmount => gasAmount.Moles);
                        var nitrogen = cell.Gases.Where(gasAmount => gasAmount.GasId == "Nitrogen").Sum(gasAmount => gasAmount.Moles);
                        Assert.That(oxygen / totalMoles, Is.EqualTo(atmosphereProfile == "KsProcgenStandardAir" ? 0.21f : 0f).Within(0.001f));
                        Assert.That(nitrogen / totalMoles, Is.EqualTo(atmosphereProfile == "KsProcgenStandardAir" ? 0.79f : 1f).Within(0.001f));
                    }
                }
                if (wireRoom)
                {
                    var wiring = generated.Stage.InitialElectrical!;
                    Assert.That(wiring.ElectricalTopologyVerified, Is.True);
                    Assert.That(wiring.ElectricalSupplyVerified, Is.False);
                    Assert.That(wiring.CableCount, Is.GreaterThan(0));
                    Assert.That(wiring.DeviceCount, Is.GreaterThanOrEqualTo(2));
                    Assert.That(generated.Proposal!.Electrical!.Service!.UncoveredFloorCells, Is.Empty);
                }
                else Assert.That(generated.Stage.InitialElectrical, Is.Null);
                Assert.That(stages.Verify(generated.Stage), Is.True);
            }
            finally { stages.DiscardAll(); Delete(savedKey); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(true)] [TestCase(false)]
    public async Task UnknownUtilityProfilesCannotBeSaved(bool atmosphere)
    {
        await AssertOnServer(() =>
        {
            var values = Values();
            if (atmosphere) values.AtmosphereProfile = "MissingUtilityProfile";
            else values.ElectricalProfile = "MissingUtilityProfile";
            Assert.That(Server.System<KsProcgenThemeLibrarySystem>().TrySave(Guid.NewGuid(), 1, values,
                out var key, out var issue), Is.False);
            Assert.That(key, Is.Null);
            Assert.That(issue, Is.Not.Null);
        });
    }
}
