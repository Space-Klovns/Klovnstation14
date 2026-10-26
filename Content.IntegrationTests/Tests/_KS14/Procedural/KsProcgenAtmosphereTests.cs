using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Content.Server._KS14.Procedural;
using Content.Shared._KS14.Procedural;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenAtmosphereProfiles))]
public sealed class KsProcgenAtmosphereTests : KsProcgenGameTest
{
    [Test]
    public async Task LoadedProfilesAndInlineRequestsRoundtripThroughEngineSerialization()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var serialization = Pair.Server.ResolveDependency<ISerializationManager>();
            foreach (var profileId in new[] { "KsProcgenStandardAir", "KsProcgenNitrogenRoom", "KsProcgenVacuum", "KsProcgenPreserveAtmosphere" })
            {
                Assert.That(KsProcgenAtmosphereProfiles.TryResolve(manager, profileId, out var profile, out var issue), Is.True, issue?.Code);
                var spec = profile!.CopySpec();
                var node = serialization.WriteValue(spec, alwaysWrite: true);
                var roundtrip = serialization.Read<KsProcgenAtmosphereSpec>(node, notNullableOverride: true);
                Assert.That(KsProcgenAtmosphereProfiles.TrySnapshot(roundtrip, out var resolved, out issue,
                    sourceProfileId: profileId), Is.True, issue?.Code);
                Assert.That(resolved!.SemanticHash, Is.EqualTo(profile.SemanticHash));
                var request = new KsProcgenRequest { RequestId = "Gas", Atmosphere = roundtrip, Shape = new() { Cells = [new(0, 0)] } };
                var requestNode = serialization.WriteValue(request, alwaysWrite: true);
                var requestRoundtrip = serialization.Read<KsProcgenRequest>(requestNode, notNullableOverride: true);
                Assert.That(KsProcgenGeometry.TryNormalize(requestRoundtrip, out _, out issue), Is.True, issue?.Code);
                Assert.That(requestRoundtrip.Atmosphere!.Mode, Is.EqualTo(profile.Mode));
                if (profile.Mode == KsProcgenAtmosphereMode.Preserve)
                    continue;
                Assert.That(KsProcgenAtmosphereMixturePlanner.TryPlan(profile, 2500f, out var mixture, out issue), Is.True, issue?.Code);
                Assert.That(mixture!.CalculatedPressureKPa, Is.EqualTo(profile.PressureKPa).Within(0.001f));
                Assert.That(mixture.InitialGasVerified, Is.False);
            }
            var missingId = "KsProcgenMissingGasProfile";
            Assert.That(KsProcgenAtmosphereProfiles.TryResolve(manager, missingId, out var missing, out var failure), Is.False);
            Assert.That(missing, Is.Null);
            Assert.That(failure?.Code, Is.EqualTo("UnknownAtmosphereProfile"));
        });
    }

    [Test]
    public async Task PausedPaintSnapshotsRetainGasProfilesAndExportVersionedReviewData()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var mapSystem = Pair.Server.System<SharedMapSystem>();
            var snapshotSystem = Pair.Server.System<KsProcgenAreaSnapshotSystem>();
            var atmosphereId = "KsProcgenNitrogenRoom";
            var atmosphere = manager.Index<KsProcgenAtmosphereProfilePrototype>(atmosphereId);
            var originalPressure = atmosphere.PressureKPa;
            var mapUid = mapSystem.CreateMap(out var mapId, runMapInit: false);
            mapSystem.SetPaused(mapId, true);
            try
            {
                var grid = mapSystem.CreateGridEntity(mapId);
                var markerId = "KsProcgenAreaCell";
                var markerUid = SEntMan.SpawnEntity(markerId, new EntityCoordinates(grid.Owner, 0.5f, 0.5f));
                Assert.That(snapshotSystem.BindMarkerToGrid(grid.Owner, markerUid, new(0, 0)), Is.Null);
                var areaId = "KsProcgenNitrogenOfficeArea";
                SEntMan.GetComponent<KsProcgenAreaCellComponent>(markerUid).Profile = areaId;
                var before = SEntMan.GetEntities().ToHashSet();
                var snapshot = snapshotSystem.Snapshot(grid.Owner, "GasHost");
                Assert.That(snapshot.Status, Is.EqualTo(KsProcgenStatus.Success), snapshot.Issue?.Code);
                var blob = snapshot.Blobs.Single();
                Assert.That(blob.Profile.Atmosphere!.SourceProfileId, Is.EqualTo(atmosphereId));
                Assert.That(blob.CreateRequest(1).Atmosphere!.MoleFractions["Nitrogen"], Is.EqualTo(1f));
                atmosphere.PressureKPa = 200f;
                var changed = snapshotSystem.Snapshot(grid.Owner, "GasHost");
                Assert.That(changed.SnapshotHash, Is.Not.EqualTo(snapshot.SnapshotHash));
                Assert.That(changed.Blobs.Single().Id, Is.EqualTo(blob.Id));
                Assert.That(blob.CreateRequest(1).Atmosphere!.PressureKPa, Is.EqualTo(originalPressure));
                var exported = KsProcgenDiagnosticExport.SerializeArea(snapshot);
                Assert.That(exported.Status, Is.EqualTo(KsProcgenDiagnosticExportStatus.Complete));
                using var json = JsonDocument.Parse(exported.Data);
                Assert.That(json.RootElement.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(3));
                var gasJson = json.RootElement.GetProperty("blobs")[0].GetProperty("atmosphere");
                Assert.That(gasJson.GetProperty("sourceProfileId").GetString(), Is.EqualTo(atmosphereId));
                Assert.That(gasJson.GetProperty("mode").GetString(), Is.EqualTo("Fill"));
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
            }
            finally
            {
                atmosphere.PressureKPa = originalPressure;
                SEntMan.DeleteEntity(mapUid);
            }
        });
    }

    [Test]
    public async Task ThemeInheritanceResolvesGasDefaultsAndSelectionHonorsExplicitOverride()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var themeId = "KsProcgenNitrogenOfficeTheme";
            Assert.That(KsProcgenThemeValidator.TryResolve(manager, themeId, out var theme, out var issue), Is.True, issue?.Code);
            Assert.That(theme!.Atmosphere!.MoleFractions.Single().GasId, Is.EqualTo("Nitrogen"));
            Assert.That(KsProcgenAtmosphereProfiles.TryResolveSelection(manager, null, null, theme.Atmosphere,
                out var inherited, out issue), Is.True, issue?.Code);
            Assert.That(inherited, Is.SameAs(theme.Atmosphere));
            var airId = "KsProcgenStandardAir";
            Assert.That(KsProcgenAtmosphereProfiles.TryResolveSelection(manager, airId, null, inherited,
                out var air, out issue), Is.True, issue?.Code);
            Assert.That(air!.MoleFractions.Count, Is.EqualTo(2));
            Assert.That(KsProcgenAtmosphereProfiles.TryResolveSelection(manager, airId, new() { Mode = KsProcgenAtmosphereMode.Preserve },
                inherited, out var preserve, out issue), Is.True, issue?.Code);
            Assert.That(preserve!.Mode, Is.EqualTo(KsProcgenAtmosphereMode.Preserve));
            Assert.That(preserve.MoleFractions, Is.Empty);
            Assert.That(KsProcgenThemeSelector.TrySelect(manager, themeId, 1, "Room", out var selected, out issue), Is.False);
            Assert.That(issue?.Code, Is.EqualTo("AtmospherePlanningUnsupported"));
            Assert.That(selected, Is.Null);
        });
    }

    [Test]
    public async Task PrimaryPlannersRejectGasRequestsBeforeGeometryWorkOrEngineAllocation()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var before = SEntMan.GetEntities().ToHashSet();
            var request = new KsProcgenRequest
            {
                RequestId = "Gas", AtmosphereProfile = "KsProcgenStandardAir",
                Shape = new() { Cells = [new(0, 0), new(1, 0), new(2, 0)] },
            };
            var themeId = "KsProcgenSimpleOffice";
            var geometry = KsProcgenGeometryPipeline.Plan(manager, request, [], themeId);
            Assert.That(geometry.Status, Is.EqualTo(KsProcgenGeometryPipelineStatus.InvalidInput));
            Assert.That(geometry.Issue?.Code, Is.EqualTo("AtmospherePlanningUnsupported"));
            Assert.That(geometry.Packing, Is.Null);
            Assert.That(geometry.SemanticHash, Is.Zero);
            request.AtmosphereProfile = null;
            var gasThemeId = "KsProcgenNitrogenOfficeTheme";
            var themed = KsProcgenGeometryPipeline.Plan(manager, request, [], gasThemeId);
            Assert.That(themed.Issue?.Code, Is.EqualTo("AtmospherePlanningUnsupported"));
            Assert.That(themed.Packing, Is.Null);
            request.AtmosphereProfile = "KsProcgenStandardAir";
            var owned = KsProcgenOwnedInteriorPlanner.Plan(manager, request, themeId,
                new(new HashSet<Vector2i>(), new HashSet<Vector2i>(), new HashSet<Vector2i>(), true, true), []);
            Assert.That(owned.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.InvalidInput));
            Assert.That(owned.Issue?.Code, Is.EqualTo("AtmospherePlanningUnsupported"));
            Assert.That(owned.ExpandedCells + owned.LayoutProbes, Is.Zero);
            Assert.That(owned.EngineAccessVerified || owned.GasClosureVerified || owned.Published, Is.False);
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }
}
