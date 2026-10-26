using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenProject))]
public sealed class KsProcgenProjectTests : KsProcgenGameTest
{
    [Test]
    public async Task ProjectImportsReferencedDefinitionsAndRoundtripsAfterLoadedAssetsChange()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var serializer = Pair.Server.ResolveDependency<ISerializationManager>();
            var source = Spec(); var project = Capture(manager, source);
            var portable = project.CopySpec();
            Assert.That(portable.Content!.Themes.ContainsKey("KsProcgenSimpleOffice"), Is.True);
            Assert.That(portable.Content.Tiles.ContainsKey("KsProcgenOfficeTiles"), Is.True);
            Assert.That(portable.Content.Assemblies.Count, Is.GreaterThan(0));
            var packId = "KsProcgenOfficeTiles";
            var loaded = manager.Index<KsProcgenTilePackPrototype>(packId);
            var saved = loaded.Palettes[0].PrimaryTile;
            var before = SEntMan.GetEntities().ToHashSet();
            try
            {
                loaded.Palettes[0].PrimaryTile = "FloorSteelMini";
                source.Areas["Area"].Request.Shape.Cells.Clear();
                var node = serializer.WriteValue(project.CopySpec(), alwaysWrite: true);
                var restored = serializer.Read<KsProcgenProjectSpec>(node, notNullableOverride: true);
                var recaptured = Capture(manager, restored);
                Assert.That(recaptured.DeclarationHash, Is.EqualTo(project.DeclarationHash));
                Assert.That(recaptured.Areas.Single().Request.CopyRequest().Shape.Cells.Count, Is.EqualTo(3));
                Assert.That(recaptured.CopySpec().Content!.Tiles[packId].Palettes[0].PrimaryTile, Is.EqualTo(saved));
                var copy = recaptured.CopySpec(); copy.Content!.Tiles[packId].Palettes[0].PrimaryTile = "NotReal";
                Assert.That(recaptured.CopySpec().Content!.Tiles[packId].Palettes[0].PrimaryTile, Is.EqualTo(saved));
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
            }
            finally { loaded.Palettes[0].PrimaryTile = saved; }
        });
    }

    [Test]
    public async Task RevisionReplacementAdvancesAllSerializedChildrenAndRejectsStaleChanges()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>(); var project = Capture(manager, Spec());
            var replacement = project.CopySpec(); replacement.Areas["Area"].Request.Seed++;
            Assert.That(project.TryRevise(manager, replacement, out var revised, out var issue), Is.True, issue?.Code);
            Assert.That(revised!.Revision, Is.EqualTo(1)); Assert.That(project.Revision, Is.EqualTo(0));
            Assert.That(revised.DeclarationHash, Is.Not.EqualTo(project.DeclarationHash));
            var next = revised.CopySpec();
            Assert.That(next.Content!.Revision, Is.EqualTo(1)); Assert.That(next.Utilities!.Revision, Is.EqualTo(1));
            Assert.That(revised.TryRevise(manager, replacement, out var rejected, out issue), Is.False);
            Assert.That(rejected, Is.Null); Assert.That(issue!.Code, Is.EqualTo("ProjectRevisionConflict"));
            Assert.That(revised.TryRevise(manager, next, out var second, out issue), Is.True, issue?.Code);
            Assert.That(second!.Revision, Is.EqualTo(2));
            Assert.That(second.DeclarationHash, Is.EqualTo(revised.DeclarationHash));
        });
    }

    [Test]
    public async Task ProjectLibraryFeedsExistingGeometryPipelineWithRetainedCustomTheme()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>(); var serializer = Pair.Server.ResolveDependency<ISerializationManager>();
            var source = Spec(); var area = source.Areas["Area"];
            area.Request.Mode = KsProcgenMode.Hybrid; area.Library = "Library"; area.Theme = "LocalOffice";
            source.Content = new() { DocumentId = source.DocumentId, Themes = new() { ["LocalOffice"] = new() { Parent = "KsProcgenSimpleOffice" } } };
            source.Layouts = new()
            {
                DocumentId = source.DocumentId,
                Rooms = new() { ["Tiny"] = new() { SourceKey = "rooms/tiny", ContentFingerprint = "declared-v1", Cells = new() { new(0, 0), new(1, 0) } } },
                Libraries = new() { ["Library"] = new() { Rooms = new() { new() { Room = "Tiny", AllowedQuarterTurns = new() { 0 } } } } },
            };
            var disconnected = Capture(manager, source).Areas.Single();
            var rejected = KsProcgenGeometryPipeline.Plan(manager, disconnected.Request.CopyRequest(), disconnected.Library!.Families,
                disconnected.Content.Theme.Id, content: disconnected.Content);
            Assert.That(rejected.Status, Is.EqualTo(KsProcgenGeometryPipelineStatus.NoPreliminaryRoute));
            Assert.That(rejected.Issue?.Code, Is.EqualTo("DisconnectedPortNetwork"));
            area.Request.RootCells.Add(new(2, 0));
            source.Layouts.Rooms["Tiny"].Ports.Add(new() { Id = "East", Threshold = new(1, 0), OutwardNormal = new(1, 0) });
            var project = Capture(manager, source);
            Assert.That(project.HasUnverifiedRoomSources, Is.True);
            Assert.That(project.RoomBindings.Single().FamilyId, Is.EqualTo("Room/Tiny"));
            var restored = serializer.Read<KsProcgenProjectSpec>(serializer.WriteValue(project.CopySpec(), alwaysWrite: true), notNullableOverride: true);
            Assert.That(Capture(manager, restored).DeclarationHash, Is.EqualTo(project.DeclarationHash));
            var captured = project.Areas.Single();
            var before = SEntMan.GetEntities().ToHashSet();
            var result = KsProcgenGeometryPipeline.Plan(manager, captured.Request.CopyRequest(), captured.Library!.Families,
                captured.Content.Theme.Id, packingBudgets: new() { PreferredPrefabCoveragePercent = 67 }, content: captured.Content);
            Assert.That(result.Status, Is.EqualTo(KsProcgenGeometryPipelineStatus.GeometryPlanned), result.Issue?.Code);
            Assert.That(result.Packing!.PrefabCells, Is.EqualTo(2));
            Assert.That(result.ContentDeclarationHash, Is.EqualTo(captured.Content.DeclarationHash));
            Assert.That(result.HasRoomsWithoutDeclaredPorts, Is.False);
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
            source.Layouts.Rooms["Tiny"].Cells.Clear(); source.Content.Themes.Clear();
            Assert.That(captured.Library.Families.Single().Options.Single().Rooms.Single().Cells.Count, Is.EqualTo(2));
        });
    }

    [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)]
    [TestCase(5)] [TestCase(6)] [TestCase(7)] [TestCase(8)] [TestCase(9)]
    [TestCase(10)] [TestCase(11)] [TestCase(12)] [TestCase(13)]
    public async Task InvalidProjectsReturnNoPartialCapture(int invalid)
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>(); var source = Spec();
            switch (invalid)
            {
                case 0: source.SchemaVersion = 2; break;
                case 1: source.Areas = null!; break;
                case 2: source.Areas["Area"].Request.RequestId = "Mismatch"; break;
                case 3: source.Areas["Area"].Theme = "MissingTheme"; break;
                case 4: source.Areas["Area"].Library = "MissingLibrary"; break;
                case 5: source.Areas["Area"].Request.Mode = KsProcgenMode.Prefabs; break;
                case 6: source.Content = new() { DocumentId = "Foreign" }; break;
                case 7: source.Utilities = new() { DocumentId = source.DocumentId, Revision = 1 }; break;
                case 8: source.Layouts = new() { DocumentId = source.DocumentId, SchemaVersion = 3 }; break;
                case 9: source.Areas["Area"] = null!; break;
                case 10: source.Areas["Area"].Request.SchemaVersion = 3; break;
                case 11: source.Content = new() { DocumentId = source.DocumentId, Themes = new() { ["KsProcgenSimpleOffice"] = new() { Parent = "KsProcgenSimpleOffice" } } }; break;
                case 12: source.DocumentId = "has spaces"; break;
                case 13: source.Areas = Enumerable.Range(0, 65).ToDictionary(index => $"Area{index}", index => new KsProcgenProjectAreaSpec { Theme = "KsProcgenSimpleOffice", Request = new() { RequestId = $"Area{index}" } }); break;
            }
            Assert.That(KsProcgenProject.TryCapture(manager, source, out var project, out var issue), Is.False);
            Assert.That(project, Is.Null); Assert.That(issue, Is.Not.Null);
        });
    }

    [Test]
    public async Task LoadedUtilityDefaultsAreImportedAndRemainExplicitlyUnsupportedForPlanning()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>(); var source = Spec();
            source.Areas["Area"].Theme = "KsProcgenNitrogenOfficeTheme";
            var project = Capture(manager, source);
            Assert.That(project.CopySpec().Utilities!.Atmosphere.ContainsKey("KsProcgenNitrogenRoom"), Is.True);
            var area = project.Areas.Single();
            var result = KsProcgenGeometryPipeline.Plan(manager, area.Request.CopyRequest(), [], area.Content.Theme.Id, content: area.Content);
            Assert.That(result.Status, Is.EqualTo(KsProcgenGeometryPipelineStatus.InvalidInput));
            Assert.That(result.Issue?.Code, Is.EqualTo("AtmospherePlanningUnsupported"));
            Assert.That(result.Packing, Is.Null);
        });
    }

    [Test]
    public async Task AreaMasksShareOneProjectBudgetAndAreaOrderIsCanonical()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>(); var source = Spec();
            source.Areas.Add("Other", new() { Theme = "KsProcgenSimpleOffice", Request = new() { RequestId = "Other" } });
            var hash = Capture(manager, source).DeclarationHash;
            source.Areas = source.Areas.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value);
            Assert.That(Capture(manager, source).DeclarationHash, Is.EqualTo(hash));
            foreach (var area in source.Areas.Values)
            {
                area.Request.Shape = new() { AddRectangles = new() { new() { Min = new(0, 0), Max = new(175, 200) } } };
            }
            Assert.That(KsProcgenProject.TryCapture(manager, source, out var project, out var issue), Is.False);
            Assert.That(project, Is.Null); Assert.That(issue!.Code, Is.EqualTo("ProjectAreaCellBudget"));
        });
    }

    private static KsProcgenProject Capture(IPrototypeManager manager, KsProcgenProjectSpec source)
    { Assert.That(KsProcgenProject.TryCapture(manager, source, out var project, out var issue), Is.True, issue?.Code); return project!; }
    private static KsProcgenProjectSpec Spec() => new()
    {
        DocumentId = "Project", Areas = new() { ["Area"] = new() { Theme = "KsProcgenSimpleOffice", Request = new()
            { RequestId = "Area", Seed = 12, Shape = new() { Cells = new() { new(0, 0), new(1, 0), new(2, 0) } } } } },
    };
}
