#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenThemeCatalog))]
public sealed class KsProcgenThemeCatalogTests : KsProcgenGameTest
{
    [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)]
    [TestCase(5)] [TestCase(6)] [TestCase(7)] [TestCase(8)] [TestCase(9)]
    public async Task EveryCandidateAndPolicyChangeInvalidatesNewCatalogsWithoutChangingEarlierViews(int change)
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var themeId = "KsProcgenSimpleOffice";
            var captured = Capture(manager, themeId);
            var theme = manager.Index<KsProcgenRoomThemePrototype>(themeId);
            var tilePackId = captured.TilePacks.Single().Id;
            var wallPackId = captured.WallPacks.Single().Id;
            var lightPackId = captured.LightingPacks.Single().Id;
            var entityPackId = "KsProcgenOfficeWorkstations";
            var tilePack = manager.Index<KsProcgenTilePackPrototype>(tilePackId);
            var wallPack = manager.Index<KsProcgenWallPackPrototype>(wallPackId);
            var lightPack = manager.Index<KsProcgenLightingPackPrototype>(lightPackId);
            var entityPack = manager.Index<KsProcgenEntityPackPrototype>(entityPackId);
            var assembly = manager.Index<KsProcgenAssemblyPrototype>(entityPack.Assemblies.Single().Assembly);
            Func<KsProcgenThemeCatalog, object?> view = null!;
            Action edit = null!;
            Action restore = null!;
            switch (change)
            {
                case 0:
                {
                    var palette = tilePack.Palettes.Last(); var saved = palette.Weight;
                    view = catalog => catalog.TilePacks.Single().Choices.Single(item => item.Id == palette.Id).Weight;
                    edit = () => palette.Weight += 0.5f; restore = () => palette.Weight = saved; break;
                }
                case 1:
                {
                    Assert.That(KsProcgenThemeSelector.TrySelect(captured, 17, "Office", out var selected, out _), Is.True);
                    var palette = tilePack.Palettes.First(item => item.Id != selected!.TilePaletteId);
                    var saved = palette.PrimaryTile;
                    view = catalog => catalog.TilePacks.Single().Choices.Single(item => item.Id == palette.Id).Declaration.PrimaryTile;
                    edit = () => palette.PrimaryTile = saved == "FloorSteel" ? "FloorSteelMini" : "FloorSteel";
                    restore = () => palette.PrimaryTile = saved; break;
                }
                case 2:
                {
                    var saved = wallPack.Families[0].Window;
                    view = catalog => catalog.WallPacks.Single().Choices[0].Declaration.Window;
                    edit = () => wallPack.Families[0].Window = null; restore = () => wallPack.Families[0].Window = saved; break;
                }
                case 3:
                {
                    var saved = lightPack.Fixtures[0].PreferredSpacing;
                    view = catalog => catalog.LightingPacks.Single().Choices[0].Declaration.PreferredSpacing;
                    edit = () => lightPack.Fixtures[0].PreferredSpacing++; restore = () => lightPack.Fixtures[0].PreferredSpacing = saved; break;
                }
                case 4:
                {
                    var saved = entityPack.Tags.ToArray();
                    view = catalog => catalog.EntityPacks.Single(pack => pack.Id == entityPackId).Tags.ToArray();
                    edit = () => entityPack.Tags.Add("CatalogMutation");
                    restore = () => { entityPack.Tags.Clear(); entityPack.Tags.AddRange(saved); }; break;
                }
                case 5:
                {
                    var member = assembly.Members[0]; var saved = member.Footprint.ToArray();
                    view = catalog => catalog.EntityPacks.Single(pack => pack.Id == entityPackId).Cores.Single().Variants[0]
                        .Members.Single(item => item.SourceMemberId == member.Id).Entry.Footprint.ToArray();
                    edit = () => member.Footprint.Add(new(1, 0));
                    restore = () => { member.Footprint.Clear(); member.Footprint.AddRange(saved); }; break;
                }
                case 6:
                {
                    var saved = entityPack.Assemblies[0].MinimumCount;
                    view = catalog => catalog.EntityPacks.Single(pack => pack.Id == entityPackId).Cores.Single().MinimumCount;
                    edit = () => entityPack.Assemblies[0].MinimumCount = 1; restore = () => entityPack.Assemblies[0].MinimumCount = saved; break;
                }
                case 7:
                {
                    var relation = assembly.Relations[0]; var saved = relation.Severity;
                    view = catalog => catalog.EntityPacks.Single(pack => pack.Id == entityPackId).Cores.Single().Variants[0]
                        .Relations.Single(item => item.Id == relation.Id).Severity;
                    edit = () => relation.Severity = saved == KsProcgenRelationSeverity.Required ? KsProcgenRelationSeverity.Preferred : KsProcgenRelationSeverity.Required;
                    restore = () => relation.Severity = saved; break;
                }
                case 8:
                {
                    var saved = theme.Goals!.LightingCoverage;
                    view = catalog => catalog.Theme.Goals.LightingCoverage;
                    edit = () => theme.Goals.LightingCoverage = 0.5f; restore = () => theme.Goals.LightingCoverage = saved; break;
                }
                default:
                {
                    var saved = theme.TilePacks![0].Weight;
                    view = catalog => catalog.Theme.TilePacks[0].Weight;
                    edit = () => theme.TilePacks[0].Weight = 2f; restore = () => theme.TilePacks[0].Weight = saved; break;
                }
            }
            var before = view(captured);
            var hash = captured.DeclarationHash;
            try
            {
                edit();
                Assert.That(Capture(manager, themeId).DeclarationHash, Is.Not.EqualTo(hash));
                Assert.That(captured.DeclarationHash, Is.EqualTo(hash));
                Assert.That(view(captured), Is.EqualTo(before));
            }
            finally { restore(); }
        });
    }

    [Test]
    public async Task UnorderedPoolsTagsAndSignedZeroHaveCanonicalIdentity()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var themeId = "KsProcgenSimpleOffice";
            var theme = manager.Index<KsProcgenRoomThemePrototype>(themeId);
            var tilePackId = "KsProcgenOfficeTiles";
            var packId = "KsProcgenOfficeWorkstations";
            var tiles = manager.Index<KsProcgenTilePackPrototype>(tilePackId);
            var pack = manager.Index<KsProcgenEntityPackPrototype>(packId);
            var savedTiles = tiles.Palettes.ToArray(); var savedRefs = theme.EntityPacks!.ToArray(); var savedTags = pack.Tags.ToArray();
            try
            {
                tiles.Palettes.Add(new() { Id = "Disabled", PrimaryTile = "FloorSteel", Weight = 0f });
                var catalog = Capture(manager, themeId);
                tiles.Palettes.Last().Weight = -0f;
                tiles.Palettes.Reverse(); theme.EntityPacks.Reverse(); pack.Tags.Reverse(); pack.Tags.Add(pack.Tags[0]);
                var permuted = Capture(manager, themeId);
                Assert.That(permuted.DeclarationHash, Is.EqualTo(catalog.DeclarationHash));
                foreach (var seed in Enumerable.Range(0, 16))
                {
                    Assert.That(KsProcgenThemeSelector.TrySelect(catalog, seed, "Room", out var first, out _), Is.True);
                    Assert.That(KsProcgenThemeSelector.TrySelect(permuted, seed, "Room", out var second, out _), Is.True);
                    Assert.That(second!.TilePaletteId, Is.EqualTo(first!.TilePaletteId));
                    Assert.That(second.DominantEntityPackId, Is.EqualTo(first.DominantEntityPackId));
                    Assert.That(second.SupportingEntityPackIds, Is.EqualTo(first.SupportingEntityPackIds));
                }
                Assert.Throws<NotSupportedException>(() => ((IList<KsProcgenCapturedPack<KsProcgenSelectedTile>>) catalog.TilePacks).Clear());
                Assert.Throws<NotSupportedException>(() => ((IList<KsProcgenWeightedDeclaration<KsProcgenSelectedTile>>) catalog.TilePacks[0].Choices)[0] = catalog.TilePacks[0].Choices[0]);
                Assert.Throws<NotSupportedException>(() => ((IList<string>) catalog.EntityPacks[0].Tags).Clear());
            }
            finally
            {
                tiles.Palettes.Clear(); tiles.Palettes.AddRange(savedTiles);
                theme.EntityPacks.Clear(); theme.EntityPacks.AddRange(savedRefs);
                pack.Tags.Clear(); pack.Tags.AddRange(savedTags);
            }
        });
    }

    [Test]
    public async Task OrderedAssemblyAlternativesRemainPartOfTheDeclarationIdentity()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var themeId = "KsProcgenRelationalThemeFixture";
            var assemblyId = "KsProcgenRelationalFixture";
            var assembly = manager.Index<KsProcgenAssemblyPrototype>(assemblyId);
            var saved = assembly.Variants.ToArray();
            try
            {
                var first = assembly.Variants.Single();
                assembly.Variants.Add(new() { Id = "Second", AnchorMember = first.AnchorMember, Members = first.Members, Relations = first.Relations });
                var captured = Capture(manager, themeId);
                assembly.Variants.Reverse();
                var reversed = Capture(manager, themeId);
                Assert.That(reversed.DeclarationHash, Is.Not.EqualTo(captured.DeclarationHash));
                Assert.That(captured.EntityPacks.Single().Cores.Single().Variants.Select(variant => variant.VariantId), Is.EqualTo(new[] { "Base", "Standing", "Second" }));
            }
            finally { assembly.Variants.Clear(); assembly.Variants.AddRange(saved); }
        });
    }

    [Test]
    public async Task OwnedRequestAndCatalogSnapshotsSurviveSourceRemovalAndRejectMismatchedThemes()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var themeId = "KsProcgenSimpleOffice";
            var catalog = Capture(manager, themeId);
            var theme = manager.Index<KsProcgenRoomThemePrototype>(themeId);
            var savedRefs = theme.TilePacks!.ToArray();
            var request = new KsProcgenRequest
            {
                RequestId = "RetainedCatalog", Seed = 23, ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland,
                Shape = new() { Cells = Enumerable.Range(0, 5).SelectMany(x => Enumerable.Range(0, 5).Select(y => new Vector2i(x, y))).ToList() },
                EntranceDomain = new() { Entrances = new() { new() { PortId = "West", ThresholdCells = new() { new(-1, 0) }, InwardNormal = new(1, 0) } } },
            };
            Assert.That(KsProcgenRequestSnapshot.TryCapture(manager, request, out var snapshot, out var issue), Is.True, issue?.Code);
            var context = new KsProcgenGroupRouteContext(new HashSet<Vector2i>(), new HashSet<Vector2i> { new(-1, 0), new(-2, 0) }, new HashSet<Vector2i>(), true, true);
            var budgets = new KsProcgenOwnedPlanBudgets { PreferredMaximumRoomCells = 64 };
            var baseline = KsProcgenOwnedInteriorPlanner.PlanSnapshot(manager, snapshot!, themeId, context, [], budgets: budgets, content: catalog);
            Assert.That(baseline.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), baseline.Issue?.Code);
            var entities = SEntMan.GetEntities().ToHashSet();
            try
            {
                theme.TilePacks.Clear();
                var retained = KsProcgenOwnedInteriorPlanner.PlanSnapshot(manager, snapshot!, themeId, context, [], budgets: budgets, content: catalog);
                Assert.That(retained.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), retained.Issue?.Code);
                Assert.That(retained.SemanticHash, Is.EqualTo(baseline.SemanticHash));
                Assert.That(retained.ContentDeclarationHash, Is.EqualTo(catalog.DeclarationHash));
                Assert.That(retained.ExpandedCells, Is.EqualTo(baseline.ExpandedCells));
                Assert.That(KsProcgenThemeCatalog.TryCapture(manager, themeId, out var rejected, out issue), Is.False);
                Assert.That(rejected, Is.Null);
                var mismatch = KsProcgenOwnedInteriorPlanner.PlanSnapshot(manager, snapshot!, "Other", context, [], content: catalog);
                Assert.That(mismatch.Issue?.Code, Is.EqualTo("MismatchedThemeCatalog"));
                Assert.That(mismatch.LayoutProbes, Is.Zero);
                var invalid = KsProcgenOwnedInteriorPlanner.PlanSnapshot(manager, snapshot!, null!, context, []);
                Assert.That(invalid.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.InvalidInput));
                Assert.That(invalid.ContentDeclarationHash, Is.Zero);
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(entities));
            }
            finally { theme.TilePacks.Clear(); theme.TilePacks.AddRange(savedRefs); }
        });
    }

    [TestCase("KsProcgenNitrogenOfficeTheme", "AtmospherePlanningUnsupported")]
    [TestCase("KsProcgenPoweredOfficeTheme", "ElectricalPlanningUnsupported")]
    public async Task CapturedUtilityDefaultsCannotBypassUnimplementedPlanningGuards(string themeId, string code)
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var content = Capture(manager, themeId);
            Assert.That(KsProcgenThemeSelector.TrySelect(content, 1, "Room", out var selected, out var issue), Is.False);
            Assert.That(selected, Is.Null);
            Assert.That(issue?.Code, Is.EqualTo(code));
            var request = new KsProcgenRequest { RequestId = "UtilityCatalog", Shape = new() { Cells = new() { new(0, 0) } } };
            Assert.That(KsProcgenRequestSnapshot.TryCapture(manager, request, out var snapshot, out issue), Is.True, issue?.Code);
            var context = new KsProcgenGroupRouteContext(new HashSet<Vector2i>(), new HashSet<Vector2i>(), new HashSet<Vector2i>(), true, true);
            var result = KsProcgenOwnedInteriorPlanner.PlanSnapshot(manager, snapshot!, themeId, context, [], content: content);
            Assert.That(result.Issue?.Code, Is.EqualTo(code));
            Assert.That(result.LayoutProbes, Is.Zero);
            Assert.That(result.ExpandedCells, Is.Zero);
            Assert.That(result.Published || result.GasClosureVerified || result.WorkingLightingVerified, Is.False);
        });
    }

    private static KsProcgenThemeCatalog Capture(IPrototypeManager manager, string themeId)
    {
        Assert.That(KsProcgenThemeCatalog.TryCapture(manager, themeId, out var catalog, out var issue), Is.True, issue?.Code);
        return catalog!;
    }
}
