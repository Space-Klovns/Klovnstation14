using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenContentDraft))]
public sealed class KsProcgenContentDraftTests : KsProcgenGameTest
{
    [Test]
    public async Task CustomInteriorAssetsRoundtripAndFeedOwnedPlanningWithoutRegistration()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var serializer = Pair.Server.ResolveDependency<ISerializationManager>();
            var source = Spec(); var draft = Capture(source);
            var restored = serializer.Read<KsProcgenContentDraftSpec>(serializer.WriteValue(draft.CopySpec(), alwaysWrite: true), notNullableOverride: true);
            Assert.That(Capture(restored).DeclarationHash, Is.EqualTo(draft.DeclarationHash));
            var before = SEntMan.GetEntities().ToHashSet();
            var catalog = Catalog(manager, draft);
            Assert.That(catalog.EntityPacks.Single().Cores.Single().Variants.Count, Is.EqualTo(2));
            Assert.That(catalog.TilePacks.Single().Choices.Single().Declaration.PrimaryTile, Is.EqualTo("FloorSteelMini"));
            var request = new KsProcgenRequest
            {
                RequestId = "CustomOffice", Seed = 13, ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland,
                Shape = new() { AddRectangles = new() { new() { Min = new(0, 0), Max = new(6, 6) } } },
                EntranceDomain = new() { Entrances = new() { new() { PortId = "West", ThresholdCells = new() { new(-1, 2) }, InwardNormal = new(1, 0) } } },
            };
            var context = new KsProcgenGroupRouteContext(new HashSet<Vector2i>(), new HashSet<Vector2i> { new(-1, 2), new(-2, 2) }, new HashSet<Vector2i>(), true, true);
            var budgets = new KsProcgenOwnedPlanBudgets { PreferredMaximumRoomCells = 64 };
            var planned = KsProcgenOwnedInteriorPlanner.Plan(manager, request, "LocalOffice", context, [], budgets: budgets, content: catalog);
            Assert.That(planned.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), planned.Issue?.Code);
            Assert.That(planned.ContentDeclarationHash, Is.EqualTo(catalog.DeclarationHash));
            Assert.That(planned.Furnishings.SelectMany(region => region.Proposal.Entities), Is.Not.Empty);
            source.Tiles["LocalTiles"].Palettes[0].PrimaryTile = "NotARealTile";
            source.Assemblies["LocalChair"].Members[0].Entity = "NotAnEntity";
            var replay = KsProcgenOwnedInteriorPlanner.Plan(manager, request, "LocalOffice", context, [], budgets: budgets, content: catalog);
            Assert.That(replay.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), replay.Issue?.Code);
            Assert.That(replay.SemanticHash, Is.EqualTo(planned.SemanticHash));
            var localThemeId = "LocalOffice";
            Assert.That(manager.HasIndex<KsProcgenRoomThemePrototype>(localThemeId), Is.False);
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task DraftAssetsShadowLoadedNamesOnlyWithinExplicitCapture()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var themeId = "KsProcgenSimpleOffice"; var packId = "KsProcgenOfficeTiles";
            var original = manager.Index<KsProcgenTilePackPrototype>(packId).Palettes[0].PrimaryTile;
            var spec = new KsProcgenContentDraftSpec { DocumentId = "Shadow", Tiles = new() { [packId] = new() { Palettes = new() { new() { Id = "Local", PrimaryTile = "FloorSteelMini" } } } } };
            Assert.That(KsProcgenThemeCatalog.TryCapture(manager, themeId, out var shadow, out var issue, assets: Capture(spec)), Is.True, issue?.Code);
            Assert.That(shadow!.TilePacks.Single().Choices.Single().Id, Is.EqualTo("Local"));
            Assert.That(manager.Index<KsProcgenTilePackPrototype>(packId).Palettes[0].PrimaryTile, Is.EqualTo(original));
            Assert.That(KsProcgenThemeCatalog.TryCapture(manager, themeId, out var loaded, out issue), Is.True, issue?.Code);
            Assert.That(loaded!.TilePacks.Single().Choices.Any(choice => choice.Id == "Local"), Is.False);
        });
    }

    [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)]
    public async Task InvalidEffectiveContentFailsCatalogCaptureWithoutPartialResults(int invalid)
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>(); var spec = Spec();
            switch (invalid)
            {
                case 0: spec.Themes["LocalOffice"].Parent = "MissingParent"; break;
                case 1: spec.Themes["LocalOffice"].Parent = "LocalOffice"; break;
                case 2: spec.Themes["LocalOffice"].FallbackTheme = "LocalOffice"; break;
                case 3: spec.Tiles["LocalTiles"].Palettes[0].PrimaryTile = "MissingTile"; break;
                case 4: spec.Assemblies["LocalChair"].Relations.Add(new() { Id = "Broken", Subject = "MissingMember", Kind = KsProcgenRelationKind.FacingOpenSpace }); break;
                case 5: spec.Entities["LocalContents"].Assemblies[0].Assembly = "MissingAssembly"; break;
            }
            var draft = Capture(spec);
            Assert.That(KsProcgenThemeCatalog.TryCapture(manager, "LocalOffice", out var catalog, out var issue, assets: draft), Is.False);
            Assert.That(catalog, Is.Null); Assert.That(issue, Is.Not.Null);
            Assert.That(draft.CopySpec().Themes.ContainsKey("LocalOffice"), Is.True);
        });
    }

    [Test]
    public async Task DraftAssemblyBindingsAndUtilityDefaultsUseTheSameScopedResolution()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>(); var spec = Spec();
            spec.Assemblies["LocalChair"].Members[0].Entity = null;
            spec.Assemblies["LocalChair"].Members[0].Binding = "Seat";
            spec.Entities["LocalContents"].Assemblies[0].Bindings.Add("Seat", "ChairOfficeLight");
            spec.Themes["LocalOffice"].AtmosphereProfile = "LocalAir";
            var utilities = new KsProcgenUtilityDraftSpec { DocumentId = "Utilities", Atmosphere = new() { ["LocalAir"] = new() { Mode = KsProcgenAtmosphereMode.Preserve } } };
            Assert.That(KsProcgenUtilityDraft.TrySnapshot(utilities, _ => true, id => manager.HasIndex<EntityPrototype>(id), out var utilityDraft, out var issue), Is.True, issue?.Code);
            Assert.That(KsProcgenThemeCatalog.TryCapture(manager, "LocalOffice", out var catalog, out issue, draft: utilityDraft, assets: Capture(spec)), Is.True, issue?.Code);
            Assert.That(catalog!.Theme.Atmosphere!.SourceProfileId, Is.EqualTo("LocalAir"));
            Assert.That(catalog.EntityPacks.Single().Cores.Single().Variants[0].Members[0].Entry.Entity, Is.EqualTo("ChairOfficeLight"));
            spec.Entities["LocalContents"].Assemblies[0].Bindings["Seat"] = "MissingEntity";
            Assert.That(KsProcgenThemeCatalog.TryCapture(manager, "LocalOffice", out var rejected, out issue, draft: utilityDraft, assets: Capture(spec)), Is.False);
            Assert.That(rejected, Is.Null);
        });
    }

    private static KsProcgenContentDraft Capture(KsProcgenContentDraftSpec spec)
    { Assert.That(KsProcgenContentDraft.TrySnapshot(spec, out var draft, out var issue), Is.True, issue?.Code); return draft!; }
    private static KsProcgenThemeCatalog Catalog(IPrototypeManager manager, KsProcgenContentDraft draft)
    { Assert.That(KsProcgenThemeCatalog.TryCapture(manager, "LocalOffice", out var catalog, out var issue, assets: draft), Is.True, issue?.Code); return catalog!; }
    private static KsProcgenContentDraftSpec Spec() => new()
    {
        DocumentId = "InteriorEditor",
        Themes = new() { ["LocalOffice"] = new() { Parent = "KsProcgenSimpleOffice", TilePacks = new() { new() { Pack = "LocalTiles" } }, WallPacks = new() { new() { Pack = "LocalWalls" } },
            LightingPacks = new() { new() { Pack = "LocalLights" } }, EntityPacks = new() { new() { Pack = "LocalContents", MinimumCount = 1 } } } },
        Tiles = new() { ["LocalTiles"] = new() { Palettes = new() { new() { Id = "Mini", PrimaryTile = "FloorSteelMini" } } } },
        Walls = new() { ["LocalWalls"] = new() { Families = new() { new() { Id = "Steel", InteriorWall = "WallSolid", HullWall = "WallReinforced", Door = "Airlock", Window = "Window" } } } },
        Lights = new() { ["LocalLights"] = new() { Fixtures = new() { new() { Id = "Post", Entity = "LightPostSmall", PreferredSpacing = 6 } } } },
        Entities = new() { ["LocalContents"] = new() { Assemblies = new() { new() { Id = "Chair", Assembly = "LocalChair", MinimumCount = 1 } } } },
        Assemblies = new() { ["LocalChair"] = new() { AnchorMember = "Chair", Members = new() { new() { Id = "Chair", Entity = "ChairOfficeLight", Role = KsProcgenEntityRole.Seat, Movement = KsProcgenMovementClass.Clear } },
            Variants = new() { new() { Id = "Alternative", AnchorMember = "Chair", Members = new() { new() { Id = "Chair", Entity = "ChairOfficeLight", Role = KsProcgenEntityRole.Seat, Movement = KsProcgenMovementClass.Clear } } } } } },
    };
}
