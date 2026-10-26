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

[TestOf(typeof(KsProcgenElectricalProfiles))]
public sealed class KsProcgenElectricalTests : KsProcgenGameTest
{
    [Test]
    public async Task LoadedProfilesRoundtripAndInspectRealVoltageConversionCapabilitiesWithoutSpawning()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var factory = Pair.Server.ResolveDependency<IComponentFactory>();
            var serialization = Pair.Server.ResolveDependency<ISerializationManager>();
            var before = SEntMan.GetEntities().ToHashSet();
            foreach (var id in new[] { "KsProcgenAutomaticElectrical", "KsProcgenWiredElectrical", "KsProcgenExistingElectrical", "KsProcgenDisabledElectrical" })
            {
                Assert.That(KsProcgenElectricalProfiles.TryResolve(manager, id, out var profile, out var issue), Is.True, issue?.Code);
                var node = serialization.WriteValue(profile!.CopySpec(), alwaysWrite: true);
                var roundtrip = serialization.Read<KsProcgenElectricalSpec>(node, notNullableOverride: true);
                Assert.That(KsProcgenElectricalProfiles.TrySnapshot(roundtrip, out var restored, out issue, sourceProfileId: id), Is.True, issue?.Code);
                Assert.That(restored!.SemanticHash, Is.EqualTo(profile.SemanticHash));
                var request = new KsProcgenRequest { RequestId = "Power", Electrical = roundtrip, Shape = new() { Cells = [new(0, 0)] } };
                var requestNode = serialization.WriteValue(request, alwaysWrite: true);
                var copied = serialization.Read<KsProcgenRequest>(requestNode, notNullableOverride: true);
                Assert.That(KsProcgenGeometry.TryNormalize(copied, out _, out issue), Is.True, issue?.Code);
                Assert.That(copied.Electrical!.Mode, Is.EqualTo(profile.Mode));
                Assert.That(KsProcgenElectricalCapabilityInspector.TryInspect(manager, factory, profile, out var report, out issue), Is.True, issue?.Message);
                Assert.That(report!.Devices.Count, Is.EqualTo(5));
                Assert.That(report.Devices.Single(device => device.Role == KsProcgenElectricalDeviceRole.LowVoltageCable).TransferRange, Is.EqualTo(3));
                var substation = report.Devices.Single(device => device.Role == KsProcgenElectricalDeviceRole.Substation);
                var apc = report.Devices.Single(device => device.Role == KsProcgenElectricalDeviceRole.Apc);
                Assert.That(substation.MaximumSupply, Is.GreaterThan(0f));
                Assert.That(substation.MaximumChargeRate, Is.GreaterThan(0f));
                Assert.That(apc.BreakerMaximumLoad, Is.GreaterThan(0f));
                Assert.That(apc.BreakerTripSeconds, Is.GreaterThan(0f));
                Assert.That(report.ElectricalTopologyVerified || report.ElectricalSupplyVerified, Is.False);
            }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)]
    public async Task ExistingButWrongPrototypesCannotFulfillCableOrConverterRoles(int defect)
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var factory = Pair.Server.ResolveDependency<IComponentFactory>();
            var spec = new KsProcgenElectricalSpec { Mode = KsProcgenElectricalMode.AutoDistribute };
            switch (defect)
            {
                case 0: spec.HighVoltageCable = "CableMV"; break;
                case 1: spec.MediumVoltageCable = "CableHV"; break;
                case 2: spec.LowVoltageCable = "CableMV"; break;
                case 3: spec.Substations[0].Prototype = "APCBasic"; break;
                case 4: spec.Apcs[0].Prototype = "SubstationBasic"; break;
            }
            Assert.That(KsProcgenElectricalProfiles.TrySnapshot(spec, out var profile, out var issue,
                entityExists: id => manager.HasIndex<EntityPrototype>(id)), Is.True, issue?.Code);
            Assert.That(KsProcgenElectricalCapabilityInspector.TryInspect(manager, factory, profile!, out var report, out issue), Is.False);
            Assert.That(issue?.Code, Is.EqualTo("ElectricalPrototypeCapabilityMismatch"));
            Assert.That(report, Is.Null);
        });
    }

    [Test]
    public async Task PausedPaintRetainsElectricalInterfacesAndExportsAnUnverifiedAuthoringSnapshot()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var mapSystem = Pair.Server.System<SharedMapSystem>();
            var snapshotSystem = Pair.Server.System<KsProcgenAreaSnapshotSystem>();
            var profileId = "KsProcgenAutomaticElectrical";
            var prototype = manager.Index<KsProcgenElectricalProfilePrototype>(profileId);
            var originalInterfaces = prototype.Interfaces;
            var mapUid = mapSystem.CreateMap(out var mapId, runMapInit: false);
            mapSystem.SetPaused(mapId, true);
            try
            {
                prototype.Interfaces = [new() { Id = "Input", NetworkKey = "HV", AvailableWatts = 10000f }];
                var grid = mapSystem.CreateGridEntity(mapId);
                var markerId = "KsProcgenAreaCell";
                var markerUid = SEntMan.SpawnEntity(markerId, new EntityCoordinates(mapUid, 0f, 0f));
                Assert.That(snapshotSystem.BindMarkerToGrid(grid.Owner, markerUid, new(0, 0)), Is.Null);
                var areaId = "KsProcgenPoweredOfficeArea";
                SEntMan.GetComponent<KsProcgenAreaCellComponent>(markerUid).Profile = areaId;
                var before = SEntMan.GetEntities().ToHashSet();
                var snapshot = snapshotSystem.Snapshot(grid.Owner, "PowerHost");
                Assert.That(snapshot.Status, Is.EqualTo(KsProcgenStatus.Success), snapshot.Issue?.Code);
                var blob = snapshot.Blobs.Single();
                Assert.That(blob.Profile.Electrical!.SourceProfileId, Is.EqualTo(profileId));
                prototype.Interfaces[0].AvailableWatts = 5f;
                var changed = snapshotSystem.Snapshot(grid.Owner, "PowerHost");
                Assert.That(changed.SnapshotHash, Is.Not.EqualTo(snapshot.SnapshotHash));
                Assert.That(changed.Blobs.Single().Id, Is.EqualTo(blob.Id));
                Assert.That(blob.CreateRequest(1).Electrical!.Interfaces.Single().AvailableWatts, Is.EqualTo(10000f));
                var exported = KsProcgenDiagnosticExport.SerializeArea(snapshot);
                Assert.That(exported.Status, Is.EqualTo(KsProcgenDiagnosticExportStatus.Complete));
                using var json = JsonDocument.Parse(exported.Data);
                Assert.That(json.RootElement.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(3));
                Assert.That(json.RootElement.GetProperty("planGenerated").GetBoolean(), Is.False);
                var electrical = json.RootElement.GetProperty("blobs")[0].GetProperty("electrical");
                Assert.That(electrical.GetProperty("mode").GetString(), Is.EqualTo("AutoDistribute"));
                Assert.That(electrical.GetProperty("interfaces")[0].GetProperty("availableWatts").GetSingle(), Is.EqualTo(10000f));
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
            }
            finally
            {
                prototype.Interfaces = originalInterfaces;
                SEntMan.DeleteEntity(mapUid);
            }
        });
    }

    [Test]
    public async Task ThemeInheritanceAndInlineSelectionCannotSilentlyLoseElectricalRequirements()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var themeId = "KsProcgenPoweredOfficeTheme";
            Assert.That(KsProcgenThemeValidator.TryResolve(manager, themeId, out var theme, out var issue), Is.True, issue?.Code);
            Assert.That(theme!.Electrical!.Mode, Is.EqualTo(KsProcgenElectricalMode.AutoDistribute));
            Assert.That(KsProcgenElectricalProfiles.TryResolveSelection(manager, null, null, theme.Electrical,
                out var inherited, out issue), Is.True, issue?.Code);
            Assert.That(inherited, Is.SameAs(theme.Electrical));
            var wiredId = "KsProcgenWiredElectrical";
            Assert.That(KsProcgenElectricalProfiles.TryResolveSelection(manager, wiredId, null, inherited,
                out var wired, out issue), Is.True, issue?.Code);
            Assert.That(wired!.SupplyRequirement, Is.EqualTo(KsProcgenElectricalSupply.WiredOnly));
            Assert.That(KsProcgenElectricalProfiles.TryResolveSelection(manager, wiredId, new() { Mode = KsProcgenElectricalMode.Disabled },
                inherited, out var disabled, out issue), Is.True, issue?.Code);
            Assert.That(disabled!.Mode, Is.EqualTo(KsProcgenElectricalMode.Disabled));
            Assert.That(KsProcgenThemeSelector.TrySelect(manager, themeId, 1, "Room", out _, out issue), Is.False);
            Assert.That(issue?.Code, Is.EqualTo("ElectricalPlanningUnsupported"));
            var request = new KsProcgenRequest { RequestId = "Power", Shape = new() { Cells = [new(0, 0)] } };
            var themed = KsProcgenGeometryPipeline.Plan(manager, request, [], themeId);
            Assert.That(themed.Issue?.Code, Is.EqualTo("ElectricalPlanningUnsupported"));
            Assert.That(themed.Packing, Is.Null);
            request.ElectricalProfile = wiredId;
            var ordinaryThemeId = "KsProcgenSimpleOffice";
            var direct = KsProcgenGeometryPipeline.Plan(manager, request, [], ordinaryThemeId);
            Assert.That(direct.Issue?.Code, Is.EqualTo("ElectricalPlanningUnsupported"));
            var owned = KsProcgenOwnedInteriorPlanner.Plan(manager, request, ordinaryThemeId,
                new(new HashSet<Vector2i>(), new HashSet<Vector2i>(), new HashSet<Vector2i>(), true, true), []);
            Assert.That(owned.Issue?.Code, Is.EqualTo("ElectricalPlanningUnsupported"));
            Assert.That(owned.ExpandedCells + owned.LayoutProbes, Is.Zero);
            Assert.That(owned.Published, Is.False);
        });
    }
}
